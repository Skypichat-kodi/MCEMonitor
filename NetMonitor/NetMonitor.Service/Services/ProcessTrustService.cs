using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Services;

/// <summary>
/// Vérifie si un processus est approuvé (chemin + hash + signature Authenticode).
/// Cache 5 min par PID pour ne pas re-hasher à chaque paquet.
/// </summary>
public sealed class ProcessTrustService
{
    private readonly ConcurrentDictionary<int, (ProcessTrustResult Result, DateTime CachedAt)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly List<ProcessTrustInfo> _whitelist = new();
    private readonly object _whitelistLock = new();
    private readonly string _whitelistPath;

    // ============================================================
    //  P/Invoke
    // ============================================================
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // SeDebugPrivilege
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
        ref TokPriv1Luid newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TokPriv1Luid
    {
        public int Count;
        public long Luid;
        public int Attr;
    }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const int SE_PRIVILEGE_ENABLED = 0x0002;

    // WinVerifyTrust
    [DllImport("wintrust.dll", PreserveSig = true, SetLastError = false)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    // ============================================================
    //  Constructeur
    // ============================================================
    public ProcessTrustService()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor");

        _whitelistPath = Path.Combine(baseDir, "trusted_processes.json");

        // ⚠ Active SeDebugPrivilege au démarrage
        EnableDebugPrivilege();

        EnsureWhitelistFileExists(baseDir);
        LoadWhitelist();
    }

    // ============================================================
    //  SeDebugPrivilege
    // ============================================================
    private static void EnableDebugPrivilege()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
                return;

