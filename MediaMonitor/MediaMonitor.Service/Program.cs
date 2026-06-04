using System;
using System.IO;
using System.Text;
using System.Threading;
using MediaMonitor.Core.Services;

namespace MediaMonitor.Service
{
    internal static class Program
    {
        private static System.Threading.Timer reportTimer;
        private static Mutex _mutex;
        private static (int hour, int minute)? _lastShutdownTime = null;
        private static FileSystemWatcher _shutdownWatcher;
        private static MediaMonitorEngine _engine;
        private static WebServer _webServer;

        // Anti-rebond
        private static DateTime _lastConfigChange = DateTime.MinValue;

        // Dernier statut CODE02
        private static string _lastReportStatus = "[CODE02] Dernier rapport inexistant";

        // ------------------------------------------------------------
        // LOGGER DÉDIÉ À LA PLANIFICATION
        // ------------------------------------------------------------
        private static void WriteScheduleLog(string message)
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                Directory.CreateDirectory(folder);

                string file = Path.Combine(folder, "MediaMonitor.Schedule.log");

                File.AppendAllText(file,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n"
                );
            }
            catch { }
        }

        private static void ClearScheduleLog()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                Directory.CreateDirectory(folder);

                string file = Path.Combine(folder, "MediaMonitor.Schedule.log");

                File.WriteAllText(file, string.Empty);
            }
            catch { }
        }

        static void Main()
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_Service", out createdNew);
            if (!createdNew)
                return;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            CoreLog.IsLoggingEnabled = () => ServiceIpcServer.ServiceLoggingEnabled;

            ClearLog();
            ClearScheduleLog();
            CoreLog.Write("=== MediaMonitor.Service démarré (SYSTEM) ===");

            _engine = new MediaMonitorEngine();

            try
            {
                _engine.Start();
                CoreLog.Write("Engine.Start() exécuté.");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR Engine.Start() : " + ex);
            }

            _lastReportStatus = "[CODE02] Dernier rapport inexistant";
            WriteScheduleLog(_lastReportStatus);

            // ?? AJOUT : charger la rétention AVANT de démarrer l’IPC
            ServiceIpcServer.LoadBackupRetention();

            // IPC
            ServiceIpcServer ipc = null;
            try
            {
                ipc = new ServiceIpcServer(_engine);
                ipc.Start();
                CoreLog.Write("IPC Server démarré.");

                StartWebServerIfEnabled();
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR IPC Start : " + ex);
            }

            LoadEmailSetting();

            // SURVEILLANCE DE Shutdown.config
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor"
                );

                _shutdownWatcher = new FileSystemWatcher(folder, "Shutdown.config");
                _shutdownWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
                _shutdownWatcher.Changed += ShutdownConfigChanged;
                _shutdownWatcher.Created += ShutdownConfigChanged;
                _shutdownWatcher.Renamed += ShutdownConfigChanged;
                _shutdownWatcher.EnableRaisingEvents = true;

                CoreLog.Write("FileSystemWatcher actif sur Shutdown.config");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR FileSystemWatcher : " + ex);
            }

            ScheduleNextReport(_engine);

            CoreLog.Write("Service en attente (Thread.Sleep Infinite).");
            Thread.Sleep(Timeout.Infinite);
        }

        // ------------------------------------------------------------
        // CHARGEMENT DU SWITCH EMAIL
        // ------------------------------------------------------------
        private static void LoadEmailSetting()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor"
                );

                Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, "MediaMonitor.Service.config");

                if (!File.Exists(path))
                {
                    ServiceIpcServer.EmailSendingEnabled = true;
                    CoreLog.Write("MediaMonitor.Service.config introuvable ? EmailSendingEnabled = true");
                    return;
                }

                string content = File.ReadAllText(path).Trim().ToLower();
                ServiceIpcServer.EmailSendingEnabled = content.Contains("true");

                CoreLog.Write("MediaMonitor.Service.config chargé ? EmailSendingEnabled = "
                    + ServiceIpcServer.EmailSendingEnabled);
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR LoadEmailSetting : " + ex);
                ServiceIpcServer.EmailSendingEnabled = true;
            }
        }

        private static void ClearLog()
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

                File.WriteAllText(file, string.Empty);
            }
            catch { }
        }

        // ------------------------------------------------------------
        // CALCUL DE L'HEURE D'ENVOI DU RAPPORT
        // ------------------------------------------------------------
        private static DateTime GetReportSendTime()
        {
            var shutdown = LoadShutdownTime();

            if (shutdown == null)
            {
                WriteScheduleLog("Aucune heure de shutdown — rapport dans 1 minute.");
                return DateTime.Now.AddMinutes(1);
            }

            var target = DateTime.Today
                .AddHours(shutdown.Value.hour)
                .AddMinutes(shutdown.Value.minute)
                .AddMinutes(-10);

            if (target < DateTime.Now)
                target = target.AddDays(1);

            TimeSpan remaining = target - DateTime.Now;

            string msg =
                $"[CODE01] Prochain envoi du rapport prévu à {target:HH:mm} " +
                $"(dans {remaining.Hours}h {remaining.Minutes}min)";

            WriteScheduleLog(msg);

            return target;
        }

        // ------------------------------------------------------------
        // PROGRAMMATION DU TIMER — VERSION CORRIGÉE
        // ------------------------------------------------------------
        private static void ScheduleNextReport(MediaMonitorEngine engine)
        {
            reportTimer?.Dispose();
            reportTimer = null;

            DateTime sendTime = GetReportSendTime();
            TimeSpan delay = sendTime - DateTime.Now;

            if (delay.TotalMilliseconds < 0)
                delay = TimeSpan.FromMinutes(1);

            reportTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    if (!ServiceIpcServer.EmailSendingEnabled)
                    {
                        WriteScheduleLog("Envoi automatique désactivé — rapport ignoré.");
                    }
                    else
                    {
                        WriteScheduleLog("Envoi du rapport…");

                        await engine.SendReportEmail();

                        _lastReportStatus = $"[CODE02] Rapport envoyé à {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                        WriteScheduleLog(_lastReportStatus);

                        engine.ClearHistory();
                    }
                }
                catch (Exception ex)
                {
                    WriteScheduleLog("ERREUR SendReportEmail : " + ex.Message);
                }

                ScheduleNextReport(engine);

            }, null, delay, Timeout.InfiniteTimeSpan);
        }

        // ------------------------------------------------------------
        // CHARGEMENT DE L'HEURE DE SHUTDOWN
        // ------------------------------------------------------------
        private static (int hour, int minute)? LoadShutdownTime()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Shutdown.config"
                );

                if (!File.Exists(path))
                    return null;

                var lines = File.ReadAllLines(path);

                int hour = -1;
                int minute = -1;

                foreach (var line in lines)
                {
                    if (line.StartsWith("Hour="))
                        hour = int.Parse(line.Split('=')[1]);

                    if (line.StartsWith("Minute="))
                        minute = int.Parse(line.Split('=')[1]);
                }

                if (hour >= 0 && minute >= 0)
                    return (hour, minute);

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------
        // RÉACTION AUX MODIFICATIONS DE Shutdown.config
        // ------------------------------------------------------------
        private static void ShutdownConfigChanged(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.Now - _lastConfigChange).TotalMilliseconds < 200)
                return;

            _lastConfigChange = DateTime.Now;

            try
            {
                var newTime = LoadShutdownTime();
                if (newTime == null)
                    return;

                bool changed =
                    _lastShutdownTime == null ||
                    _lastShutdownTime.Value.hour != newTime.Value.hour ||
                    _lastShutdownTime.Value.minute != newTime.Value.minute;

                _lastShutdownTime = newTime.Value;

                ClearScheduleLog();

                if (changed)
                    WriteScheduleLog($"Nouvelle heure détectée : {_lastShutdownTime.Value.hour:D2}:{_lastShutdownTime.Value.minute:D2}");

                WriteScheduleLog($"Shutdown.config chargé : {_lastShutdownTime.Value.hour:D2}:{_lastShutdownTime.Value.minute:D2}");

                WriteScheduleLog(_lastReportStatus);

                ScheduleNextReport(_engine);
            }
            catch (Exception ex)
            {
                WriteScheduleLog("ERREUR ShutdownConfigChanged : " + ex.Message);
            }
        }

        internal static void StartWebServerIfEnabled()
        {
            try
            {
                var settings = WebServerSettings.Load();

                if (!settings.Enabled)
                {
                    CoreLog.Write("Serveur Web désactivé (Enabled=false).");
                    return;
                }

                _webServer = new WebServer(settings.Port, _engine);
                _webServer.Start();

                CoreLog.Write($"Serveur Web démarré sur le port {settings.Port}.");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR StartWebServerIfEnabled : " + ex);
            }
        }

        internal static void StopWebServer()
        {
            try
            {
                _webServer?.Stop();
                _webServer = null;
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR StopWebServer : " + ex);
            }
        }
    }
}

