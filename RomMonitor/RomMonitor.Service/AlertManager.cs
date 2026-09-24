using System;
using System.Collections.Generic;
using System.Linq;

namespace RomMonitor.Service
{
    /// <summary>
    /// Gère les alertes : anti-spam, ajout, récupération.
    /// </summary>
    public class AlertManager
    {
        private readonly RomMonitorSettings _settings;
        private readonly AlertHistory _history;

        public AlertManager(RomMonitorSettings settings, AlertHistory history)
        {
            _settings = settings;
            _history = history;
        }

        /// <summary>
        /// Vérifie si on peut envoyer une alerte (anti-spam).
        /// </summary>
        public bool CanSendAlert(AlertType type, string target)
        {
            var cooldown = TimeSpan.FromHours(_settings.AlertCooldownHours);
            var limit = DateTime.Now - cooldown;

            return !_history.GetAll()
                .Where(a => a.Type == type && a.Target == target && a.Timestamp >= limit)
                .Any();
        }

        /// <summary>
        /// Enregistre une alerte dans l'historique.
        /// </summary>
        public void Add(Alert alert)
        {
            _history.Add(alert);
            CoreLog.Write($"[ALERT] {alert.Severity} | {alert.Target} | {alert.Message} | Email={alert.EmailSent}");
        }

        /// <summary>
        /// Retourne toutes les alertes.
        /// </summary>
        public List<Alert> GetAlerts() => _history.GetAll();
        
                /// <summary>
        /// Vide l'historique des alertes.
        /// </summary>
        public void Clear()
        {
            _history.Clear();
        }
    }
}