using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MediaMonitor.Core.Models;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Models;
using Newtonsoft.Json;

namespace MediaMonitor.Service.Service
{
    /// <summary>
    /// Gère les sauvegardes horaires de l'historique.
    /// </summary>
    public class BackupService
    {
        private readonly MediaMonitorEngine _engine;
        private System.Threading.Timer _hourlyBackupTimer;

        private bool _isWakeUp = false;
        private DateTime _lastWakeUp = DateTime.MinValue;

        public BackupService(MediaMonitorEngine engine)
        {
            _engine = engine;
        }

        // ---------------------------------------------------------------
        //  Démarrage du timer
        // ---------------------------------------------------------------
        public void Start()
        {
            _hourlyBackupTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    int count = _engine.GetHistory().Count;
                    ScheduleLogger.Write("DEBUG: Count=" + count);

                    SaveBackup();

                    if (count == 0)
                    {
                        ScheduleLogger.Write("Backup effectué (historique vide pour le moment).");
                    }
                    else
                    {
                        ScheduleLogger.Write($"Backup effectué ({count} médias).");
                    }

                    _hourlyBackupTimer.Change(TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan);
                }
                catch (Exception ex)
                {
                    ScheduleLogger.Write("Erreur backup horaire : " + ex.Message);
                }
            }, null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
        }

        // ---------------------------------------------------------------
        //  Redémarrage du timer (ex : changement de rétention)
        // ---------------------------------------------------------------
        public void Restart()
        {
            try
            {
                _hourlyBackupTimer?.Dispose();
                Start();
                ScheduleLogger.Write("Timer de sauvegarde redémarré suite au changement de rétention.");
            }
            catch (Exception ex)
            {
                ScheduleLogger.Write("Erreur RestartBackupTimer : " + ex.Message);
            }
        }

        // ---------------------------------------------------------------
        //  Notification sortie de veille
        // ---------------------------------------------------------------
        public void NotifyWakeUp()
        {
            _lastWakeUp = DateTime.Now;
            _isWakeUp = true;
            ScheduleLogger.Write("[WAKEUP] Sortie de veille détectée. Backup suspendu 10 secondes.");
        }

        // ---------------------------------------------------------------
        //  Sauvegarde
        // ---------------------------------------------------------------
        public void SaveBackup()
        {
            try
            {
                if (_isWakeUp)
                {
                    if ((DateTime.Now - _lastWakeUp).TotalSeconds < 10)
                    {
                        ScheduleLogger.Write("[WAKEUP] Backup ignoré (machine vient de sortir de veille).");
                        return;
                    }
                    else
                    {
                        _isWakeUp = false;
                        ScheduleLogger.Write("[WAKEUP] Backup réactivé.");
                    }
                }

                _engine.IsBackupRunning = true;

                int retentionDays = WebServerSettings.Load().RetentionDays;

                if (retentionDays > 0)
                    DoBackup(retentionDays);
            }
            catch (Exception ex)
            {
                LogService.WriteError("Erreur lors de la sauvegarde cumulée : " + ex.Message);
            }
            finally
            {
                _engine.IsBackupRunning = false;
            }
        }

        // ---------------------------------------------------------------
        //  Logique de sauvegarde cumulée
        // ---------------------------------------------------------------
        private void DoBackup(int retentionDays)
        {
            string backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "Backups"
            );

            Directory.CreateDirectory(backupDir);

            string backupPath = Path.Combine(backupDir, "history_backup.json");

            BackupFileModel backup = null;

            if (File.Exists(backupPath))
            {
                string oldJson = File.ReadAllText(backupPath);

                int half = oldJson.Length / 2;
                if (oldJson.Length > 100 && oldJson.Substring(0, half) == oldJson.Substring(half))
                {
                    ScheduleLogger.Write("[CODE05] Duplication détectée dans le JSON. Réparation en cours.");
                }

                backup = JsonConvert.DeserializeObject<BackupFileModel>(oldJson);

                var allItems = backup.Reports
                    .SelectMany(r => r.Items)
                    .GroupBy(i => new { i.Path, i.FileName, i.MediaType, i.Nom, i.Saison, i.Episode, i.Timestamp })
                    .Select(g => g.First())
                    .ToList();

                int totalBefore = backup.Reports.Sum(r => r.Items.Count);
                int totalAfter = allItems.Count;

                if (totalAfter < totalBefore)
                {
                    ScheduleLogger.Write($"[CODE07] Réparation globale : {totalBefore - totalAfter} doublons supprimés.");
                }

                foreach (var report in backup.Reports)
                {
                    report.Items = allItems
                        .Where(i => report.Date == i.Timestamp.Date)
                        .ToList();
                }

                foreach (var report in backup.Reports)
                {
                    int before = report.Items.Count;

                    report.Items = report.Items
                        .GroupBy(i => new { i.Path, i.FileName, i.MediaType, i.Nom, i.Saison, i.Episode })
                        .Select(g => g.First())
                        .ToList();

                    int after = report.Items.Count;

                    if (after < before)
                    {
                        ScheduleLogger.Write($"[CODE06] Réparation JSON : {before - after} doublons dans le jour {report.Date:yyyy-MM-dd}");
                    }
                }
            }

            if (backup == null)
                backup = new BackupFileModel { RetentionDays = retentionDays, Reports = new() };

            DateTime today = DateTime.Now.Date;

            var existing = backup.Reports.FirstOrDefault(r => r.Date == today);

            if (existing == null)
            {
                var ramItems = _engine.GetHistory();

                if (ramItems.Count > 0)
                {
                    existing = new DailyReport
                    {
                        Date = today,
                        Items = ramItems.ToList()
                    };

                    backup.Reports.Add(existing);
                }
                else
                {
                    ScheduleLogger.Write("Backup : aucun média aujourd'hui, jour non créé.");
                }
            }
            else
            {
                var ramItems = _engine.GetHistory();

                var newFilteredItems = ramItems.Where(item =>
                    !existing.Items.Any(x =>
                        x.Path == item.Path &&
                        x.FileName == item.FileName &&
                        x.MediaType == item.MediaType &&
                        x.Nom == item.Nom &&
                        x.Saison == item.Saison &&
                        x.Episode == item.Episode
                    )
                ).ToList();

                foreach (var item in newFilteredItems)
                    existing.Items.Add(item);

                ScheduleLogger.Write($"Backup : {newFilteredItems.Count} nouveaux items ajoutés");
            }

            existing.Items = existing.Items
                .GroupBy(i => new { i.Path, i.FileName, i.MediaType, i.Nom, i.Saison, i.Episode })
                .Select(g => g.First())
                .ToList();

            var newItems = _engine.GetHistory();

            var filtered = newItems.Where(item =>
                !existing.Items.Any(x =>
                    x.Path == item.Path &&
                    x.FileName == item.FileName &&
                    x.MediaType == item.MediaType &&
                    x.Nom == item.Nom &&
                    x.Saison == item.Saison &&
                    x.Episode == item.Episode
                )
            ).ToList();

            foreach (var item in filtered)
                existing.Items.Add(item);

            ScheduleLogger.Write($"Backup : {filtered.Count} nouveaux items ajoutés sans doublon");

            DateTime limit = today.AddDays(-retentionDays);
            backup.Reports.RemoveAll(r => r.Date < limit);

            string json = JsonConvert.SerializeObject(backup, Formatting.Indented);
            File.WriteAllText(backupPath, json, Encoding.UTF8);
        }
    }
}