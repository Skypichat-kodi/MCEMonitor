using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using MediaMonitor.Core.Services;

namespace MediaMonitor.Service
{
    public class ServiceIpcServer
    {
        private readonly MediaMonitorEngine _engine;
        private bool _running = true;

        public ServiceIpcServer(MediaMonitorEngine engine)
        {
            _engine = engine;
        }

        public void Start()
        {
            Log("IPC Server démarrage du thread serveur.");

            new Thread(ServerLoop)
            {
                IsBackground = true
            }.Start();
        }

        private void ServerLoop()
        {
            while (_running)
            {
                try
                {
                    Log("IPC : attente d'une connexion client...");

                    using var server = new NamedPipeServerStream(
                        "MediaMonitorPipe",
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None
                    );

                    server.WaitForConnection();
                    Log("IPC : client connecté.");

                    var reader = new StreamReader(server);
                    var writer = new StreamWriter(server) { AutoFlush = true };

                    // ?? Lecture simple : une ligne = une commande
                    string command = reader.ReadLine()?.Trim() ?? "";
                    Log("IPC : commande reçue = " + command);

                    switch (command)
                    {
                        case "shutdown":
                            HandleShutdown();
                            break;

                        case "get-state":
                            HandleGetState(writer, server);
                            break;

                        case "get-history":
                            HandleGetHistory(writer, server);
                            break;

                        case "get-report":
                            HandleGetReport(writer, server);
                            break;

                        case "send-report":
                            HandleSendReport(writer, server);
                            break;

                        default:
                            writer.Write("{\"error\":\"unknown command\"}");
                            writer.Flush();
                            server.WaitForPipeDrain();
                            server.Disconnect();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log("ERREUR IPC : " + ex);
                }
            }

            Log("IPC ServerLoop terminé (running = false).");
        }

        private void HandleShutdown()
        {
            Log("IPC : commande SHUTDOWN reçue.");

            try
            {
                _engine.Stop();
                Log("IPC : moteur arrêté proprement.");
            }
            catch (Exception ex)
            {
                Log("ERREUR IPC : moteur.Stop() ? " + ex);
            }

            Environment.Exit(0);
        }

        private void HandleGetState(StreamWriter writer, NamedPipeServerStream server)
        {
            var state = new
            {
                openFiles = _engine.GetCurrentOpenFiles(),
                lastImage = _engine.GetLastImage()
            };

            string json = JsonSerializer.Serialize(state);
            Log("JSON envoyé (get-state) : " + json);

            writer.Write(json);
            writer.Flush();

            server.WaitForPipeDrain();
            server.Disconnect();
        }

        private void HandleGetHistory(StreamWriter writer, NamedPipeServerStream server)
        {
            var history = _engine.GetHistory();
            string json = JsonSerializer.Serialize(history);

            Log("JSON envoyé (get-history) : " + json);

            writer.Write(json);
            writer.Flush();

            server.WaitForPipeDrain();
            server.Disconnect();
        }

        private void HandleGetReport(StreamWriter writer, NamedPipeServerStream server)
        {
            string html = _engine.GenerateReportFromHistory();
            string json = JsonSerializer.Serialize(new { report = html });

            Log("JSON envoyé (get-report) : " + json);

            writer.Write(json);
            writer.Flush();

            server.WaitForPipeDrain();
            server.Disconnect();
        }

        private void HandleSendReport(StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                _engine.SendReportEmail().Wait();
                writer.Write("{\"status\":\"ok\"}");
                Log("IPC : send-report exécuté.");
            }
            catch (Exception ex)
            {
                writer.Write("{\"status\":\"error\",\"message\":\"" + ex.Message + "\"}");
                Log("ERREUR send-report : " + ex);
            }

            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        private void Log(string message)
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                Directory.CreateDirectory(folder);

                string file = Path.Combine(folder, "MediaMonitor.Service.log");

                File.AppendAllText(
                    file,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}"
                );
            }
            catch { }
        }
    }
}

