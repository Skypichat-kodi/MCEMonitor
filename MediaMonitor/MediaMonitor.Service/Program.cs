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

            // ?? IMPORTANT : Charger la config APRÈS la création de ServiceIpcServer
            LoadEmailSetting();

            ScheduleNextReport(engine);

            CoreLog.Write("Service en attente (Thread.Sleep Infinite).");
            Thread.Sleep(Timeout.Infinite);
        }
        // ------------------------------------------------------------
        // CHARGEMENT DU SWITCH EMAIL (persistant)
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
                    return (hour, minute);
                }

                CoreLog.Write("Shutdown.config invalide.");
                return null;
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR LoadShutdownTime : " + ex);
                return null;
            }
        }

        private static DateTime GetReportSendTime()
        {
            var shutdown = LoadShutdownTime();

            if (shutdown == null)
            {
                CoreLog.Write("Aucune heure de shutdown, rapport dans 1 minute.");
                return DateTime.Now.AddMinutes(1);
            }

            var target = DateTime.Today
                .AddHours(shutdown.Value.hour)
                .AddMinutes(shutdown.Value.minute)
                .AddMinutes(-10);

            if (target < DateTime.Now)
                target = target.AddDays(1);

            CoreLog.Write($"Prochain envoi de rapport prévu à : {target:HH:mm:ss}");

            return target;
        }

        private static void ScheduleNextReport(MediaMonitorEngine engine)
        {
            DateTime sendTime = GetReportSendTime();
            TimeSpan delay = sendTime - DateTime.Now;

            if (delay.TotalMilliseconds < 0)
                delay = TimeSpan.FromMinutes(1);

            CoreLog.Write($"Timer rapport programmé dans {delay.TotalMinutes:F1} minutes.");

            reportTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    if (!ServiceIpcServer.EmailSendingEnabled)
                    {
                        CoreLog.Write("Envoi automatique du rapport désactivé ? rapport ignoré.");
                    }
                    else
                    {
                        CoreLog.Write("Envoi du rapport...");
                        await engine.SendReportEmail();
                        CoreLog.Write("Rapport envoyé.");

                        engine.ClearHistory();
                        CoreLog.Write("Historique RAM effacé après envoi du rapport.");
                    }
                }
                catch (Exception ex)
                {
                    CoreLog.Write("ERREUR SendReportEmail : " + ex);
                }

                ScheduleNextReport(engine);

            }, null, delay, Timeout.InfiniteTimeSpan);
        }
    }
}

