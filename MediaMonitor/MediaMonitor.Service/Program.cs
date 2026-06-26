using System;
using System.IO;
using System.Text;
using System.Threading;
using MediaMonitor.Core.Services;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using MediaMonitor.Core.Models;
using MediaMonitor.Core.Language;

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
        private static System.Threading.Timer hourlyBackupTimer;
        private static FileSystemWatcher _webConfigWatcher;
        private static bool _dvbViewerEnabled = false;

        // Anti-rebond
        private static DateTime _lastConfigChange = DateTime.MinValue;

        // Dernier statut CODE02
        private static string _lastReportStatus =
            "[CODE02] " + (LanguageManager.Get("Dernier rapport inexistant") ?? "Dernier rapport inexistant");

        private static int LoadRetentionDays()
        {
            var settings = WebServerSettings.Load();
            return settings.RetentionDays;
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
                "[CODE01] " +
                (LanguageManager.Get("Prochain envoi du rapport prévu à") ?? "Prochain envoi du rapport prévu à")
                + $" {target:HH:mm} "
                + "("
                + (LanguageManager.Get("dans") ?? "dans")
                + $" {remaining.Hours}h {remaining.Minutes}min)";

            WriteScheduleLog(msg);

            return target;
        }

        // ------------------------------------------------------------
        // PROGRAMMATION DU TIMER
        // ------------------------------------------------------------
        private static bool _isSending = false;
        private static DateTime _lastReportSent = DateTime.MinValue;

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
                if (_isSending)
                    return;

                _isSending = true;

                try
                {
                // Protection anti-double envoi : fenêtre de 30 secondes
                if ((DateTime.Now - _lastReportSent) < TimeSpan.FromSeconds(30))
                {
                    WriteScheduleLog($"[CODE03] Double envoi évité — last={_lastReportSent:HH:mm:ss}, now={DateTime.Now:HH:mm:ss}");
                    return;
                }
                WriteScheduleLog("Envoi du rapport…");

                // Calculer le prochain envoi AVANT l’envoi
                DateTime nextSend = GetReportSendTime();

                // Envoi (pas de bool possible)
                await engine.SendReportEmail();

                // Mise à jour anti-doublon
                _lastReportSent = DateTime.Now;

                _lastReportStatus = $"[CODE02] Rapport envoyé à {_lastReportSent:yyyy-MM-dd HH:mm:ss}";
                WriteScheduleLog(_lastReportStatus);
                WriteScheduleLog("DEBUG: Count=" + engine.GetHistory().Count);

                // Sauvegarde JSON AVANT ClearHistory()
                SaveBackup(engine);

                // Maintenant seulement on vide l’historique RAM
                engine.ClearHistory();

                // Programmer le prochain envoi
                ScheduleNextReport(engine);
                }
                catch (Exception ex)
                {
                    WriteScheduleLog("ERREUR SendReportEmail : " + ex.Message);
                }
                finally
                {
                    _isSending = false;
                }

            }, null, delay, Timeout.InfiniteTimeSpan);
        }

        private static void SaveBackup(MediaMonitorEngine engine)
        {
            try
            {
                int retentionDays = LoadRetentionDays(); // 0, 7, 14, 30

                if (retentionDays > 0)
                {
                    string backupDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MCEMonitor",
                        "Backups"
                    );

                    Directory.CreateDirectory(backupDir);

                    string backupPath = Path.Combine(backupDir, "history_backup.json");

                    // Charger l'ancien backup s'il existe
                    // Charger l'ancien backup s'il existe
                    BackupFileModel backup = null;

                    if (File.Exists(backupPath))
                    {
                        string oldJson = File.ReadAllText(backupPath);
                        backup = JsonConvert.DeserializeObject<BackupFileModel>(oldJson);
                    }

                    if (backup == null)
                        backup = new BackupFileModel { RetentionDays = retentionDays, Reports = new List<DailyReport>() };

                    // ------------------------------------------------------------
                    // ?? INCRÉMENTIEL : fusionner les items du jour (version corrigée)
                    // ------------------------------------------------------------
                    DateTime today = DateTime.Now.Date;

                    // Chercher le rapport du jour existant
                    var existing = backup.Reports.FirstOrDefault(r => r.Date == today);

                    if (existing == null)
                    {
                        existing = new DailyReport
                        {
                            Date = today,
                            Items = new List<MediaUsageItem>()
                        };
                        backup.Reports.Add(existing);
                    }

                    // Nettoyage des doublons dans le backup JSON
                    existing.Items = existing.Items
                        .GroupBy(i => new { i.Path, i.ClientIP, i.MediaType, i.Nom, i.Saison, i.Episode, i.FileName, i.UNC })
                        .Select(g => g.First())
                        .ToList();

                    // Récupérer les items RAM
                    var newItems = engine.GetHistory();

                    // Filtrer les nouveaux items (anti-doublons)
                    var filtered = newItems.Where(item =>
                        !existing.Items.Any(x =>
                            x.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase) &&
                            x.ClientIP.Equals(item.ClientIP, StringComparison.OrdinalIgnoreCase) &&
                            x.MediaType == item.MediaType &&
                            x.Nom == item.Nom &&
                            x.Saison == item.Saison &&
                            x.Episode == item.Episode &&
                            x.FileName == item.FileName &&
                            x.UNC == item.UNC
                        )
                    ).ToList();

                    // Ajouter uniquement les nouveaux items
                    foreach (var item in filtered)
                    {
                        existing.Items.Add(item);
                    }

                    // ?? Loguer les doublons réels (ceux filtrés)
                    foreach (var item in newItems.Except(filtered))
                    {
                        WriteScheduleLog($"[CODE04] Doublon ignoré : {item.Path} (client {item.ClientIP}, {item.Timestamp:HH:mm:ss})");
                    }

                    // (OPTIONNEL) — Si tu veux loguer combien ont été ajoutés
                    WriteScheduleLog($"Backup : {filtered.Count} nouveaux items ajoutés.");

                    // ------------------------------------------------------------
                    // ?? Rétention glissante : supprimer les jours trop anciens
                    // ------------------------------------------------------------
                    DateTime limit = today.AddDays(-retentionDays);
                    backup.Reports.RemoveAll(r => r.Date < limit);

                    // Sauvegarder le fichier final
                    string json = JsonConvert.SerializeObject(backup, Formatting.Indented);
                    File.WriteAllText(backupPath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                LogService.WriteError("Erreur lors de la sauvegarde cumulée : " + ex.Message);
            }
        }

        private static void StartHourlyBackup(MediaMonitorEngine engine)
        {
            hourlyBackupTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    int count = engine.GetHistory().Count;

                    WriteScheduleLog("DEBUG: Count=" + count);

                    SaveBackup(engine);

                    if (count == 0)
                        WriteScheduleLog("Backup effectué (historique vide pour le moment).");
                    else
                        WriteScheduleLog($"Backup effectué ({count} médias).");

                    hourlyBackupTimer.Change(TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan);
                }
                catch (Exception ex)
                {
                    WriteScheduleLog("Erreur backup horaire : " + ex.Message);
                }

            }, null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
        }

        public static void RestartBackupTimer()
        {
            try
            {
                hourlyBackupTimer?.Dispose();
                StartHourlyBackup(_engine);
                WriteScheduleLog("Timer de sauvegarde redémarré suite au changement de rétention.");
            }
            catch (Exception ex)
            {
                WriteScheduleLog("Erreur RestartBackupTimer : " + ex.Message);
            }
        }

        private static DateTime _lastWebConfigChange = DateTime.MinValue;
        
        private static void WebConfigChanged_Master(object sender, FileSystemEventArgs e)
        {
            var now = DateTime.Now;

            // Anti-rebond : si un événement est arrivé il y a moins de 1 seconde, on ignore
            if ((now - _lastWebConfigChange).TotalMilliseconds < 1000)
                return;

            _lastWebConfigChange = now;
            
            ScheduleNextReport(_engine);

            WebConfigChanged_Retention(sender, e);
            WebConfigChanged_DvbViewer(sender, e);
        }

        private static void WebConfigChanged_Retention(object sender, FileSystemEventArgs e)
        {
            try
            {
                CoreLog.Write("WebConfigChanged déclenché !");
                Thread.Sleep(200);

                var settings = WebServerSettings.Load();
                int days = settings.RetentionDays;

                RestartBackupTimer();

                WriteScheduleLog($"Rétention mise à jour via Web.config : {days} jours");
                WriteScheduleLog("Timer de sauvegarde reprogrammé suite au changement de rétention.");
            }
            catch (Exception ex)
            {
                WriteScheduleLog("Erreur WebConfigChanged : " + ex.Message);
                CoreLog.Write("Erreur WebConfigChanged : " + ex);
            }
        }

        private static void WebConfigChanged_DvbViewer(object sender, FileSystemEventArgs e)
        {
            try
            {
                Thread.Sleep(200);

                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "MediaMonitor.Web.config"
                );

                if (!File.Exists(path))
                    return;

                var lines = File.ReadAllLines(path);

                foreach (var line in lines)
                {
                    if (line.StartsWith("DvbViewerSwitch=", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line.Split('=')[1].Trim();
                        bool enabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);

                        if (enabled != _dvbViewerEnabled)
                        {
                            _dvbViewerEnabled = enabled;
                            _engine.DvbViewerEnabled = enabled;

                            CoreLog.Write("DVBViewer RS " + (enabled ? "ACTIVÉ" : "DÉSACTIVÉ") + " via Web.config");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR WebConfigChanged_DvbViewer : " + ex);
            }
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

            var cfg = WebServerSettings.Load();
            _engine.DvbViewerUrl = cfg.DvbViewerUrl;
            _engine.DvbViewerUser = cfg.DvbViewerUser;
            _engine.DvbViewerPass = cfg.DvbViewerPass;
            _dvbViewerEnabled = cfg.DvbViewerSwitch;   // bool déjà dans WebServerSettings
            CoreLog.Write("DVBViewer RS initial: " + _dvbViewerEnabled);           

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

            // SURVEILLANCE DE MediaMonitor.Web.config
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor"
                );

            _webConfigWatcher = new FileSystemWatcher(folder, "MediaMonitor.Web.config");
            _webConfigWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;

            // ?? Un seul handler maître
            _webConfigWatcher.Changed += WebConfigChanged_Master;
            _webConfigWatcher.Created += WebConfigChanged_Master;
            _webConfigWatcher.Renamed += WebConfigChanged_Master;

            _webConfigWatcher.EnableRaisingEvents = true;

                CoreLog.Write("FileSystemWatcher actif sur MediaMonitor.Web.config");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR FileSystemWatcher WebConfig : " + ex);
            }

            ScheduleNextReport(_engine);
            StartHourlyBackup(_engine);

            CoreLog.Write("Service en attente (Thread.Sleep Infinite).");
            Thread.Sleep(Timeout.Infinite);
        }
    }
}

