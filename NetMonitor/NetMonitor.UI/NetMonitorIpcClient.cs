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
    //  Modèles de réponse (noms alignés sur le camelCase du Service)
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
        public string source { get; set; } = "";
        public string severity { get; set; } = "";
        public string message { get; set; } = "";
        public string? remoteIp { get; set; }
        public int remotePort { get; set; }
        public int localPort { get; set; }
        public int pid { get; set; }
        public string? processName { get; set; }
        public string? details { get; set; }
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
    }
    
    public class NetWebStatus
    {
        public bool enabled { get; set; }
        public int port { get; set; }
        public string username { get; set; } = "";
        public string? password { get; set; }
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
            catch { return null; }
        }

        private static bool IsOk(string? json)
            => !string.IsNullOrWhiteSpace(json) && json.Contains("\"status\":\"ok\"");

        // ---------- Commandes ----------

        public static async Task<NetStatus?> GetStatus()
            => Deserialize<NetStatus>(await SendCommand("get-status"));

        public static async Task<List<NetAlert>?> GetAlerts()
            => Deserialize<List<NetAlert>>(await SendCommand("get-alerts"));

        public static async Task<List<NetConnection>?> GetConnections()
            => Deserialize<List<NetConnection>>(await SendCommand("get-connections"));

        public static async Task<List<NetProblem>?> GetProblems()
            => Deserialize<List<NetProblem>>(await SendCommand("get-problems"));

        public static async Task<NetConfig?> GetConfig()
            => Deserialize<NetConfig>(await SendCommand("get-config"));

        public static async Task<bool> ClearAlertsAsync()
            => IsOk(await SendCommand("clear-alerts"));

        public static async Task<bool> ReloadConfigAsync()
            => IsOk(await SendCommand("reload-config"));

        public static async Task<bool> ForceScanAsync()
            => IsOk(await SendCommand("force-scan"));
            
        public static async Task<NetWebStatus?> GetWebStatus()
            => Deserialize<NetWebStatus>(await SendCommand("get-web-status"));

        public static async Task<bool> SetWebConfig(bool enabled, int port, string username, string password)
        {
            string cmd = $"set-web-config enabled={enabled.ToString().ToLower()}" +
                         $"&port={port}" +
                         $"&username={username}" +
                         $"&password={password}";

            return IsOk(await SendCommand(cmd));
        }            
    }
}