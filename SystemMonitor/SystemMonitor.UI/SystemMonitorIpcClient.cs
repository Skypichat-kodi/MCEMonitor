using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SystemMonitor.UI
{
    // ============================================================
    //  Modèles de réponse
    // ============================================================

    public class SysStatus
    {
        public bool running { get; set; }
        public DateTime lastUpdate { get; set; }
        public int interval { get; set; }
        public double cpuUsage { get; set; }
        public double? cpuTemp { get; set; }
        public double ramUsage { get; set; }
        public int gpuCount { get; set; }
    }

    public class SysCpu
    {
        public string name { get; set; } = "";
        public double usagePercent { get; set; }
        public double? temperature { get; set; }
        public double? frequencyMHz { get; set; }
        public double? maxFrequencyMHz { get; set; }
        public List<SysCpuCore> cores { get; set; } = new();
    }

    public class SysCpuCore
    {
        public string name { get; set; } = "";
        public double usagePercent { get; set; }
    }

    public class SysGpu
    {
        public string name { get; set; } = "";
        public double usagePercent { get; set; }
        public double? temperature { get; set; }
        public double? vramUsedMB { get; set; }
        public double? vramTotalMB { get; set; }
    }

    public class SysRam
    {
        public double totalGB { get; set; }
        public double usedGB { get; set; }
        public double freeGB { get; set; }
        public double usagePercent { get; set; }
    }

    public class SysNetwork
    {
        public string name { get; set; } = "";
        public double downloadKBps { get; set; }
        public double uploadKBps { get; set; }
    }

    public class SysConfig
    {
        public int interval { get; set; }
        public bool alertOnHighCpu { get; set; }
        public int cpuThresholdPercent { get; set; }
        public int cpuCooldownMinutes { get; set; }
        public bool alertOnHighRam { get; set; }
        public int ramThresholdPercent { get; set; }
        public bool alertOnHighTemp { get; set; }
        public int tempThresholdCelsius { get; set; }
    }

    public class SysWebStatus
    {
        public bool enabled { get; set; }
        public int port { get; set; }
        public string username { get; set; } = "";
    }

    public class SysSnapshot
    {
        public DateTime timestamp { get; set; }
        public SysCpu cpu { get; set; } = new();
        public List<SysGpu> gpus { get; set; } = new();
        public SysRam ram { get; set; } = new();
        public List<SysNetwork> networks { get; set; } = new();
    }

    // ============================================================
    //  Client IPC
    // ============================================================

    public static class SystemMonitorIpcClient
    {
        private const string PIPE_NAME = "MCEMonitor_SystemMonitorPipe";
        private const int GLOBAL_TIMEOUT_MS = 3000;

        // Passer à true pour activer les logs de debug
        private static readonly bool EnableDebugLog = false;

        private static void LogDebug(string message)
        {
            if (!EnableDebugLog) return;

            try
            {
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs",
                    "system-ui-debug.log"
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

        public static async Task<SysStatus?> GetStatus()
        {
            string? json = await SendCommand("get-status");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<SysStatus>(json); }
            catch (Exception ex)
            {
                LogDebug($"GetStatus ERREUR : {ex.Message}");
                return null;
            }
        }

        public static async Task<SysSnapshot?> GetSnapshot()
        {
            string? json = await SendCommand("get-snapshot");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<SysSnapshot>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                LogDebug($"GetSnapshot ERREUR : {ex.Message}");
                return null;
            }
        }

        public static async Task<SysConfig?> GetConfig()
        {
            string? json = await SendCommand("get-config");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try { return JsonSerializer.Deserialize<SysConfig>(json); }
            catch { return null; }
        }

        public static async Task<SysWebStatus?> GetWebStatus()
        {
            string? json = await SendCommand("get-web-status");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<SysWebStatus>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return null; }
        }

        public static async Task<bool> SetWebConfig(bool enabled, int port, string username, string password)
        {
            string cmd = $"set-web-config enabled={enabled.ToString().ToLower()}" +
                         $"&port={port}" +
                         $"&username={username}" +
                         $"&password={password}";

            string? json = await SendCommand(cmd);
            return json != null && json.Contains("\"status\":\"ok\"");
        }

        public static async Task<bool> SetConfig(SysConfig cfg)
        {
            string cmd = $"set-config interval={cfg.interval}" +
                         $"&alertCpu={cfg.alertOnHighCpu.ToString().ToLower()}" +
                         $"&cpuThreshold={cfg.cpuThresholdPercent}" +
                         $"&cpuCooldown={cfg.cpuCooldownMinutes}" +
                         $"&alertRam={cfg.alertOnHighRam.ToString().ToLower()}" +
                         $"&ramThreshold={cfg.ramThresholdPercent}" +
                         $"&alertTemp={cfg.alertOnHighTemp.ToString().ToLower()}" +
                         $"&tempThreshold={cfg.tempThresholdCelsius}";

            string? json = await SendCommand(cmd);
            return json != null && json.Contains("\"status\":\"ok\"");
        }

        public static async Task<bool> ForceScanAsync()
        {
            string? json = await SendCommand("force-scan");
            return json != null && json.Contains("\"status\":\"ok\"");
        }

        public static async Task<bool> IsServiceRunning()
        {
            var status = await GetStatus();
            return status != null;
        }
    }
}