using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SystemMonitor.Service
{
    public class AlertHistory
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "SystemMonitor"
        );

        private static readonly string FilePath = Path.Combine(Folder, "alert_history.json");

        private List<Alert> _alerts = new();

        public AlertHistory()
        {
            Load();
        }

        public List<Alert> GetAll() => _alerts;

        public void Add(Alert alert)
        {
            _alerts.Add(alert);
            Save();
        }

        public void Clear()
        {
            try
            {
                _alerts.Clear();
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, "[]");
                CoreLog.Write("Historique des alertes purgé");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Clear AlertHistory : " + ex.Message);
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;

                string json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<List<Alert>>(json);

                if (loaded != null)
                    _alerts = loaded;
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Load AlertHistory : " + ex.Message);
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);

                var limit = DateTime.Now.AddDays(-30);
                _alerts = _alerts.Where(a => a.Timestamp >= limit).ToList();

                string json = JsonSerializer.Serialize(_alerts, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Save AlertHistory : " + ex.Message);
            }
        }
    }
}