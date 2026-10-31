using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NetMonitor.UI
{
    // ============================================================
    //  Modèles de réponse
    // ============================================================

    public class NetStatus
    {
        public bool running { get; set; }
        public DateTime lastUpdate { get; set; }
        public string worstSeverity { get; set; } = "ok";
        public int alertCount { get; set; }
        public int connectionCount { get; set; }
    }

    public class NetAlert
    {
        public DateTime timestamp { get; set; }
        public string? source { get; set; }
        public string? severity { get; set; }
        public string? message { get; set; }
        public string? remoteIp { get; set; }
        public int remotePort { get; set; }
        public int localPort { get; set; }
        public int pid { get; set; }
        public string? processName { get; set; }
        public string? processPath { get; set; }
        public string? trustReason { get; set; }
        public string? details { get; set; }
        public bool isKnownScanner { get; set; }
    }

    public class NetConnection
    {
        public int pid { get; set; }
        public string? processName { get; set; }
        public string localIp { get; set; } = "";
        public int localPort { get; set; }
        public string remoteIp { get; set; } = "";
        public int remotePort { get; set; }
        public string protocol { get; set; } = "Tcp";
        public DateTime timestamp { get; set; }
    }

    public class NetProblem
    {
        public DateTime timestamp { get; set; }
        public string severity { get; set; } = "";
        public string category { get; set; } = "";
        public string message { get; set; } = "";
    }

    public class NetConfig
    {
        public int portScanWindowSeconds { get; set; }
        public int portScanThreshold { get; set; }
        public int beaconWindowSeconds { get; set; }
        public int beaconMinHits { get; set; }
        public double beaconJitterTolerancePct { get; set; }
        public bool autoBlock { get; set; }
        public bool autoBlockCritical { get; set; }
        public bool autoBlockWarning { get; set; }
        public bool autoBlockSubnet { get; set; }
        public string[] ignoredNetworks { get; set; } = Array.Empty<string>();
        public bool disableFilters { get; set; }
        public int blacklistLevel { get; set; }             
        public DateTime blacklistLastUpdate { get; set; }  

        // Scanners de recherche (Trusted) ---
        public bool ignoreSearchScanners { get; set; }
        public string[] scannerPatterns { get; set; } = Array.Empty<string>();
    }

    public class NetWebStatus
    {
        public bool enabled { get; set; }
        public int port { get; set; }
        public string username { get; set; } = "";
        public string? password { get; set; }
    }

    public class BlockedIpDto
    {
        public string Ip { get; set; } = "";
        public DateTime BlockedAt { get; set; }
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
        public string Severity { get; set; } = "-";
        public bool IsProcess { get; set; }
        public string? ProcessPath { get; set; }
        public string? ManualSeverity { get; set; }
        public DateTime? ManualSeveritySetAt { get; set; }
        public bool IsKnownScanner { get; set; }        
    }

    public class BlockedIpRetryDto
    {
        public string Ip { get; set; } = "";
        public string OriginalDetector { get; set; } = "-";
        public DateTime BlockedAt { get; set; }
        public DateTime FirstRetryAt { get; set; }
        public DateTime LastRetryAt { get; set; }
        public int RetryCount { get; set; }
    }

    // ============================================================
    //  Statistiques (miroir de StatsBlock côté serveur)
    // ============================================================
    public class IpStatDto
    {
        public string Ip { get; set; } = "";
        public int Count { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public string MaxSeverity { get; set; } = "ok";
        public List<string> Detectors { get; set; } = new();
        public string? Country { get; set; }
        public string? CountryCode { get; set; }
        public string? City { get; set; }
        public string? Isp { get; set; }
        public string? ReverseDns { get; set; }
        public bool IsPrivate { get; set; }
    }

    public class CountryStatDto
    {
        public string Country { get; set; } = "";
        public string CountryCode { get; set; } = "";
        public int Count { get; set; }
        public int DistinctIps { get; set; }
    }

    public class DetectorStatDto
    {
        public string Detector { get; set; } = "";
        public int Count { get; set; }
    }

    public class StatsBlockDto
    {
        public int TotalAlerts { get; set; }
        public int DistinctIps { get; set; }
        public int DistinctCountries { get; set; }
        public string WorstSeverity { get; set; } = "ok";
        public List<IpStatDto> TopIps { get; set; } = new();
        public List<CountryStatDto> TopCountries { get; set; } = new();
        public List<DetectorStatDto> TopDetectors { get; set; } = new();
        public Dictionary<string, int> AlertsByCountryCode { get; set; } = new();
    }

    public class GlobalStatsDto
    {
        public int TotalAlerts { get; set; }
        public int DistinctIps { get; set; }
        public int DistinctCountries { get; set; }
        public string WorstSeverity { get; set; } = "ok";
        public DateTime GeneratedAt { get; set; }
        public Dictionary<string, int> SeverityCounts { get; set; } = new();
        public StatsBlockDto Active { get; set; } = new();
        public StatsBlockDto Blocked { get; set; } = new();
        public List<ProcessStatDto> TopProcesses { get; set; } = new();
        public List<HourlyStatDto> ActivityByHour { get; set; } = new();
        public List<SeverityStatDto> SeverityDistribution { get; set; } = new();        
    }

    public class WhitelistEntryDto
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string? Sha256 { get; set; }
        public string? Publisher { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class NetPendingProcess
    {
        public string processPath { get; set; } = "";
        public string processName { get; set; } = "";
        public string? trustReason { get; set; }
        public string? remoteIp { get; set; }
        public int remotePort { get; set; }
        public int pid { get; set; }
        public DateTime firstSeen { get; set; }
        public DateTime lastSeen { get; set; }
        public int count { get; set; }
        public string severity { get; set; } = "";
        public string? detector { get; set; }
    }
    
    public class AuditGroupedDto
    {
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int Count { get; set; }
        public string Severity { get; set; } = "";
        public string Source { get; set; } = "";
        public string? RemoteIp { get; set; }
        public int RemotePort { get; set; }
        public int LocalPort { get; set; }
        public int Pid { get; set; }
        public string? ProcessName { get; set; }
        public string? ProcessPath { get; set; }
        public string? TrustReason { get; set; }
        public string? Details { get; set; }
        public bool isKnownScanner { get; set; }
    }    

    public class PersistentProblemDto
    {
        public DateTime Timestamp { get; set; }
        public string Severity { get; set; } = "";
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class ProcessStatDto
    {
        public string ProcessName { get; set; } = "";
        public int Count { get; set; }
    }

    public class HourlyStatDto
    {
        public int Hour { get; set; }
        public int Count { get; set; }
        public int CriticalCount { get; set; }
    }

    public class SeverityStatDto
    {
        public string Severity { get; set; } = "";
        public int Count { get; set; }
    }
    
    // ============================================================
    //  Client IPC
    // ============================================================

    public static class NetMonitorIpcClient
    {
        private const string PIPE_NAME = "MCEMonitor_NetMonitorPipe";
        private const int GLOBAL_TIMEOUT_MS = 3000;

        private static readonly SemaphoreSlim _ipcLock = new(1, 1);

        private static async Task<string?> SendCommand(string command)
        {
            await _ipcLock.WaitAsync();
            try
            {
                using var cts = new CancellationTokenSource(GLOBAL_TIMEOUT_MS);
                return await SendCommandInternal(command, cts.Token);
            }
            catch { return null; }
            finally { _ipcLock.Release(); }
        }

        private static async Task<string?> SendCommandInternal(string command, CancellationToken token)
        {
            using var client = new NamedPipeClientStream(
                ".", PIPE_NAME, PipeDirection.InOut, PipeOptions.Asynchronous);

            try { await client.ConnectAsync(1500, token); }
            catch { return null; }

            if (!client.IsConnected) return null;

            client.ReadMode = PipeTransmissionMode.Byte;

            byte[] cmdBytes = Encoding.UTF8.GetBytes(command);
            await client.WriteAsync(cmdBytes, 0, cmdBytes.Length, token);
            await client.FlushAsync(token);

            byte[] buffer = new byte[16384];
            using var mem = new MemoryStream();

            while (true)
            {
                int bytesRead = await client.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead <= 0) break;
                mem.Write(buffer, 0, bytesRead);
            }

            return Encoding.UTF8.GetString(mem.ToArray());
        }

        private static T? Deserialize<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<T>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                // ⚠ Ne plus avaler silencieusement : on log pour diagnostiquer
                // immédiatement un mismatch de contrat IPC (enum vs string, etc.)
                CoreLog.Error(
                    $"Deserialize<{typeof(T).Name}> FAIL : {ex.GetType().Name} — {ex.Message}");

                string preview = json.Length > 500 ? json.Substring(0, 500) + "…" : json;
                CoreLog.Error($"JSON reçu (aperçu) : {preview}");

                return null;
            }
        }

        private static bool IsOk(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            return json.Contains("\"status\":\"ok\"") ||
                   json.Contains("\"status\": \"ok\"");
        }

        // ---------- Status / Listes ----------

        public static async Task<NetStatus?> GetStatus()
            => Deserialize<NetStatus>(await SendCommand("get-status"));

        public static async Task<List<NetAlert>?> GetActiveIntrusions()
            => Deserialize<List<NetAlert>>(await SendCommand("get-active"));

        public static async Task<List<NetAlert>?> GetAlerts()
            => Deserialize<List<NetAlert>>(await SendCommand("get-alerts"));

        public static async Task<List<BlockedIpDto>?> GetBlockedIps()
            => Deserialize<List<BlockedIpDto>>(await SendCommand("get-blocked"));

        public static async Task<List<NetConnection>?> GetConnections()
            => Deserialize<List<NetConnection>>(await SendCommand("get-connections"));

        public static async Task<List<NetProblem>?> GetProblems()
            => Deserialize<List<NetProblem>>(await SendCommand("get-problems"));

        public static async Task<NetConfig?> GetConfig()
            => Deserialize<NetConfig>(await SendCommand("get-config"));

        public static async Task<NetWebStatus?> GetWebStatus()
            => Deserialize<NetWebStatus>(await SendCommand("get-web-status"));

        public static async Task<List<BlockedIpRetryDto>?> GetBlockedIpRetriesAsync()
            => Deserialize<List<BlockedIpRetryDto>>(await SendCommand("get-blocked-retries"));
            
        public static async Task<List<WhitelistEntryDto>?> GetWhitelistAsync()
            => Deserialize<List<WhitelistEntryDto>>(await SendCommand("get-whitelist"));
            
        public static async Task<List<AuditGroupedDto>?> GetAuditHistoryGroupedAsync()
            => Deserialize<List<AuditGroupedDto>>(await SendCommand("get-audit-grouped"));                        

        public static async Task<List<PersistentProblemDto>?> GetPersistentProblemsAsync()
            => Deserialize<List<PersistentProblemDto>>(await SendCommand("get-persistent-problems"));
            
        // ---------- Actions ----------

        public static async Task<bool> ClearActiveIntrusionsAsync()
            => IsOk(await SendCommand("clear-active"));

        public static async Task<bool> ClearAlertsAsync()
            => IsOk(await SendCommand("clear-alerts"));

        public static async Task<bool> ReloadConfigAsync()
            => IsOk(await SendCommand("reload-config"));

        public static async Task<bool> ForceScanAsync()
            => IsOk(await SendCommand("force-scan"));

        public static async Task<bool> UnblockIpAsync(string ip)
            => IsOk(await SendCommand($"unblock-ip {ip}"));

        public static async Task<bool> ClearBlockedIpRetriesAsync()
            => IsOk(await SendCommand("clear-blocked-retries"));
            
        public static async Task<bool> RemoveWhitelistEntryAsync(string path)
            => IsOk(await SendCommand($"unwhitelist-process {path}"));

        public static async Task<bool> ToggleWhitelistEntryAsync(string path, bool enabled)
            => IsOk(await SendCommand($"toggle-whitelist-entry {path}|{(enabled ? "true" : "false")}"));            

        public static async Task<bool> ClearPersistentProblemsAsync()
            => IsOk(await SendCommand("clear-persistent-problems"));
            
        public static async Task<bool> RestartComputerAsync()
            => IsOk(await SendCommand("restart-computer"));
            
        public static async Task<bool> SetManualSeverityAsync(string ip, string? severityOrAuto)
        {
            string sev = string.IsNullOrWhiteSpace(severityOrAuto) ? "auto" : severityOrAuto;
            return IsOk(await SendCommand($"set-manual-severity {ip}|{sev}"));
        }                        
        
        public static async Task<bool> SetWebConfig(bool enabled, int port, string username, string password)
        {
            string cmd = $"set-web-config enabled={enabled.ToString().ToLower()}" +
                         $"&port={port}" +
                         $"&username={username}" +
                         $"&password={password}";

            return IsOk(await SendCommand(cmd));
        }

        // ---------- Commandes avec résultat détaillé ----------

        public static async Task<bool> BlockIpAsync(string ip, string? source = null,
            string? message = null, string? details = null, string? severity = null)
        {
            var result = await BlockIpWithResultAsync(ip, source, message, details, severity);
            return result.Success;
        }

        public static async Task<(bool Success, string? Error)> BlockIpWithResultAsync(
            string ip, string? source = null, string? message = null,
            string? details = null, string? severity = null)
        {
            string cmd = $"block-ip {ip}|{source ?? ""}|{message ?? ""}|{details ?? ""}|{severity ?? ""}";
            string? json = await SendCommand(cmd);

            if (string.IsNullOrWhiteSpace(json))
                return (false, "Service injoignable.");

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool ok = root.TryGetProperty("blocked", out var b) && b.GetBoolean();
                string? err = root.TryGetProperty("message", out var m) ? m.GetString() : null;

                return (ok, err);
            }
            catch
            {
                return (false, "Erreur de lecture de la réponse IPC.");
            }
        }
        
        public static async Task<List<NetAlert>?> GetAuditHistory()
            => Deserialize<List<NetAlert>>(await SendCommand("get-audit"));

        public static async Task<bool> ClearAuditAsync()
            => IsOk(await SendCommand("clear-audit"));
            
        public static async Task<string?> ExportPdfAsync()
            => await SendCommand("export-pdf");
            
        // ---------- Statistiques (timeout long : GeoIP peut être lent au 1er appel) ----------
        public static async Task<GlobalStatsDto?> GetStatsAsync()
        {
            await _ipcLock.WaitAsync();
            try
            {
                using var cts = new CancellationTokenSource(15000);
                string? json = await SendCommandInternal("get-stats", cts.Token);
                return Deserialize<GlobalStatsDto>(json);
            }
            catch { return null; }
            finally { _ipcLock.Release(); }
        }
        
        public static async Task<List<NetPendingProcess>?> GetPendingProcesses()
            => Deserialize<List<NetPendingProcess>>(await SendCommand("get-pending-processes"));

        public static async Task<bool> WhitelistProcessAsync(string path)
        {
            var resp = await SendCommand("whitelist-process " + path);
            return resp != null && resp.Contains("\"status\":\"ok\"");
        }

        public static async Task<(bool ok, string error)> BlockProcessWithResultAsync(string path)
        {
            string? json = await SendCommand("block-process " + path);
            if (string.IsNullOrWhiteSpace(json))
                return (false, "Service injoignable.");

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool ok = root.TryGetProperty("blocked", out var b) && b.GetBoolean();
                string? err = root.TryGetProperty("message", out var m) ? m.GetString() : null;

                return (ok, err ?? "");
            }
            catch
            {
                return (false, "Erreur de lecture de la réponse IPC.");
            }
        }

        public static async Task<bool> UnblockProcessAsync(string path)
        {
            var resp = await SendCommand("unblock-process " + path);
            return resp != null && resp.Contains("\"status\":\"ok\"");
        }
        
        public static async Task<bool> DismissPendingProcessAsync(string processPath)
        {
            try
            {
                // Utilise la méthode SendCommand existante
                var result = await SendCommand($"dismiss-pending {processPath}");
                
                // Vérifie la réponse JSON
                return result != null && result.Contains("\"dismissed\":true");
            }
            catch
            {
                return false;
            }
        }                                               
    }
}