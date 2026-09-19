using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RomMonitor.UI
{
    // ============================================================
    //  Modèles de réponse
    // ============================================================

    public class RomStatus
    {
        public DateTime lastCheck { get; set; }
        public int diskCount { get; set; }
        public int smartCount { get; set; }
        public int alertCount { get; set; }
        public int interval { get; set; }
        public bool alertOnSmartFailure { get; set; }
    }

    public class RomDisk
    {
        public string name { get; set; } = "";
        public string label { get; set; } = "";
        public string driveType { get; set; } = "";
        public double totalGo { get; set; }
        public double freeGo { get; set; }
        public double freePercent { get; set; }
        public string physicalSerial { get; set; } = "";
        public int? physicalDiskNumber { get; set; }
        public double physicalSizeGo { get; set; }    
    }

    public class RomSmart
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
        public double capacityGo { get; set; }    // ??
    }

    public class RomAlert
    {
        public DateTime timestamp { get; set; }
        public string type { get; set; } = "";
        public string severity { get; set; } = "";
        public string target { get; set; } = "";
        public string message { get; set; } = "";
        public bool emailSent { get; set; }
    }

    // ============================================================
    //  Client IPC
    // ============================================================

    public static class RomMonitorIpcClient
    {
        private const string PIPE_NAME = "MCEMonitor_RomMonitorPipe";
        private const int GLOBAL_TIMEOUT_MS = 3000;

        // ------------------------------------------------------------
        //  Log de debug
        // ------------------------------------------------------------
        private static void LogDebug(string message)
        {
            try
            {
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs",
                    "ui-debug.log"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }
        
        private static readonly SemaphoreSlim _ipcLock = new(1, 1);

        private static async Task<string?> SendCommand(string command)
        {
            await _ipcLock.WaitAsync();
            try
            {
                using var cts = new CancellationTokenSource(GLOBAL_TIMEOUT_MS);
                return await SendCommandInternal(command, cts.Token);
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

        public static async Task<RomStatus?> GetStatus()
        {
            string? json = await SendCommand("get-status");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<RomStatus>(json); }
            catch { return null; }
        }

        public static async Task<List<RomDisk>?> GetDisks()
        {
            string? json = await SendCommand("get-disks");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<List<RomDisk>>(json); }
            catch { return null; }
        }

        public static async Task<List<RomSmart>?> GetSmart()
        {
            string? json = await SendCommand("get-smart");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<List<RomSmart>>(json); }
            catch { return null; }
        }

        public static async Task<List<RomAlert>?> GetAlerts()
        {
            string? json = await SendCommand("get-alerts");

            try
            {
                LogDebug($"GetAlerts : json.len={json?.Length ?? 0}");

                if (string.IsNullOrWhiteSpace(json))
                    return null;

                var result = JsonSerializer.Deserialize<List<RomAlert>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                LogDebug($"GetAlerts : {result?.Count ?? 0} alertes désérialisées");

                return result;
            }
            catch (Exception ex)
            {
                LogDebug($"GetAlerts ERREUR : {ex.Message}");
                LogDebug($"GetAlerts JSON brut : {json}");
                return null;
            }
        }

        public static async Task<bool> IsServiceRunning()
        {
            var status = await GetStatus();
            return status != null;
        }
    }
}