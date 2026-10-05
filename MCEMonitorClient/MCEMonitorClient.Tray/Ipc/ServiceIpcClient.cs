using System;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using MCEMonitorClient.Tray.Models;

namespace MCEMonitorClient.Tray.Ipc
{
    /// <summary>
    /// Client IPC pour communiquer avec le Service (requêtes ponctuelles).
    /// </summary>
    public static class ServiceIpcClient
    {
        private const string PipeName = "MCEMonitor_ClientPipe";

        /// <summary>
        /// Envoie une commande au Service et attend la réponse.
        /// </summary>
        private static async Task<string?> SendCommand(string command)
        {
            try
            {
                MCEMonitorClient.Tray.Logs.CoreLog.Write($"IPC [SEND] '{command}'");

                using var client = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                await client.ConnectAsync(1500);

                if (!client.IsConnected)
                {
                    MCEMonitorClient.Tray.Logs.CoreLog.Write("IPC [SEND] non connecté");
                    return null;
                }

                MCEMonitorClient.Tray.Logs.CoreLog.Write("IPC [SEND] connecté");

                byte[] cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
                await client.WriteAsync(cmdBytes);
                await client.FlushAsync();

                MCEMonitorClient.Tray.Logs.CoreLog.Write("IPC [SEND] commande envoyée, attente réponse...");

                byte[] buffer = new byte[65536];
                using var ms = new System.IO.MemoryStream();

                while (true)
                {
                    int read = await client.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    ms.Write(buffer, 0, read);
                }

                string response = Encoding.UTF8.GetString(ms.ToArray());
                MCEMonitorClient.Tray.Logs.CoreLog.Write($"IPC [SEND] réponse reçue ({response.Length} octets)");

                return response;
            }
            catch (Exception ex)
            {
                MCEMonitorClient.Tray.Logs.CoreLog.Write("IPC [SEND] ERROR : " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Récupère l'état de tous les serveurs depuis le Service.
        /// </summary>
        public static async Task<TrayState?> GetStateAsync()
        {
            string? json = await SendCommand("get-state");
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<TrayState>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Demande au Service de refaire un scan immédiat.
        /// </summary>
        public static async Task<bool> ForceScanAsync()
        {
            string? json = await SendCommand("force-scan");
            return json != null && json.Contains("\"status\":\"ok\"");
        }

        /// <summary>
        /// Demande au Service de s'arrêter.
        /// </summary>
        public static async Task<bool> ShutdownAsync()
        {
            string? json = await SendCommand("shutdown");
            return json != null && json.Contains("\"status\":\"ok\"");
        }
    }

    /// <summary>
    /// Réponse de la commande get-state.
    /// </summary>
    public class TrayState
    {
        public string GlobalState { get; set; } = "ok";
        public List<PollResult> Servers { get; set; } = new();
    }
}