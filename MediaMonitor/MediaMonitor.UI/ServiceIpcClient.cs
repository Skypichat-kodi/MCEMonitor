using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaMonitor.Core.Models;
using MediaMonitor.UI;

namespace MediaMonitor.UI.Services
{
    public class StateResponse
    {
        public List<MediaUsageItem> openFiles { get; set; } = new();
        public string lastImage { get; set; } = "";
    }

    public static class ServiceIpcClient
    {
        private const string PIPE_NAME = "MediaMonitorPipe";

        // Empêche plusieurs appels IPC simultanés
        private static readonly SemaphoreSlim _ipcLock = new(1, 1);

        // Timeout global pour chaque commande IPC
        private const int GLOBAL_TIMEOUT_MS = 2000;

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
                MainWindow.StaticUiLog($"IPC ? Timeout global sur commande : {command}");
                return null;
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("IPC ? ERREUR : " + ex.Message);
                return null;
            }
            finally
            {
                _ipcLock.Release();
            }
        }

        private static async Task<string?> SendCommandInternal(string command, CancellationToken token)
        {
            MainWindow.StaticUiLog("IPC ? tentative connexion au pipe");

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
            catch (OperationCanceledException)
            {
                MainWindow.StaticUiLog("IPC ? Timeout connexion pipe");
                return null;
            }

            if (!client.IsConnected)
            {
                MainWindow.StaticUiLog("IPC ? pipe non connecté");
                return null;
            }

            client.ReadMode = PipeTransmissionMode.Byte;

            MainWindow.StaticUiLog("IPC ? connecté, envoi commande : " + command);

            byte[] cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
            await client.WriteAsync(cmdBytes, 0, cmdBytes.Length, token);
            await client.FlushAsync(token);

            byte[] buffer = new byte[4096];
            using var mem = new MemoryStream();

            while (true)
            {
                int bytesRead = await client.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead <= 0)
                    break;

                mem.Write(buffer, 0, bytesRead);
            }

            string json = Encoding.UTF8.GetString(mem.ToArray());

            MainWindow.StaticUiLog("IPC ? réponse brute : " + json);

            return json;
        }

        // ============================================================
        // GETTERS
        // ============================================================

        public static async Task<StateResponse?> GetState()
        {
            string? json = await SendCommand("get-state");
            if (json == null)
                return null;

            try
            {
                return JsonSerializer.Deserialize<StateResponse>(json);
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON get-state : " + ex.Message);
                return null;
            }
        }

        public static async Task<List<MediaUsageItem>?> GetHistory()
        {
            string? json = await SendCommand("get-history");
            if (json == null)
                return null;

            try
            {
                return JsonSerializer.Deserialize<List<MediaUsageItem>>(json);
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON get-history : " + ex.Message);
                return null;
            }
        }

        public static async Task<string?> GetReport()
        {
            string? json = await SendCommand("get-report");
            if (json == null)
                return null;

            try
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return obj != null && obj.ContainsKey("report") ? obj["report"] : null;
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON get-report : " + ex.Message);
                return null;
            }
        }

        public static async Task<bool> SendReport()
        {
            string? json = await SendCommand("send-report");
            if (json == null)
                return false;

            try
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return obj != null && obj.ContainsKey("status") && obj["status"] == "ok";
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON send-report : " + ex.Message);
                return false;
            }
        }

        public static async Task<bool> ShutdownService()
        {
            string? json = await SendCommand("shutdown");
            return json != null;
        }

        // ============================================================
        // LOG SERVICE
        // ============================================================
        public static async Task<bool> SetLogging(bool enabled)
        {
            string cmd = enabled ? "set-logging true" : "set-logging false";

            string? json = await SendCommand(cmd);
            if (json == null)
                return false;

            try
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return obj != null && obj.ContainsKey("status") && obj["status"] == "ok";
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON set-logging : " + ex.Message);
                return false;
            }
        }

        // ============================================================
        // EMAIL
        // ============================================================
        public static async Task<bool> SetEmailSending(bool enabled)
        {
            string cmd = enabled ? "set-email-enabled true" : "set-email-enabled false";

            string? json = await SendCommand(cmd);
            if (json == null)
                return false;

            try
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return obj != null && obj.ContainsKey("status") && obj["status"] == "ok";
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON set-email-enabled : " + ex.Message);
                return false;
            }
        }

        public static async Task<bool?> GetEmailEnabled()
        {
            string? json = await SendCommand("get-email-enabled");
            if (json == null)
                return null;

            try
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                return obj != null && obj.ContainsKey("enabled") ? obj["enabled"] : null;
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON get-email-enabled : " + ex.Message);
                return null;
            }
        }

        // ============================================================
        // WEB SERVER
        // ============================================================

        public static async Task<bool> GetWebEnabled()
        {
            string? json = await SendCommand("get-web-enabled");
            if (json == null)
                return false;

            try
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                return obj != null && obj.ContainsKey("enabled") && obj["enabled"];
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON get-web-enabled : " + ex.Message);
                return false;
            }
        }

        public static async Task<int> GetWebPort()
        {
            string? json = await SendCommand("get-web-port");
            if (json == null)
                return 8081;

            try
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                return obj != null && obj.ContainsKey("port") ? obj["port"] : 8081;
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON get-web-port : " + ex.Message);
                return 8081;
            }
        }

        public static Task SetWebEnabled(bool enabled)
        {
            return SendCommand("set-web-enabled " + (enabled ? "true" : "false"));
        }

        public static Task SetWebPort(int port)
        {
            return SendCommand("set-web-port " + port);
        }

        // ============================================================
        // ?? AJOUT : WEB CREDENTIALS
        // ============================================================

        public static async Task<bool> SetWebCredentials(string username, string password)
        {
            string cmd = $"set-web-credentials {username} {password}";

            string? json = await SendCommand(cmd);
            if (json == null)
                return false;

            try
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return obj != null && obj.ContainsKey("status") && obj["status"] == "ok";
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("ERREUR JSON set-web-credentials : " + ex.Message);
                return false;
            }
        }
    }
}

