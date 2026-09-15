using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using MediaMonitor.Core.Services;
using MediaMonitor.Core.Language;
using MediaMonitor.Service.Reports;

namespace MediaMonitor.Service.Ipc
{
    /// <summary>
    /// Traite les commandes IPC (lecture, écriture de config, actions).
    /// </summary>
    public class IpcCommandHandler
    {
        private readonly MediaMonitorEngine _engine;

        public IpcCommandHandler(MediaMonitorEngine engine)
        {
            _engine = engine;
        }

        // ---------------------------------------------------------------
        //  Dispatch principal
        // ---------------------------------------------------------------
        public void Handle(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            // Commandes avec argument
            if (command.StartsWith("set-logging ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetLogging(command, writer, server);
                return;
            }

            if (command.StartsWith("set-email-enabled ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetEmailEnabled(command, writer, server);
                return;
            }

            if (command.StartsWith("set-web-enabled ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetWebEnabled(command, writer, server);
                return;
            }

            if (command.StartsWith("set-web-port ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetWebPort(command, writer, server);
                return;
            }

            if (command.StartsWith("set-web-credentials ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetWebCredentials(command, writer, server);
                return;
            }

            if (command.StartsWith("set-retention ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetRetention(command, writer, server);
                return;
            }

            if (command.StartsWith("set-dvb-config ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetDvbConfig(command, writer, server);
                return;
            }

            if (command.StartsWith("get-file-info ", StringComparison.OrdinalIgnoreCase))
            {
                HandleGetFileInfo(command, writer, server);
                return;
            }

            // Commandes exactes
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

                case "get-dvb-config":
                    HandleGetDvbConfig(writer, server);
                    break;

                default:
                    IpcResponse.Error(writer, server, "unknown command");
                    break;
            }
        }

        // ===============================================================
        //  HANDLERS : SET
        // ===============================================================

        private void HandleSetLogging(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool enable = parts.Length > 1 && parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);

                ServiceIpcServer.ServiceLoggingEnabled = enable;

                IpcResponse.Ok(writer, server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        private void HandleSetEmailEnabled(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool enable = parts.Length > 1 && parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);

                ServiceIpcServer.EmailSendingEnabled = enable;
                SaveEmailSetting(enable);

                IpcResponse.Ok(writer, server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
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

                Program.StopWebServer();
                Program.StartWebServerIfEnabled();

                IpcResponse.Ok(writer, server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        private void HandleSetWebPort(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2 || !int.TryParse(parts[1], out int newPort))
                {
                    IpcResponse.Error(writer, server, "invalid port");
                    return;
                }

                Program.StopWebServer();
                FirewallHelper.UpdateFirewallRule(newPort);

                var settings = WebServerSettings.Load();
                settings.Port = newPort;
                settings.Save();

                Program.StartWebServerIfEnabled();

                IpcResponse.Ok(writer, server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        private void HandleSetWebCredentials(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 3)
                {
                    IpcResponse.Error(writer, server, "missing parameters");
                    return;
                }

                string username = parts[1];
                string password = parts[2];

                var settings = WebServerSettings.Load();
                settings.Username = username;
                settings.Password = password;
                settings.Save();

                Program.StopWebServer();
                Program.StartWebServerIfEnabled();

                IpcResponse.Ok(writer, server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        private void HandleSetRetention(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2 || !int.TryParse(parts[1], out int days))
                {
                    IpcResponse.Error(writer, server, "invalid retention");
                    return;
                }

                var settings = WebServerSettings.Load();
                settings.RetentionDays = days;
                settings.Save();

                Program.RestartBackupTimer();

                IpcResponse.OkWithMessage(writer, server, "Rétention mise à jour. Sauvegarde reprogrammée.");
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        private void HandleSetDvbConfig(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string payload = command.Substring("set-dvb-config ".Length);
                string[] parts = payload.Split('|');

                if (parts.Length != 3)
                {
                    IpcResponse.Error(writer, server, "invalid parameters");
                    return;
                }

                var cfg = WebServerSettings.Load();
                cfg.DvbViewerUrl = parts[0];
                cfg.DvbViewerUser = parts[1];
                cfg.DvbViewerPass = parts[2];
                cfg.Save();

                IpcResponse.Ok(writer, server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        // ===============================================================
        //  HANDLERS : GET
        // ===============================================================

        private void HandleGetState(StreamWriter writer, NamedPipeServerStream server)
        {
            var state = new
            {
                openFiles = _engine.GetCurrentOpenFiles(),
                lastImage = _engine.GetLastImage()
            };
            IpcResponse.Json(writer, server, state);
        }

        private void HandleGetHistory(StreamWriter writer, NamedPipeServerStream server)
        {
            IpcResponse.Json(writer, server, _engine.GetHistory());
        }

        private void HandleGetReport(StreamWriter writer, NamedPipeServerStream server)
        {
            string html = MediaMonitor.Service.Web.Reports.ReportHtmlBuilder.Build(_engine.GetHistory());
            IpcResponse.Json(writer, server, new { report = html });
        }

        private void HandleGetEmailEnabled(StreamWriter writer, NamedPipeServerStream server)
        {
            IpcResponse.Json(writer, server, new { enabled = ServiceIpcServer.EmailSendingEnabled });
        }

        private void HandleGetWebEnabled(StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                var settings = WebServerSettings.Load();
                IpcResponse.Json(writer, server, new { enabled = settings.Enabled });
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        private void HandleGetWebPort(StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                var settings = WebServerSettings.Load();
                IpcResponse.Json(writer, server, new { port = settings.Port });
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        private void HandleGetRetention(StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                var settings = WebServerSettings.Load();
                IpcResponse.Json(writer, server, new { days = settings.RetentionDays });
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        private void HandleGetDvbConfig(StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                var cfg = WebServerSettings.Load();
                IpcResponse.Json(writer, server, new
                {
                    url = cfg.DvbViewerUrl,
                    user = cfg.DvbViewerUser,
                    pass = cfg.DvbViewerPass
                });
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        private void HandleGetFileInfo(string command, StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                string path = command.Substring("get-file-info".Length).Trim();

                if (string.IsNullOrWhiteSpace(path))
                {
                    IpcResponse.Error(writer, server, "empty path");
                    return;
                }

                if (!File.Exists(path))
                {
                    IpcResponse.Error(writer, server, "file not found");
                    return;
                }

                var info = FileAnalyzer.Analyze(path);
                IpcResponse.Json(writer, server, info);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        // ===============================================================
        //  HANDLERS : ACTIONS
        // ===============================================================

        private void HandleShutdown()
        {
            try
            {
                Program.StopWebServer();
                _engine.Stop();
            }
            catch { }

            Environment.Exit(0);
        }

        private void HandleSendReport(StreamWriter writer, NamedPipeServerStream server)
        {
            try
            {
                if (!ServiceIpcServer.EmailSendingEnabled)
                {
                    IpcResponse.OkWithMessage(writer, server, "email disabled");
                    return;
                }

                EmailReportSender.SendAsync(_engine.GetHistory()).Wait();
                IpcResponse.Ok(writer, server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(writer, server, ex.Message);
            }
        }

        // ===============================================================
        //  PERSISTENCE
        // ===============================================================

        private void SaveEmailSetting(bool enabled)
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor"
                );

                Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, "MediaMonitor.Service.config");

                File.WriteAllText(path, enabled ? "EmailEnabled=true" : "EmailEnabled=false");
            }
            catch { }
        }
    }
}