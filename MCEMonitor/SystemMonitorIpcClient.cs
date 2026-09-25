using System;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCEMonitor
{
    public class SystemMonitorConfig
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

    public static class SystemMonitorIpcClient
    {
        private const string PIPE_NAME = "MCEMonitor_SystemMonitorPipe";
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
                ".", PIPE_NAME, PipeDirection.InOut, PipeOptions.Asynchronous);

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
            using var mem = new System.IO.MemoryStream();

            while (true)
            {
                int bytesRead = await client.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead <= 0)
                    break;

                mem.Write(buffer, 0, bytesRead);
            }

            return Encoding.UTF8.GetString(mem.ToArray());
        }

        public static async Task<SystemMonitorConfig?> GetConfig()
        {
            string? json = await SendCommand("get-config");
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<SystemMonitorConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return null; }
        }

        public static async Task<bool> SetConfig(SystemMonitorConfig cfg)
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
    }
}