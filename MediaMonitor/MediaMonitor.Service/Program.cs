using System;
using System.IO;
using System.Text;
using System.Threading;
using MediaMonitor.Core.Services;
using System.Text.Json;
using System.Collections.Generic;
using MediaMonitor.Core.Models;
using System.Linq;

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

                        // ? AJOUT : sauvegarde avant purge
                        SaveHistoryBackup(engine);

                        // ? AJOUT : rétention
                        CleanupOldBackups();

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
// =============================================================
//  SAUVEGARDE FUSIONNÉE (Option B : start/end + fusion)
// =============================================================
private static void SaveHistoryBackup(MediaMonitorEngine engine)
{
    try
    {
        string baseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Backups"
        );

        Directory.CreateDirectory(baseFolder);

        // 1) Charger l'historique RAM du jour
        var todayItems = engine.GetHistory(); // List<MediaUsageItem>

        // 2) Charger les anciennes sauvegardes encore dans la période
        int days = ServiceIpcServer.BackupRetentionDays;
        DateTime limit = DateTime.Now.AddDays(-days);

        var mergedItems = new List<MediaUsageItem>();

        var oldFiles = Directory.GetFiles(baseFolder, "history_*.json");

        foreach (var file in oldFiles)
        {
            try
            {
                // Extraire la date de fin du fichier
                // Format attendu : history_YYYY-MM-DD_to_YYYY-MM-DD.json
                string name = Path.GetFileNameWithoutExtension(file);

                if (!name.Contains("_to_"))
                    continue;

                string endPart = name.Split("_to_")[1];
                if (!DateTime.TryParse(endPart, out DateTime endDate))
                    continue;

                // Garder seulement les fichiers dans la période
                if (endDate < limit)
                    continue;

                // Charger le JSON
                string json = File.ReadAllText(file);
                var wrapper = JsonSerializer.Deserialize<BackupWrapper>(json);

                if (wrapper?.Items != null)
                    mergedItems.AddRange(wrapper.Items);
            }
            catch { }
        }

        // 3) Ajouter les items du jour
        mergedItems.AddRange(todayItems);

        if (mergedItems.Count == 0)
            return;

        // 4) Calculer start/end
        DateTime start = mergedItems.Min(i => i.Timestamp).Date;
        DateTime end = mergedItems.Max(i => i.Timestamp).Date;

        // 5) Construire le wrapper final
        var finalWrapper = new BackupWrapper
        {
            Start = start,
            End = end,
            Items = mergedItems.OrderBy(i => i.Timestamp).ToList()
        };

        // 6) Nom du fichier final
        string finalFile = Path.Combine(
            baseFolder,
            $"history_{start:yyyy-MM-dd}_to_{end:yyyy-MM-dd}.json"
        );

        // 7) Écrire le fichier fusionné
        string finalJson = JsonSerializer.Serialize(finalWrapper, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(finalFile, finalJson);

        CoreLog.Write("Sauvegarde fusionnée créée : " + finalFile);

        // 8) Supprimer les anciens fichiers individuels
        foreach (var file in oldFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch { }
        }
    }
    catch (Exception ex)
    {
        CoreLog.Write("ERREUR SaveHistoryBackup : " + ex);
    }
}


// =============================================================
//  WRAPPER JSON POUR START/END
// =============================================================
private class BackupWrapper
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public List<MediaUsageItem> Items { get; set; } = new();
}


        // =============================================================
        //  RÉTENTION DES SAUVEGARDES
        // =============================================================
        private static void CleanupOldBackups()
        {
            try
            {
                if (ServiceIpcServer.BackupRetentionDays <= 0)
                    return;

                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Backups"
                );

                if (!Directory.Exists(folder))
                    return;

                var files = Directory.GetFiles(folder, "history_*.json");

                foreach (var f in files)
                {
                    if (File.GetCreationTime(f) < DateTime.Now.AddDays(-ServiceIpcServer.BackupRetentionDays))
                    {
                        File.Delete(f);
                        CoreLog.Write("Backup supprimé (rétention) : " + f);
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR CleanupOldBackups : " + ex);
            }
        }
        
    }
}

