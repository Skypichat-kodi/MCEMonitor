using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCEMonitor
{
    // ============================================================
    //  Modèles de réponse
    // ============================================================

    public class RomMonitorStatus
    {
        public DateTime lastCheck { get; set; }
        public int diskCount { get; set; }
        public int smartCount { get; set; }
        public int alertCount { get; set; }
        public int interval { get; set; }
        public bool alertOnSmartFailure { get; set; }
    }

    public class RomMonitorDisk
    {
        public string name { get; set; } = "";
        public string label { get; set; } = "";
        public string driveType { get; set; } = "";
        public double totalGo { get; set; }
        public double freeGo { get; set; }
        public double freePercent { get; set; }
    }

    public class RomMonitorSmart
    {
        public string device { get; set; } = "";
        public string model { get; set; } = "";
        public string serial { get; set; } = "";
        public string type { get; set; } = "";
        public string status { get; set; } = "";
        public string statusReason { get; set; } = "";
        public int? temperature { get; set; }
        public int? powerOnHours { get; set; }
        public bool passed { get; set; }
    }

    public class RomMonitorAlert
    {
        public DateTime timestamp { get; set; }
        public string type { get; set; } = "";
        public string severity { get; set; } = "";
        public string target { get; set; } = "";
        public string message { get; set; } = "";
        public bool emailSent { get; set; }
    }

    public class RomMonitorConfig
    {
        public int interval { get; set; }
        public int diskSpaceWarnPercent { get; set; }
        public int diskSpaceCriticalPercent { get; set; }
        public int diskSpaceWarnGo { get; set; }
        public int diskSpaceCriticalGo { get; set; }
        public bool alertOnSmartFailure { get; set; }
        public bool alertOnLowDiskSpace { get; set; }
        public int alertCooldownHours { get; set; }
    }

    // ============================================================
    //  Client IPC
    // ============================================================

    public static class RomMonitorIpcClient
    {
        private const string PIPE_NAME = "MCEMonitor_RomMonitorPipe";
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
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                _ipcLock.Release();
            }
        }

        private static async Task<string?> SendCommandInternal(string command, CancellationToken token)
        {
            using var client = new NamedPipeClientStream(
                ".",
                PIPE_NAME,
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );

            try
            {
                await client.ConnectAsync(1500, token);
            }
            catch
            {
                return null;
            }

            if (!client.IsConnected)
                return null;

            client.ReadMode = PipeTransmissionMode.Byte;

            byte[] cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
            await client.WriteAsync(cmdBytes, 0, cmdBytes.Length, token);
            await client.FlushAsync(token);

            byte[] buffer = new byte[8192];
            using var mem = new MemoryStream();

            while (true)
            {
                int bytesRead = await client.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead <= 0)
                    break;

                mem.Write(buffer, 0, bytesRead);
            }

            return Encoding.UTF8.GetString(mem.ToArray());
        }

        // ============================================================
        //  Commandes
        // ============================================================

        public static async Task<RomMonitorStatus?> GetStatus()
        {
            string? json = await SendCommand("get-status");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<RomMonitorStatus>(json); }
            catch { return null; }
        }

        public static async Task<List<RomMonitorDisk>?> GetDisks()
        {
            string? json = await SendCommand("get-disks");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<List<RomMonitorDisk>>(json); }
            catch { return null; }
        }

        public static async Task<List<RomMonitorSmart>?> GetSmart()
        {
            string? json = await SendCommand("get-smart");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<List<RomMonitorSmart>>(json); }
            catch { return null; }
        }

        public static async Task<List<RomMonitorAlert>?> GetAlerts()
        {
            string? json = await SendCommand("get-alerts");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<List<RomMonitorAlert>>(json); }
            catch { return null; }
        }

        public static async Task<RomMonitorConfig?> GetConfig()
        {
            string? json = await SendCommand("get-config");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<RomMonitorConfig>(json); }
            catch { return null; }
        }

        public static async Task<bool> SendTestEmail()
        {
            string? json = await SendCommand("send-test-email");
            return json != null && json.Contains("\"status\":\"ok\"");
        }

        public static async Task<bool> ShutdownService()
        {
            string? json = await SendCommand("shutdown");
            return json != null;
        }

        public static async Task<bool> IsServiceRunning()
        {
            var status = await GetStatus();
            return status != null;
        }
        
        public static async Task<bool> SetConfig(RomMonitorConfig cfg)
        {
            string cmd = $"set-config interval={cfg.interval}" +
                         $"&warnPct={cfg.diskSpaceWarnPercent}" +
                         $"&critPct={cfg.diskSpaceCriticalPercent}" +
                         $"&warnGo={cfg.diskSpaceWarnGo}" +
                         $"&critGo={cfg.diskSpaceCriticalGo}" +
                         $"&smart={cfg.alertOnSmartFailure.ToString().ToLower()}" +
                         $"&lowDisk={cfg.alertOnLowDiskSpace.ToString().ToLower()}" +
                         $"&cooldown={cfg.alertCooldownHours}";

            string? json = await SendCommand(cmd);
            return json != null && json.Contains("\"status\":\"ok\"");
        }        
    }
}