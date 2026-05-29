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

        // Log désactivé par défaut
        public static bool ServiceLoggingEnabled = false;

        // Envoi email activé par défaut (sera écrasé par Program.LoadEmailSetting)
        public static bool EmailSendingEnabled = true;

        public ServiceIpcServer(MediaMonitorEngine engine)
        {
            _engine = engine;

            // Le moteur demande ici si le log est activé
            CoreLog.IsLoggingEnabled = () => ServiceLoggingEnabled;
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

                    string command = reader.ReadLine()?.Trim() ?? "";
                    Log("IPC : commande reçue = " + command);

                    // Commande set-logging
                    if (command.StartsWith("set-logging ", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSetLogging(command, writer, server);
                        continue;
                    }

                    // Commande set-email-enabled
                    if (command.StartsWith("set-email-enabled ", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSetEmailEnabled(command, writer, server);
                        continue;
                    }

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

                        // ?? Commande manquante ajoutée ici
                        case "get-email-enabled":
                            HandleGetEmailEnabled(writer, server);
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
        // ?? NOUVELLE MÉTHODE : renvoyer l'état EmailSendingEnabled
        private void HandleGetEmailEnabled(StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                writer.Write("{\"enabled\":" + (EmailSendingEnabled ? "true" : "false") + "}");
            }
            catch (Exception ex)
            {
                writer.Write("{\"error\":\"" + ex.Message + "\"}");
                Log("ERREUR get-email-enabled : " + ex);
            }

            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        // Commande : activer/désactiver le log du service
        private void HandleSetLogging(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool enable = parts.Length > 1 && parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);

                ServiceLoggingEnabled = enable;

                Log("IPC : Logging service = " + (enable ? "activé" : "désactivé"));

                writer.Write("{\"status\":\"ok\"}");
            }
            catch (Exception ex)
            {
                writer.Write("{\"status\":\"error\",\"message\":\"" + ex.Message + "\"}");
                Log("ERREUR set-logging : " + ex);
            }

            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        // Commande : activer/désactiver l'envoi automatique d'email
        private void HandleSetEmailEnabled(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool enable = parts.Length > 1 && parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);

                EmailSendingEnabled = enable;

                Log("IPC : EmailSendingEnabled = " + enable);

                // Sauvegarde persistante
                SaveEmailSetting();

                writer.Write("{\"status\":\"ok\"}");
            }
            catch (Exception ex)
            {
                writer.Write("{\"status\":\"error\",\"message\":\"" + ex.Message + "\"}");
                Log("ERREUR set-email-enabled : " + ex);
            }

            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        // Sauvegarde persistante du switch Email
        private void SaveEmailSetting()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor"
                );

                Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, "MediaMonitor.Service.config");

                File.WriteAllText(path, EmailSendingEnabled ? "EmailEnabled=true" : "EmailEnabled=false");

                Log("MediaMonitor.Service.config sauvegardé : " + EmailSendingEnabled);
            }
            catch (Exception ex)
            {
                Log("ERREUR SaveEmailSetting : " + ex);
            }
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
                if (!EmailSendingEnabled)
                {
                    Log("IPC : envoi email désactivé ? send-report ignoré.");
                    writer.Write("{\"status\":\"ok\",\"message\":\"email disabled\"}");
                }
                else
                {
                    _engine.SendReportEmail().Wait();
                    writer.Write("{\"status\":\"ok\"}");
                    Log("IPC : send-report exécuté.");
                }
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
            if (!ServiceLoggingEnabled)
                return;

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

