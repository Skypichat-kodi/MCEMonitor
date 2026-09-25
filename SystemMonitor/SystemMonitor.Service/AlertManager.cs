using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemMonitor.Service
{
    public class AlertManager
    {
        private readonly SystemMonitorSettings _settings;
        private readonly AlertHistory _history;

        public AlertManager(SystemMonitorSettings settings, AlertHistory history)
        {
            _settings = settings;
            _history = history;
        }

        /// <summary>
        /// Vérifie si on peut envoyer une alerte (anti-spam basé sur le cooldown CPU).
        /// </summary>
        public bool CanSendAlert(AlertType type, string target)
        {
            var cooldown = TimeSpan.FromMinutes(_settings.CpuCooldownMinutes);
            var limit = DateTime.Now - cooldown;

            return !_history.GetAll()
                .Where(a => a.Type == type && a.Target == target && a.Timestamp >= limit)
                .Any();
        }

        public void Add(Alert alert)
        {
            _history.Add(alert);
            CoreLog.Write($"[ALERT] {alert.Severity} | {alert.Target} | {alert.Message} | Email={alert.EmailSent}");
        }

        public List<Alert> GetAlerts() => _history.GetAll();
    }
}