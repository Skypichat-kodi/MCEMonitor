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

    private static readonly object _auditLock = new();

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
    private const uint TOKEN_QUERY              = 0x0008;
    private const int  SE_PRIVILEGE_ENABLED     = 0x0002;

    // WinVerifyTrust
    [DllImport("wintrust.dll", PreserveSig = true, SetLastError = false)]
    private static extern uint WinVerifyTrust(IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

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
            if (!OpenProcessToken(GetCurrentProcess(),
                                  TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
                                  out IntPtr hToken))
                return;

            try
            {
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out long luid))
                    return;

                var tp = new TokPriv1Luid
                {
                    Count = 1,
                    Luid  = luid,
                    Attr  = SE_PRIVILEGE_ENABLED
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

            // Enrichit automatiquement si absent
            if (string.IsNullOrWhiteSpace(entry.Sha256))
                entry.Sha256 = TryComputeSha256(entry.Path) ?? "";

            if (string.IsNullOrWhiteSpace(entry.Publisher))
                entry.Publisher = TryGetAuthenticodePublisher(entry.Path) ?? "";

            // Cas 1 : même chemin exact existe déjà → rien à faire
            if (list.Any(e => string.Equals(NormalizePath(e.Path),
                                            NormalizePath(entry.Path),
                                            StringComparison.OrdinalIgnoreCase)))
                return true;

            // Cas 2 : même nom + même éditeur → mise à jour (changement de dossier)
            string? newPubName = GetPublisherName(entry.Publisher);
            var existingByNameAndPub = list.FirstOrDefault(e =>
            {
                if (!string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
                    return false;

                string? existingPubName = GetPublisherName(e.Publisher);
                return !string.IsNullOrWhiteSpace(existingPubName)
                    && !string.IsNullOrWhiteSpace(newPubName)
                    && existingPubName.Equals(newPubName, StringComparison.OrdinalIgnoreCase);
            });

            if (existingByNameAndPub != null)
            {
                CoreLog.Info($"[TRUST] Mise à jour de '{entry.Name}' : " +
                             $"chemin {existingByNameAndPub.Path} → {entry.Path}");
                existingByNameAndPub.Path      = entry.Path;
                existingByNameAndPub.Sha256    = entry.Sha256;
                existingByNameAndPub.Publisher = entry.Publisher;
            }
            else
            {
                // Cas 3 : nouvelle entrée (vraiment inconnue)
                list.Add(entry);
            }

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

            string json = JsonSerializer.Serialize(defaults,
                new JsonSerializerOptions { WriteIndented = true });
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
                Level       = ProcessTrustLevel.Unknown,
                ProcessName = "<inaccessible>",
                Reason      = "processus inaccessible"
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

        // 3. Résout le chemin complet (avec capture de l'erreur)
        string? fullPath = TryGetFullPath(pid, out int pathError);

        if (CoreLog.Verbose)
        {
            CoreLog.Debug($"[TRUST] PID {pid} ({name}) → path={fullPath ?? "<null>"}, win32err={pathError}");
        }

        // 4. AUTO-TRUST MICROSOFT (avant tout le reste)
        if (fullPath != null && IsSignedBy(fullPath, "Microsoft Corporation"))
        {
            CoreLog.Debug($"[TRUST] {name} auto-trusté (signature Microsoft)");

            // Log d'audit visible en prod (fichier séparé pour ne pas polluer le log principal)
            AuditAutoTrust(name, fullPath, "Microsoft Corporation");

            return new ProcessTrustResult
            {
                Level        = ProcessTrustLevel.Trusted,
                ProcessName  = name,
                FullPath     = fullPath,
                Publisher    = "Microsoft Corporation",
                Reason       = "signé Microsoft (auto-trust)",
                MatchedEntry = "<auto:Microsoft>"
            };
        }

        // 5. Chemin inaccessible → Unknown avec Win32Error
        if (fullPath == null)
        {
            return new ProcessTrustResult
            {
                Level       = ProcessTrustLevel.Unknown,
                ProcessName = name,
                Win32Error  = pathError,
                Reason      = pathError == 5
                    ? "chemin inaccessible (droits insuffisants ?)"
                    : "chemin inaccessible (processus terminé ?)"
            };
        }

        // 6. Aucun candidat dans la whitelist → Unknown
        if (candidates.Count == 0)
        {
            return new ProcessTrustResult
            {
                Level       = ProcessTrustLevel.Unknown,
                ProcessName = name,
                FullPath    = fullPath,
                Reason      = "nom absent de la whitelist"
            };
        }

        // 7. Cherche l'entrée dont le chemin correspond EXACTEMENT
        var pathMatch = candidates.FirstOrDefault(e =>
            string.Equals(NormalizePath(e.Path), NormalizePath(fullPath), StringComparison.OrdinalIgnoreCase));

        // ============================================================
        //  CAS A : chemin identique → vérification hash + signature
        // ============================================================
        if (pathMatch != null)
        {
            // 8. Vérifie le hash si demandé
            string? actualHash = null;
            if (!string.IsNullOrWhiteSpace(pathMatch.Sha256))
            {
                actualHash = TryComputeSha256(fullPath);
                if (actualHash == null)
                {
                    return new ProcessTrustResult
                    {
                        Level       = ProcessTrustLevel.Unknown,
                        ProcessName = name,
                        FullPath    = fullPath,
                        Reason      = "impossible de hasher le binaire"
                    };
                }

                if (!string.Equals(actualHash, pathMatch.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    // Le hash a changé. Vérifier si c'est une MAJ légitime (signature OK)
                    string? newPublisher       = TryGetAuthenticodePublisher(fullPath);
                    string? entryPublisherName = GetPublisherName(pathMatch.Publisher);

                    if (newPublisher != null
                        && !string.IsNullOrWhiteSpace(entryPublisherName)
                        && newPublisher.Contains(entryPublisherName, StringComparison.OrdinalIgnoreCase))
                    {
                        // MAJ légitime : auto-update du hash en base
                        CoreLog.Info($"[TRUST] {pathMatch.Name} : MAJ détectée " +
                                     $"(éditeur '{entryPublisherName}' OK), auto-update du hash");
                        UpdateWhitelistHash(pathMatch.Name, pathMatch.Path, actualHash);

                        return new ProcessTrustResult
                        {
                            Level        = ProcessTrustLevel.Trusted,
                            ProcessName  = name,
                            FullPath     = fullPath,
                            Sha256       = actualHash,
                            Publisher    = newPublisher,
                            MatchedEntry = pathMatch.Name,
                            Reason       = "MAJ auto (signature éditeur vérifiée)"
                        };
                    }

                    // Hash changé + signature absente/différente → Spoofed
                    return new ProcessTrustResult
                    {
                        Level       = ProcessTrustLevel.Spoofed,
                        ProcessName = name,
                        FullPath    = fullPath,
                        Sha256      = actualHash,
                        Publisher   = newPublisher,
                        Reason      = newPublisher == null
                            ? "hash différent et binaire non signé"
                            : $"hash différent et éditeur '{newPublisher}' ≠ '{entryPublisherName}'"
                    };
                }
            }

            // 9. Vérifie la signature Authenticode
            string? publisher = null;
            if (!string.IsNullOrWhiteSpace(pathMatch.Publisher))
            {
                publisher = TryGetAuthenticodePublisher(fullPath);
                if (publisher == null)
                {
                    return new ProcessTrustResult
                    {
                        Level       = ProcessTrustLevel.Spoofed,
                        ProcessName = name,
                        FullPath    = fullPath,
                        Sha256      = actualHash,
                        Reason      = "signature Authenticode absente ou invalide"
                    };
                }

                string? expectedPublisherName = GetPublisherName(pathMatch.Publisher);
                if (!string.IsNullOrWhiteSpace(expectedPublisherName)
                    && !publisher.Contains(expectedPublisherName, StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessTrustResult
                    {
                        Level       = ProcessTrustLevel.Spoofed,
                        ProcessName = name,
                        FullPath    = fullPath,
                        Sha256      = actualHash,
                        Publisher   = publisher,
                        Reason      = $"éditeur '{publisher}' ne contient pas '{expectedPublisherName}'"
                    };
                }
            }

            return new ProcessTrustResult
            {
                Level        = ProcessTrustLevel.Trusted,
                ProcessName  = name,
                FullPath     = fullPath,
                Sha256       = actualHash,
                Publisher    = publisher,
                MatchedEntry = pathMatch.Name
            };
        }

        // ============================================================
        //  CAS B : chemin différent mais nom whitelisté
        //          → vérifier si c'est un changement de dossier
        //            d'un éditeur déjà connu (contrôle manuel)
        // ============================================================
        string? changedPublisher = TryGetAuthenticodePublisher(fullPath);

        if (changedPublisher != null)
        {
            // Cherche une entrée du même nom avec un publisher compatible
            var publisherMatch = candidates.FirstOrDefault(e =>
            {
                string? entryPubName = GetPublisherName(e.Publisher);
                return !string.IsNullOrWhiteSpace(entryPubName)
                    && changedPublisher.Contains(entryPubName, StringComparison.OrdinalIgnoreCase);
            });

            if (publisherMatch != null)
            {
                // Changement de répertoire d'un éditeur connu → contrôle manuel
                string editorName = GetPublisherName(publisherMatch.Publisher) ?? "?";
                return new ProcessTrustResult
                {
                    Level       = ProcessTrustLevel.Unknown,
                    ProcessName = name,
                    FullPath    = fullPath,
                    Publisher   = changedPublisher,
                    Reason      = $"changement de répertoire (éditeur '{editorName}' vérifié) — validation requise"
                };
            }

            // Nom whitelisté mais éditeur totalement différent → Spoofed
            return new ProcessTrustResult
            {
                Level       = ProcessTrustLevel.Spoofed,
                ProcessName = name,
                FullPath    = fullPath,
                Publisher   = changedPublisher,
                Reason      = $"chemin non whitelisté ET éditeur '{changedPublisher}' ≠ entrées connues"
            };
        }

        // Binaire non signé au nom whitelisté → Spoofed
        return new ProcessTrustResult
        {
            Level       = ProcessTrustLevel.Spoofed,
            ProcessName = name,
            FullPath    = fullPath,
            Reason      = $"chemin non whitelisté ET binaire non signé : {fullPath}"
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

    private static string? TryGetFullPath(int pid, out int win32Error)
    {
        win32Error = 0;
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
        {
            win32Error = Marshal.GetLastWin32Error();
            CoreLog.Debug($"[TRUST] OpenProcess(pid={pid}) échec, Win32={win32Error}");
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!QueryFullProcessImageName(h, 0, sb, ref size))
            {
                win32Error = Marshal.GetLastWin32Error();
                return null;
            }

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
    /// </summary>
    private static string? TryGetAuthenticodePublisher(string filePath)
    {
        try
        {
            IntPtr pInfo    = IntPtr.Zero;
            IntPtr pWvtData = IntPtr.Zero;

            try
            {
                var wvtFile = new WinTrustFileInfo
                {
                    cbStruct       = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                    pcwszFilePath  = filePath,
                    hFile          = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                };

                pInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(wvtFile, pInfo, false);

                var wvtData = new WinTrustData
                {
                    cbStruct            = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData      = IntPtr.Zero,
                    dwUIChoice          = 2,             // WTD_UI_NONE
                    fdwRevocationChecks = 0,             // WTD_REVOKE_NONE
                    dwUnionChoice       = 1,             // WTD_CHOICE_FILE
                    pFile               = pInfo,
                    dwStateAction       = 0,             // WTD_STATEACTION_VERIFY
                    hWVTStateData       = IntPtr.Zero,
                    pwszURLReference    = IntPtr.Zero,
                    dwProvFlags         = 0x00000010,    // WTD_CACHE_ONLY_URL_RETRIEVAL
                    dwUIContext         = 0
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
                if (pInfo    != IntPtr.Zero) Marshal.FreeHGlobal(pInfo);
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
            using var cert = X509Certificate.CreateFromSignedFile(filePath);
            return cert.Subject.Contains(publisherSubstring, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Met à jour le hash d'une entrée whitelist (après MAJ légitime détectée).
    /// Utilise le nom + chemin pour identifier l'entrée de manière unique.
    /// </summary>
    private void UpdateWhitelistHash(string name, string path, string newHash)
    {
        try
        {
            var list = GetAll();
            var entry = list.FirstOrDefault(e =>
                string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizePath(e.Path), NormalizePath(path), StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                CoreLog.Warn($"[TRUST] UpdateWhitelistHash : entrée '{name}' introuvable ({path})");
                return;
            }

            if (string.Equals(entry.Sha256, newHash, StringComparison.OrdinalIgnoreCase))
                return;   // rien à faire

            entry.Sha256 = newHash;
            SaveList(list);
            Reload();
            CoreLog.Info($"[TRUST] Hash mis à jour pour {name} : {newHash.Substring(0, 12)}…");
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"[TRUST] UpdateWhitelistHash : {ex.Message}");
        }
    }

    /// <summary>
    /// Extrait le nom d'éditeur depuis un Subject X509.
    /// Ex. "CN=Microsoft Corporation, O=Microsoft Corporation, ..." → "Microsoft Corporation"
    /// </summary>
    private static string? GetPublisherName(string? publisherSubject)
    {
        if (string.IsNullOrWhiteSpace(publisherSubject)) return null;

        // Cas 1 : format X509 standard avec CN=
        var match = System.Text.RegularExpressions.Regex.Match(
            publisherSubject, @"CN=([^,]+)");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // Cas 2 : ancien format (nom de fichier ou nom brut), on retourne tel quel
        return publisherSubject.Trim();
    }

    /// <summary>
    /// Trace les auto-trusts dans un fichier dédié pour permettre l'audit
    /// a posteriori (ex: "quel binaire Microsoft s'est connecté à 85.x.x.x ?").
    /// </summary>
    private static void AuditAutoTrust(string processName, string fullPath, string publisher)
    {
        try
        {
            string auditFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor", "NetMonitor", "auto_trust_audit.log");

            var dir = Path.GetDirectoryName(auditFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PID-check " +
                          $"{processName} | {publisher} | {fullPath}{Environment.NewLine}";

            lock (_auditLock)
                File.AppendAllText(auditFile, line);
        }
        catch { }
    }

    // ============================================================
    //  Structs P/Invoke
    // ============================================================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint   cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint   cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint   dwUIChoice;
        public uint   fdwRevocationChecks;
        public uint   dwUnionChoice;
        public IntPtr pFile;
        public uint   dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint   dwProvFlags;
        public uint   dwUIContext;
    }
}