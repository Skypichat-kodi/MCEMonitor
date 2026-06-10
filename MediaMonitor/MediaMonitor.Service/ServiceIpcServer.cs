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

                    // ------------------------------------------------------------
                    // COMMANDES AVEC ARGUMENTS
                    // ------------------------------------------------------------

                    // set-logging true/false
                    if (command.StartsWith("set-logging ", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSetLogging(command, writer, server);
                        continue;
                    }

                    // set-email-enabled true/false
                    if (command.StartsWith("set-email-enabled ", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSetEmailEnabled(command, writer, server);
                        continue;
                    }

                    // set-web-enabled true/false
                    if (command.StartsWith("set-web-enabled ", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSetWebEnabled(command, writer, server);
                        continue;
                    }

                    // set-web-port 8081
                    if (command.StartsWith("set-web-port ", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSetWebPort(command, writer, server);
                        continue;
                    }

                    // set-web-credentials login password
                    if (command.StartsWith("set-web-credentials ", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSetWebCredentials(command, writer, server);
                        continue;
                    }

                    // ------------------------------------------------------------
                    // AJOUT : set-retention X
                    // ------------------------------------------------------------
                    if (command.StartsWith("set-retention ", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSetRetention(command, writer, server);
                        continue;
                    }

                    // ------------------------------------------------------------
                    // COMMANDES EXACTES (SANS ARGUMENT)
                    // ------------------------------------------------------------
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

                        case "get-email-enabled":
                            HandleGetEmailEnabled(writer, server);
                            break;

                        case "get-web-enabled":
                            HandleGetWebEnabled(writer, server);
                            break;

                        case "get-web-port":
                            HandleGetWebPort(writer, server);
                            break;

                        case "get-retention":
                            HandleGetRetention(writer, server);
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
        // ------------------------------------------------------------
        // SET WEB CREDENTIALS
        // ------------------------------------------------------------

        private void HandleSetWebCredentials(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 3)
                {
                    writer.Write("{\"status\":\"error\",\"message\":\"missing parameters\"}");
                    writer.Flush();
                    server.WaitForPipeDrain();
                    server.Disconnect();
                    return;
                }

                string username = parts[1];
                string password = parts[2];

                Log($"IPC : Mise à jour Web Credentials : {username} / (hidden)");

                var settings = WebServerSettings.Load();
                settings.Username = username;
                settings.Password = password;
                settings.Save();

                Program.StopWebServer();
                Program.StartWebServerIfEnabled();

                writer.Write("{\"status\":\"ok\"}");
            }
            catch (Exception ex)
            {
                writer.Write("{\"status\":\"error\",\"message\":\"" + ex.Message + "\"}");
                Log("ERREUR set-web-credentials : " + ex);
            }

            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        // ------------------------------------------------------------
        // GETTERS
        // ------------------------------------------------------------

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

        private void HandleGetWebEnabled(StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                var settings = WebServerSettings.Load();
                writer.Write("{\"enabled\":" + (settings.Enabled ? "true" : "false") + "}");
            }
            catch (Exception ex)
            {
                writer.Write("{\"error\":\"" + ex.Message + "\"}");
            }

            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        private void HandleGetWebPort(StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                var settings = WebServerSettings.Load();
                writer.Write("{\"port\":" + settings.Port + "}");
            }
            catch (Exception ex)
            {
                writer.Write("{\"error\":\"" + ex.Message + "\"}");
            }

            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

private void HandleGetRetention(StreamWriter writer, NamedPipeServerStream server)
{
    try
    {
        var settings = WebServerSettings.Load();
        writer.Write("{\"days\":" + settings.RetentionDays + "}");
    }
    catch (Exception ex)
    {
        writer.Write("{\"error\":\"" + ex.Message + "\"}");
        Log("ERREUR get-retention : " + ex);
    }

    writer.Flush();
    server.WaitForPipeDrain();
    server.Disconnect();
}

        // ------------------------------------------------------------
        // SETTERS
        // ------------------------------------------------------------

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

        private void HandleSetEmailEnabled(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool enable = parts.Length > 1 && parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);

                EmailSendingEnabled = enable;

                Log("IPC : EmailSendingEnabled = " + enable);

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

        private void HandleSetWebEnabled(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool enable = parts.Length > 1 && parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);

                var settings = WebServerSettings.Load();
                settings.Enabled = enable;
                settings.Save();

                Log("IPC : WebServer Enabled = " + enable);

                Program.StopWebServer();
                Program.StartWebServerIfEnabled();

                writer.Write("{\"status\":\"ok\"}");
            }
            catch (Exception ex)
            {
                writer.Write("{\"status\":\"error\",\"message\":\"" + ex.Message + "\"}");
                Log("ERREUR set-web-enabled : " + ex);
            }

            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        private void HandleSetWebPort(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2 || !int.TryParse(parts[1], out int newPort))
                {
                    writer.Write("{\"status\":\"error\",\"message\":\"invalid port\"}");
                    return;
                }

                Log("IPC : WebServer Port = " + newPort);

                Program.StopWebServer();

                FirewallHelper.UpdateFirewallRule(newPort);

                var settings = WebServerSettings.Load();
                settings.Port = newPort;
                settings.Save();

                Program.StartWebServerIfEnabled();

                writer.Write("{\"status\":\"ok\"}");
            }
            catch (Exception ex)
            {
                writer.Write("{\"status\":\"error\",\"message\":\"" + ex.Message + "\"}");
                Log("ERREUR set-web-port : " + ex);
            }

            writer.Flush();
            server.WaitForPipeDrain();
            server.Disconnect();
        }

        // ------------------------------------------------------------
        // AJOUT : HandleSetRetention
        // ------------------------------------------------------------

private void HandleSetRetention(string command, StreamWriter writer, NamedPipeServerStream server)
{
    try
    {
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || !int.TryParse(parts[1], out int days))
        {
            writer.Write("{\"status\":\"error\",\"message\":\"invalid retention\"}");
        }
        else
        {
            var settings = WebServerSettings.Load();
            settings.RetentionDays = days;
            settings.Save();

            // ?? Redémarrer le timer de sauvegarde
            Program.RestartBackupTimer();

            Log($"IPC : RetentionDays = {days}");

            // ?? Message amélioré
            writer.Write("{\"status\":\"ok\",\"message\":\"Rétention mise à jour. Sauvegarde reprogrammée.\"}");
        }
    }
    catch (Exception ex)
    {
        writer.Write("{\"status\":\"error\",\"message\":\"" + ex.Message + "\"}");
        Log("ERREUR set-retention : " + ex);
    }

    writer.Flush();
    server.WaitForPipeDrain();
    server.Disconnect();
}

        // ------------------------------------------------------------
        // COMMANDES EXISTANTES
        // ------------------------------------------------------------

        private void HandleShutdown()
        {
            Log("IPC : commande SHUTDOWN reçue.");

            try
            {
                Program.StopWebServer();
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

        // ------------------------------------------------------------
        // PERSISTENCE
        // ------------------------------------------------------------

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

        // ------------------------------------------------------------
        // LOG
        // ------------------------------------------------------------

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

