using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCEMonitor
{
    public sealed class NetMonitorStatus
    {
        public bool running { get; set; }
        public int alertCount { get; set; }
    }

    public sealed class NetMonitorAlert
    {
        public DateTime Timestamp { get; set; }
        public string Severity { get; set; } = "Info";
        public string Source { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
    }

    public sealed class NetMonitorConnection
    {
        public int Pid { get; set; }
        public string? ProcessName { get; set; }
        public string LocalIp { get; set; } = "";
        public int LocalPort { get; set; }
        public string RemoteIp { get; set; } = "";
        public int RemotePort { get; set; }
        public string Protocol { get; set; } = "Tcp";
        public DateTime Timestamp { get; set; }
    }

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

            byte[] cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
            await client.WriteAsync(cmdBytes, 0, cmdBytes.Length, token);
            await client.FlushAsync(token);

            byte[] buffer = new byte[16384];
            using var mem = new System.IO.MemoryStream();

            while (true)
            {
                int bytesRead = await client.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead <= 0) break;
                mem.Write(buffer, 0, bytesRead);
            }

            return Encoding.UTF8.GetString(mem.ToArray());
        }

        private static T? ParsePayload<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Success", out var s) || !s.GetBoolean())
                    return null;
                if (!doc.RootElement.TryGetProperty("PayloadJson", out var p) ||
                    p.ValueKind != JsonValueKind.String)
                    return null;

                var inner = p.GetString();
                if (string.IsNullOrWhiteSpace(inner)) return null;

                return JsonSerializer.Deserialize<T>(inner,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return null; }
        }

        private static bool IsOk(string? json)
            => !string.IsNullOrWhiteSpace(json) && json.Contains("\"Success\":true");

        // ---------- Commandes ----------

        public static async Task<NetMonitorStatus?> GetStatus()
            => ParsePayload<NetMonitorStatus>(await SendCommand("net.status"));

        public static async Task<List<NetMonitorAlert>?> GetHistory()
            => ParsePayload<List<NetMonitorAlert>>(await SendCommand("net.history"));

        public static async Task<List<NetMonitorConnection>?> GetRecentConnections()
            => ParsePayload<List<NetMonitorConnection>>(await SendCommand("net.recent"));

        public static async Task<bool> Start()
            => IsOk(await SendCommand("net.start"));

        public static async Task<bool> Stop()
            => IsOk(await SendCommand("net.stop"));

        public static async Task<bool> BlockIp(string ip)
            => IsOk(await SendCommand($"net.blockip {ip}"));

        public static async Task<bool> UnblockIp(string ip)
            => IsOk(await SendCommand($"net.unblockip {ip}"));
    }
}