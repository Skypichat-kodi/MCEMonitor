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

        // >>> AJOUT : mémorisation de la dernière heure connue
        private static (int hour, int minute)? _lastShutdownTime = null;

        // >>> AJOUT : FileSystemWatcher
        private static FileSystemWatcher _shutdownWatcher;

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

        // >>> AJOUT : vider le log de planification
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
            CoreLog.Write("=== MediaMonitor.Service démarré (SYSTEM) ===");

            var engine = new MediaMonitorEngine();

            try
            {
                engine.Start();
                CoreLog.Write("Engine.Start() exécuté.");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR Engine.Start() : " + ex);
            }

            // ------------------------------------------------------------
            // IPC
            // ------------------------------------------------------------
            ServiceIpcServer ipc = null;
            try
            {
                ipc = new ServiceIpcServer(engine);
                ipc.Start();
                CoreLog.Write("IPC Server démarré.");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR IPC Start : " + ex);
            }

            // Charger la config email
            LoadEmailSetting();

            // === AJOUT : SURVEILLANCE DE Shutdown.config ===
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

            // Programmer le premier rapport
            ScheduleNextReport(engine);

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
                {
                    CoreLog.Write("Shutdown.config introuvable.");
                    WriteScheduleLog("Shutdown.config introuvable — impossible de programmer l’envoi automatique.");
                    return null;
                }

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
                {
                    CoreLog.Write($"Shutdown.config chargé : {hour:D2}:{minute:D2}");
                    WriteScheduleLog($"Shutdown.config chargé : {hour:D2}:{minute:D2}");

                    // >>> AJOUT : détection de changement
                    if (_lastShutdownTime == null ||
                        _lastShutdownTime.Value.hour != hour ||
                        _lastShutdownTime.Value.minute != minute)
                    {
                        _lastShutdownTime = (hour, minute);
                        ClearScheduleLog();
                        WriteScheduleLog($"Nouvelle heure détectée : {hour:D2}:{minute:D2}");
                    }

                    return (hour, minute);
                }

                CoreLog.Write("Shutdown.config invalide.");
                WriteScheduleLog("Shutdown.config invalide — valeurs incorrectes.");
                return null;
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR LoadShutdownTime : " + ex);
                WriteScheduleLog("ERREUR LoadShutdownTime : " + ex.Message);
                return null;
            }
        }

        // ------------------------------------------------------------
        // CALCUL DE L'HEURE D'ENVOI DU RAPPORT
        // ------------------------------------------------------------
        private static DateTime GetReportSendTime()
        {
            var shutdown = LoadShutdownTime();

            if (shutdown == null)
            {
                CoreLog.Write("Aucune heure de shutdown — rapport dans 1 minute.");
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
                $"Prochain envoi du rapport prévu à {target:HH:mm} " +
                $"(dans {remaining.Hours}h {remaining.Minutes}min)";

            CoreLog.Write(msg);
            WriteScheduleLog(msg);

            return target;
        }

        // ------------------------------------------------------------
        // PROGRAMMATION DU TIMER
        // ------------------------------------------------------------
        private static void ScheduleNextReport(MediaMonitorEngine engine)
        {
            DateTime sendTime = GetReportSendTime();
            TimeSpan delay = sendTime - DateTime.Now;

            if (delay.TotalMilliseconds < 0)
                delay = TimeSpan.FromMinutes(1);

            // Format attendu par l’UI
            string msgOld =
                $"Prochain envoi du rapport prévu à {sendTime:HH:mm} " +
                $"(dans {delay.Hours}h {delay.Minutes}min)";

            WriteScheduleLog(msgOld);

            // Nouveau format (on le garde aussi)
            string msgNew =
                $"Timer programmé : prochain rapport dans {delay.Hours}h {delay.Minutes}min " +
                $"(à {sendTime:HH:mm})";

            WriteScheduleLog(msgNew);
            CoreLog.Write(msgNew);


            reportTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    if (!ServiceIpcServer.EmailSendingEnabled)
                    {
                        CoreLog.Write("Envoi automatique désactivé — rapport ignoré.");
                        WriteScheduleLog("Envoi automatique désactivé — rapport ignoré.");
                    }
                    else
                    {
                        CoreLog.Write("Envoi du rapport...");
                        WriteScheduleLog("Envoi du rapport…");

                        await engine.SendReportEmail();

                        CoreLog.Write("Rapport envoyé.");
                        WriteScheduleLog("Rapport envoyé.");

                        engine.ClearHistory();
                        CoreLog.Write("Historique RAM effacé après envoi du rapport.");
                    }
                }
                catch (Exception ex)
                {
                    CoreLog.Write("ERREUR SendReportEmail : " + ex);
                    WriteScheduleLog("ERREUR SendReportEmail : " + ex.Message);
                }

                ScheduleNextReport(engine);

            }, null, delay, Timeout.InfiniteTimeSpan);
        }

        // ------------------------------------------------------------
        // AJOUT : RÉACTION AUX MODIFICATIONS DE Shutdown.config
        // ------------------------------------------------------------
        private static void ShutdownConfigChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                CoreLog.Write("Modification détectée dans Shutdown.config");
                WriteScheduleLog("Modification détectée dans Shutdown.config");

                var newTime = LoadShutdownTime();

                if (newTime == null)
                    return;

                if (_lastShutdownTime == null ||
                    _lastShutdownTime.Value.hour != newTime.Value.hour ||
                    _lastShutdownTime.Value.minute != newTime.Value.minute)
                {
                    _lastShutdownTime = newTime.Value;

                    ClearScheduleLog();
                    WriteScheduleLog($"Nouvelle heure détectée : {_lastShutdownTime.Value.hour:D2}:{_lastShutdownTime.Value.minute:D2}");
                }

                // ?? Toujours écrire la ligne attendue par l’UI, même si l’heure n’a pas changé
                DateTime next = DateTime.Today
                    .AddHours(newTime.Value.hour)
                    .AddMinutes(newTime.Value.minute)
                    .AddMinutes(-10);

                if (next < DateTime.Now)
                    next = next.AddDays(1);

                TimeSpan remaining = next - DateTime.Now;

                WriteScheduleLog(
                    $"Prochain envoi du rapport prévu à {next:HH:mm} (dans {remaining.Hours}h {remaining.Minutes}min)"
                );
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR ShutdownConfigChanged : " + ex);
                WriteScheduleLog("ERREUR ShutdownConfigChanged : " + ex.Message);
            }
        }
    }
}

