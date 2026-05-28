using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
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

        private static async Task<string?> SendCommand(string command)
        {
            try
            {
                MainWindow.StaticUiLog("IPC ? tentative connexion au pipe");

                using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut, PipeOptions.None);

                await client.ConnectAsync(1500);

                client.ReadMode = PipeTransmissionMode.Byte;

                MainWindow.StaticUiLog("IPC ? connecté au pipe, envoi commande : " + command);

                // ?? On envoie la commande + \n pour ReadLine()
                byte[] cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
                await client.WriteAsync(cmdBytes, 0, cmdBytes.Length);
                await client.FlushAsync();

                // ?? Lecture illimitée
                byte[] buffer = new byte[4096];
                int bytesRead;

                using var mem = new MemoryStream();

                while ((bytesRead = await client.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    mem.Write(buffer, 0, bytesRead);
                }

                string json = Encoding.UTF8.GetString(mem.ToArray());

                MainWindow.StaticUiLog("IPC ? réponse brute : " + json);

                return json;
            }
            catch (Exception ex)
            {
                MainWindow.StaticUiLog("IPC ? ERREUR : " + ex.Message);
                return null;
            }
        }

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
    }
}

