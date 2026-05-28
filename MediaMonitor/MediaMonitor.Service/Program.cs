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

            // ?? Réinitialisation du log au démarrage
            ClearLog();

            Log("=== MediaMonitor.Service démarré (SYSTEM) ===");

            var engine = new MediaMonitorEngine();

            try
            {
                engine.Start();
                Log("Engine.Start() exécuté.");
            }
            catch (Exception ex)
            {
                Log("ERREUR Engine.Start() : " + ex);
            }

            // ------------------------------------------------------------
            // IPC : permet au Tray d'envoyer "shutdown"
            // ------------------------------------------------------------
            try
            {
                var ipc = new ServiceIpcServer(engine);
                ipc.Start();
                Log("IPC Server démarré.");
            }
            catch (Exception ex)
            {
                Log("ERREUR IPC Start : " + ex);
            }

            ScheduleNextReport(engine);

            Log("Service en attente (Thread.Sleep Infinite).");

            Thread.Sleep(Timeout.Infinite);
        }

        // ------------------------------------------------------------
        // LOGGING
        // ------------------------------------------------------------

        // ?? Nouveau : réinitialisation du log au démarrage
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
            catch
            {
                // On ne casse jamais le service pour un log
            }
        }

        private static void Log(string message)
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
            catch
            {
                // On ne casse jamais le service pour un log
            }
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
                    Log("Shutdown.config introuvable.");
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
                    Log($"Shutdown.config chargé : {hour:D2}:{minute:D2}");
                    return (hour, minute);
                }

                Log("Shutdown.config invalide.");
                return null;
            }
            catch (Exception ex)
            {
                Log("ERREUR LoadShutdownTime : " + ex);
                return null;
            }
        }

        private static DateTime GetReportSendTime()
        {
            var shutdown = LoadShutdownTime();

            if (shutdown == null)
            {
                Log("Aucune heure de shutdown, rapport dans 1 minute.");
                return DateTime.Now.AddMinutes(1);
            }

            var target = DateTime.Today
                .AddHours(shutdown.Value.hour)
                .AddMinutes(shutdown.Value.minute)
                .AddMinutes(-10);

            if (target < DateTime.Now)
                target = target.AddDays(1);

            Log($"Prochain envoi de rapport prévu à : {target:HH:mm:ss}");

            return target;
        }

        private static void ScheduleNextReport(MediaMonitorEngine engine)
        {
            DateTime sendTime = GetReportSendTime();
            TimeSpan delay = sendTime - DateTime.Now;

            if (delay.TotalMilliseconds < 0)
                delay = TimeSpan.FromMinutes(1);

            Log($"Timer rapport programmé dans {delay.TotalMinutes:F1} minutes.");

            reportTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    Log("Envoi du rapport...");
                    await engine.SendReportEmail();
                    Log("Rapport envoyé.");

                    // ?? Effacer l’historique en RAM après envoi
                    engine.ClearHistory();
                    Log("Historique RAM effacé après envoi du rapport.");
                }
                catch (Exception ex)
                {
                    Log("ERREUR SendReportEmail : " + ex);
                }

                ScheduleNextReport(engine);

            }, null, delay, Timeout.InfiniteTimeSpan);
        }
    }
}