            try
            {
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out long luid))
                    return;

                var tp = new TokPriv1Luid
                {
                    Count = 1,
                    Luid = luid,
                    Attr = SE_PRIVILEGE_ENABLED
                };

                if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                    return;

                int err = Marshal.GetLastWin32Error();
                if (err == 0)
                    CoreLog.Info("ProcessTrust : SeDebugPrivilege activé");
                else
                    CoreLog.Warn($"ProcessTrust : SeDebugPrivilege non activé (Win32={err})");
            }
            finally { CloseHandle(hToken); }
        }
        catch (Exception ex)
        {
            CoreLog.Warn("ProcessTrust.EnableDebugPrivilege : " + ex.Message);
        }
    }

    // ============================================================
    //  Chargement de la whitelist
    // ============================================================
    public void Reload()
    {
        LoadWhitelist();
        _cache.Clear();
    }

    public bool AddToWhitelist(ProcessTrustInfo entry)
    {
        try
        {
            List<ProcessTrustInfo> list = new();
            if (File.Exists(_whitelistPath))
            {
                string json = File.ReadAllText(_whitelistPath);
                list = JsonSerializer.Deserialize<List<ProcessTrustInfo>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }

            if (list.Any(e => string.Equals(NormalizePath(e.Path),
                                            NormalizePath(entry.Path),
                                            StringComparison.OrdinalIgnoreCase)))
                return true;

            // Enrichit automatiquement si absent
            if (string.IsNullOrWhiteSpace(entry.Sha256))
                entry.Sha256 = TryComputeSha256(entry.Path);

            if (string.IsNullOrWhiteSpace(entry.Publisher))
                entry.Publisher = TryGetAuthenticodePublisher(entry.Path);

            list.Add(entry);

            string outJson = JsonSerializer.Serialize(list,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_whitelistPath, outJson);

            Reload();
            CoreLog.Info($"ProcessTrust : {entry.Name} ajouté à la whitelist ({entry.Path})");
            return true;
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"ProcessTrust.AddToWhitelist : {ex.Message}");
            return false;
        }
    }

    public List<ProcessTrustInfo> GetAll()
    {
        try
        {
            if (!File.Exists(_whitelistPath))
                return new List<ProcessTrustInfo>();

            string json = File.ReadAllText(_whitelistPath);
            return JsonSerializer.Deserialize<List<ProcessTrustInfo>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<ProcessTrustInfo>();
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"ProcessTrust.GetAll : {ex.Message}");
            return new List<ProcessTrustInfo>();
        }
    }

    public bool RemoveFromWhitelist(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var list = GetAll();
            int removed = list.RemoveAll(e =>
                string.Equals(NormalizePath(e.Path), NormalizePath(path), StringComparison.OrdinalIgnoreCase));

            if (removed == 0) return false;

            SaveList(list);
            Reload();
            CoreLog.Info($"ProcessTrust : entrée retirée de la whitelist ({path})");
            return true;
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"ProcessTrust.RemoveFromWhitelist : {ex.Message}");
            return false;
        }
    }

    public bool SetEnabled(string path, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var list = GetAll();
            var entry = list.FirstOrDefault(e =>
                string.Equals(NormalizePath(e.Path), NormalizePath(path), StringComparison.OrdinalIgnoreCase));

            if (entry == null) return false;
            if (entry.Enabled == enabled) return true;

            entry.Enabled = enabled;

            SaveList(list);
            Reload();
            CoreLog.Info($"ProcessTrust : entrée {(enabled ? "activée" : "désactivée")} ({path})");
            return true;
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"ProcessTrust.SetEnabled : {ex.Message}");
            return false;
        }
    }

    private void SaveList(List<ProcessTrustInfo> list)
    {
        string json = JsonSerializer.Serialize(list,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_whitelistPath, json);
    }

    private void LoadWhitelist()
    {
        try
        {
            if (!File.Exists(_whitelistPath))
            {
                lock (_whitelistLock) _whitelist.Clear();
                return;
            }

            string json = File.ReadAllText(_whitelistPath);
            var list = JsonSerializer.Deserialize<List<ProcessTrustInfo>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            lock (_whitelistLock)
            {
                _whitelist.Clear();
                if (list != null)
                    _whitelist.AddRange(list.Where(e => e.Enabled));
            }

            CoreLog.Info($"ProcessTrust : {_whitelist.Count} entrée(s) whitelist chargée(s)");
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"ProcessTrust.LoadWhitelist : {ex.Message}");
        }
    }

    private static void EnsureWhitelistFileExists(string baseDir)
    {
        try
        {
            Directory.CreateDirectory(baseDir);
            string path = Path.Combine(baseDir, "trusted_processes.json");
            if (File.Exists(path)) return;

            var defaults = new[]
            {
                new ProcessTrustInfo { Name = "svchost",  Path = @"C:\Windows\System32\svchost.exe",  Enabled = true },
                new ProcessTrustInfo { Name = "explorer", Path = @"C:\Windows\explorer.exe",           Enabled = true },
                new ProcessTrustInfo { Name = "lsass",    Path = @"C:\Windows\System32\lsass.exe",     Enabled = true },
                new ProcessTrustInfo { Name = "services", Path = @"C:\Windows\System32\services.exe",  Enabled = true },
                new ProcessTrustInfo { Name = "winlogon", Path = @"C:\Windows\System32\winlogon.exe",  Enabled = true }
            };

            string json = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            CoreLog.Info($"ProcessTrust : fichier whitelist créé ({path})");
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"ProcessTrust.EnsureWhitelistFileExists : {ex.Message}");
        }
    }

    // ============================================================
    //  Vérification d'un processus
    // ============================================================
    public ProcessTrustResult CheckProcess(int pid)
    {
        if (pid <= 0) return new ProcessTrustResult { Level = ProcessTrustLevel.System, ProcessName = "?" };
        if (pid == 4) return new ProcessTrustResult { Level = ProcessTrustLevel.System, ProcessName = "System" };

        if (_cache.TryGetValue(pid, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            return cached.Result;

        var result = CheckProcessInternal(pid);
        _cache[pid] = (result, DateTime.UtcNow);
        return result;
    }

    private ProcessTrustResult CheckProcessInternal(int pid)
    {
        // 1. Résout le nom
        string name;
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
        }
        catch
        {
            return new ProcessTrustResult
            {
                Level = ProcessTrustLevel.Unknown,
                ProcessName = "<inaccessible>",
                Reason = "processus inaccessible"
            };
        }

        // 2. Cherche les entrées whitelist qui matchent ce nom
        List<ProcessTrustInfo> candidates;
        lock (_whitelistLock)
        {
            candidates = _whitelist
                .Where(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // 3. Résout le chemin complet
        string? fullPath = TryGetFullPath(pid);

        // ⚠ 4. AUTO-TRUST MICROSOFT (avant tout le reste)
        // Élimine les faux positifs OneDrive, Office, Edge, GameBar...
        if (fullPath != null && IsSignedBy(fullPath, "Microsoft Corporation"))
        {
            return new ProcessTrustResult
            {
                Level = ProcessTrustLevel.Trusted,
                ProcessName = name,
                FullPath = fullPath,
                Publisher = "Microsoft Corporation",
                Reason = "signé Microsoft (auto-trust)",
                MatchedEntry = "<auto:Microsoft>"
            };
        }

        // 5. Aucun candidat dans la whitelist → Unknown
        if (candidates.Count == 0)
        {
            return new ProcessTrustResult
            {
                Level = ProcessTrustLevel.Unknown,
                ProcessName = name,
                FullPath = fullPath,
                Reason = "nom absent de la whitelist"
            };
        }

        // 6. Chemin inaccessible → Unknown, mais on escalade si droits insuffisants
        if (fullPath == null)
        {
            return new ProcessTrustResult
            {
                Level = ProcessTrustLevel.Unknown,
                ProcessName = name,
                Reason = "chemin inaccessible (droits insuffisants ?)"
            };
        }

        // 7. Cherche l'entrée dont le chemin correspond
        var pathMatch = candidates.FirstOrDefault(e =>
            string.Equals(NormalizePath(e.Path), NormalizePath(fullPath), StringComparison.OrdinalIgnoreCase));

        if (pathMatch == null)
        {
            return new ProcessTrustResult
            {
                Level = ProcessTrustLevel.Spoofed,
                ProcessName = name,
                FullPath = fullPath,
                Reason = $"chemin non whitelisté : {fullPath}"
            };
        }

        // 8. Vérifie le hash si demandé
        string? actualHash = null;
        if (!string.IsNullOrWhiteSpace(pathMatch.Sha256))
        {
            actualHash = TryComputeSha256(fullPath);
            if (actualHash == null)
            {
                return new ProcessTrustResult
                {
                    Level = ProcessTrustLevel.Unknown,
                    ProcessName = name,
                    FullPath = fullPath,
                    Reason = "impossible de hasher le binaire"
                };
            }

            if (!string.Equals(actualHash, pathMatch.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessTrustResult
                {
                    Level = ProcessTrustLevel.Spoofed,
                    ProcessName = name,
                    FullPath = fullPath,
                    Sha256 = actualHash,
                    Reason = "hash SHA-256 différent de la whitelist"
                };
            }
        }

        // 9. Vérifie la signature Authenticode si demandé
        string? publisher = null;
        if (!string.IsNullOrWhiteSpace(pathMatch.Publisher))
        {
            publisher = TryGetAuthenticodePublisher(fullPath);
            if (publisher == null)
            {
                return new ProcessTrustResult
                {
                    Level = ProcessTrustLevel.Spoofed,
                    ProcessName = name,
                    FullPath = fullPath,
                    Sha256 = actualHash,
                    Reason = "signature Authenticode absente ou invalide"
                };
            }

            if (!publisher.Contains(pathMatch.Publisher, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessTrustResult
                {
                    Level = ProcessTrustLevel.Spoofed,
                    ProcessName = name,
                    FullPath = fullPath,
                    Sha256 = actualHash,
                    Publisher = publisher,
                    Reason = $"éditeur '{publisher}' ne contient pas '{pathMatch.Publisher}'"
                };
            }
        }

        return new ProcessTrustResult
        {
            Level = ProcessTrustLevel.Trusted,
            ProcessName = name,
            FullPath = fullPath,
            Sha256 = actualHash,
            Publisher = publisher,
            MatchedEntry = pathMatch.Name
        };
    }

    // ============================================================
    //  Utilitaires
    // ============================================================
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return path.Replace('/', '\\').TrimEnd(new char[] { '\\' });
    }

    private static string? TryGetFullPath(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            CoreLog.Debug($"[TRUST] OpenProcess(pid={pid}) échec, Win32={err}");
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;

            if (!QueryFullProcessImageName(h, 0, sb, ref size))
                return null;

            int len = (int)size;
            if (len <= 0) return null;

            var chars = new char[len];
            sb.CopyTo(0, chars, 0, len);

            return new string(chars);
        }
        finally { CloseHandle(h); }
    }

    private static string? TryComputeSha256(string path)
    {
        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch { return null; }
    }

    /// <summary>
    /// Vérifie la signature Authenticode et retourne le SUBJECT du certificat signataire
    /// (ex. "CN=Microsoft Corporation, O=Microsoft Corporation, ...").
    /// ⚠ Correctif : retourne le vrai subject, plus le nom de fichier.
    /// </summary>
    private static string? TryGetAuthenticodePublisher(string filePath)
    {
        try
        {
            IntPtr pInfo = IntPtr.Zero;
            IntPtr pWvtData = IntPtr.Zero;

            try
            {
                var wvtFile = new WinTrustFileInfo
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                    pcwszFilePath = filePath,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                };

                pInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(wvtFile, pInfo, false);

                var wvtData = new WinTrustData
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = 2,             // WTD_UI_NONE
                    fdwRevocationChecks = 0,    // WTD_REVOKE_NONE
                    dwUnionChoice = 1,          // WTD_CHOICE_FILE
                    pFile = pInfo,
                    dwStateAction = 0,          // WTD_STATEACTION_VERIFY
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = 0x00000010,   // WTD_CACHE_ONLY_URL_RETRIEVAL
                    dwUIContext = 0
                };

                pWvtData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
                Marshal.StructureToPtr(wvtData, pWvtData, false);

                uint result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pWvtData);

                // ⚠ TOUJOURS libérer l'état, même en cas d'échec
                try
                {
                    wvtData.dwStateAction = 2; // WTD_STATEACTION_CLOSE
                    Marshal.StructureToPtr(wvtData, pWvtData, false);
                    WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pWvtData);
                }
                catch { }

                if (result != 0) return null;

                // ⚠ Retourne le VRAI subject du certificat, plus le nom de fichier
                try
                {
                    using var cert = X509Certificate.CreateFromSignedFile(filePath);
                    return cert.Subject ?? "";
                }
                catch
                {
                    return null;
                }
            }
            finally
            {
                if (pWvtData != IntPtr.Zero) Marshal.FreeHGlobal(pWvtData);
                if (pInfo != IntPtr.Zero) Marshal.FreeHGlobal(pInfo);
            }
        }
        catch { return null; }
    }

    /// <summary>
    /// Vérifie que le fichier est signé Authenticode ET que le subject du certificat
    /// contient la sous-chaîne attendue (ex. "Microsoft").
    /// </summary>
    public static bool IsSignedBy(string filePath, string publisherSubstring)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            string? subject = TryGetAuthenticodePublisher(filePath);
            if (string.IsNullOrEmpty(subject))
                return false;

            return subject.Contains(publisherSubstring, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }
}