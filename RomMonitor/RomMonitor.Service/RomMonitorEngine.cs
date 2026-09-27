using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;
using MCEMonitor.Languages;

namespace RomMonitor.Service
{
    /// <summary>
    /// Orchestrateur principal : boucle de vérification toutes les X minutes.
    /// </summary>
    public class RomMonitorEngine
    {
        private readonly RomMonitorSettings _settings;
        private readonly AlertManager _alertManager;
        private readonly AlertHistory _history;

        private Timer _timer;
        private bool _isRunning;

        // Cache des derniers résultats (pour IPC)
        private List<DiskInfo> _lastDisks = new();
        private List<SmartInfo> _lastSmart = new();

        public IReadOnlyList<DiskInfo> LastDisks => _lastDisks;
        public IReadOnlyList<SmartInfo> LastSmart => _lastSmart;
        public IReadOnlyList<Alert> LastAlerts => _alertManager.GetAlerts();
        public DateTime LastCheckTime { get; private set; } = DateTime.MinValue;

        public event Action OnUpdate;

        public RomMonitorEngine(RomMonitorSettings settings)
        {
            _settings = settings;
            _history = new AlertHistory();

            // Purge de l'historique à chaque démarrage
            _history.Clear();

            _alertManager = new AlertManager(_settings, _history);
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            CoreLog.Write($"RomMonitorEngine démarré (intervalle = {_settings.Interval} min)");

            _timer = new Timer(_settings.Interval * 60 * 1000);
            _timer.Elapsed += (s, e) => Tick();
            _timer.AutoReset = true;
            _timer.Start();

            // Premier check 30 secondes après le démarrage
            Task.Delay(30000).ContinueWith(_ => Tick());
        }

        public void Stop()
        {
            _isRunning = false;
            _timer?.Stop();
            CoreLog.Write("RomMonitorEngine arrêté");
        }

        /// <summary>
        /// Force un Tick immédiat, sans attendre l'intervalle.
        /// Appelé par IPC quand l'utilisateur clique sur "Rafraîchir".
        /// </summary>
        public void ForceTick()
        {
            CoreLog.Write("ForceTick demandé via IPC");
            Task.Run(() => Tick());
        }

        /// <summary>
        /// Vide l'historique des alertes (appelé via IPC).
        /// </summary>
        public void ClearAlerts()
        {
            _alertManager.Clear();
            CoreLog.Write("Historique des alertes vidé par IPC");

            OnUpdate?.Invoke();
        }
                
        // ------------------------------------------------------------------
        //  Tick principal
        // ------------------------------------------------------------------
        private void Tick()
        {
            if (!_isRunning) return;

            try
            {

                // 1. Espace disque
                var disks = DiskSpaceChecker.GetDisks();
                _lastDisks = disks;
                CheckDiskSpace(disks);

                // 2. Santé SMART
                var smartInfo = DiskHealthChecker.GetAllSmartInfo();
                _lastSmart = smartInfo.Values.ToList();
                CheckSmart(smartInfo);

                LastCheckTime = DateTime.Now;

                OnUpdate?.Invoke();
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Tick : " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        //  Espace disque
        // ------------------------------------------------------------------
        private void CheckDiskSpace(List<DiskInfo> disks)
        {
            foreach (var disk in disks)
            {
                bool warn = disk.FreePercent < _settings.DiskSpaceWarnPercent
                            || disk.FreeGo < _settings.DiskSpaceWarnGo;

                bool critical = disk.FreePercent < _settings.DiskSpaceCriticalPercent
                                || disk.FreeGo < _settings.DiskSpaceCriticalGo;

                if (critical)
                {
                    var alert = new Alert
                    {
                        Timestamp = DateTime.Now,
                        Type = AlertType.DiskSpaceCritical,
                        Severity = "Critical",
                        Target = disk.Name,
                        Message = string.Format(
                            LanguageManager.Get("Espace critique : {0:F1}% ({1:F1} Go libre)") 
                                ?? "Espace critique : {0:F1}% ({1:F1} Go libre)",
                            disk.FreePercent, disk.FreeGo),
                        EmailSent = false
                    };

                    if (_alertManager.CanSendAlert(alert.Type, alert.Target))
                    {
                        // ?? Envoi email pour les Critical si activé
                        if (_settings.AlertOnLowDiskSpace)
                        {
                            _ = SendDiskSpaceAlertEmailAsync(disk, "Critical");
                            alert.EmailSent = true;
                        }

                        _alertManager.Add(alert);
                    }
                }
                else if (warn)
                {
                    var alert = new Alert
                    {
                        Timestamp = DateTime.Now,
                        Type = AlertType.DiskSpaceLow,
                        Severity = "Warning",
                        Target = disk.Name,
                        Message = $"Espace faible : {disk.FreePercent:F1}% ({disk.FreeGo:F1} Go libre)",
                        EmailSent = false
                    };

                    if (_alertManager.CanSendAlert(alert.Type, alert.Target))
                    {
                        // Pas d'email pour les Warning (log uniquement)
                        _alertManager.Add(alert);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        //  SMART
        // ------------------------------------------------------------------
        private void CheckSmart(Dictionary<string, SmartInfo> smartInfo)
        {
            foreach (var kv in smartInfo)
            {
                var info = kv.Value;

                if (!info.Available)
                    continue;

                bool isCritical = info.Status == "Critical";
                bool isWarning = info.Status == "Warning";

                if (!isCritical && !isWarning)
                    continue;

                string target = !string.IsNullOrEmpty(info.Serial)
                    ? $"{info.Model} ({info.Serial})"
                    : info.Device;

                string severity = isCritical ? "Critical" : "Warning";

                var alert = new Alert
                {
                    Timestamp = DateTime.Now,
                    Type = AlertType.SmartFailure,
                    Severity = severity,
                    Target = target,
                    Message = $"[{severity}] {info.Model} : {info.StatusReason}",
                    EmailSent = false
                };

                if (_alertManager.CanSendAlert(alert.Type, alert.Target))
                {
                    CoreLog.Write($"[SMART] {target} : {info.Status} - {info.StatusReason}");

                    // Envoi email si critique
                    if (isCritical && _settings.AlertOnSmartFailure)
                    {
                        _ = SendSmartAlertEmailAsync(info);
                        alert.EmailSent = true;
                    }

                    _alertManager.Add(alert);
                }
            }
        }

        // ------------------------------------------------------------------
        //  Envoi email d'alerte SMART
        // ------------------------------------------------------------------
        private async Task SendSmartAlertEmailAsync(SmartInfo info)
        {
            try
            {
                var cfg = EmailConfig.Load();

                if (string.IsNullOrEmpty(cfg.Server))
                {
                    CoreLog.Write("[EMAIL] Config email vide, envoi annulé");
                    return;
                }

                string body = $@"
                    <p><b>Disque :</b> {info.Model}</p>
                    <p><b>Numéro de série :</b> {info.Serial}</p>
                    <p><b>Device :</b> {info.Device}</p>
                    <p><b>Type :</b> {info.Type}</p>
                    <p><b>Statut :</b> <span style='color:#c0392b'>{info.Status}</span></p>
                    <p><b>Raison :</b> {info.StatusReason}</p>
                    {(info.Temperature.HasValue ? $"<p><b>Température :</b> {info.Temperature}°C</p>" : "")}
                    {(info.PowerOnHours.HasValue ? $"<p><b>Heures de fonctionnement :</b> {info.PowerOnHours}</p>" : "")}";

                await EmailSender.SendAsync(
                    cfg,
                    $"[ALERTE] Disque {info.Model} en danger",
                    body,
                    isHtml: true);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email SMART : " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        //  ?? Envoi email d'alerte espace disque
        // ------------------------------------------------------------------
        private async Task SendDiskSpaceAlertEmailAsync(DiskInfo disk, string severity)
        {
            try
            {
                var cfg = EmailConfig.Load();

                if (string.IsNullOrEmpty(cfg.Server))
                {
                    CoreLog.Write("[EMAIL] Config email vide, envoi annulé");
                    return;
                }

                string subject = $"[ALERTE] Disque {disk.Name} en danger";
                string reasonText = "Espace critique (< 5% libre)";

                string body = $@"
                    <p><b>Disque :</b> {disk.Name} {(string.IsNullOrEmpty(disk.Label) ? "" : $"({disk.Label})")}</p>
                    <p><b>Statut :</b> <span style='color:#c0392b'>{severity}</span></p>
                    <p><b>Espace libre :</b> {disk.FreePercent:F1}% ({disk.FreeGo:F1} Go sur {disk.TotalGo:F1} Go)</p>
                    <p><b>Raison :</b> {reasonText}</p>
                    <hr>
                    <p style='color:#888;font-size:12px'>Serveur : {Environment.MachineName}</p>";

                await EmailSender.SendAsync(cfg, subject, body, isHtml: true);

                CoreLog.Write($"[EMAIL] Alerte espace disque envoyée pour {disk.Name}");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email espace : " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        //  Email de test (appelé par IPC)
        // ------------------------------------------------------------------
        public async Task SendTestEmailAsync()
        {
            var cfg = EmailConfig.Load();

            string body = $@"
                <p>Ceci est un email de test envoyé par RomMonitor.</p>
                <p><b>Machine :</b> {Environment.MachineName}</p>
                <p><b>Disques surveillés :</b> {_lastDisks.Count}</p>
                <p><b>Disques avec SMART :</b> {_lastSmart.Count}</p>";

            await EmailSender.SendAsync(cfg, "Test RomMonitor", body, isHtml: true);
        }
        
        /// <summary>
        /// Sévérité la plus grave basée sur l'état ACTUEL des disques
        /// (SMART + espace disque). Reflète l'état RÉEL du système à l'instant T.
        /// 
        /// IMPORTANT : Ne se base PAS sur l'historique des alertes. Ainsi, si un
        /// problème disparaît (espace libéré, disque remplacé, SMART redevenu OK),
        /// l'icône du Tray revient automatiquement à la normale, sans nécessiter
        /// de réinitialisation manuelle.
        ///
        /// Valeurs possibles : "ok" / "warning" / "critical"
        /// </summary>
        public string WorstSeverity
        {
            get
            {
                // Aucun scan effectué pour l'instant ? On considère que tout va bien.
                if (_lastDisks.Count == 0 && _lastSmart.Count == 0)
                    return "ok";

                // ============================================================
                //  Priorité 1 : CRITICAL
                // ============================================================

                // 1a. SMART en état critique (secteurs en attente, FAILED, etc.)
                foreach (var s in _lastSmart)
                {
                    if (s.Available && s.Status == "Critical")
                        return "critical";
                }

                // 1b. Espace disque critique
                foreach (var d in _lastDisks)
                {
                    bool critical = d.FreePercent < _settings.DiskSpaceCriticalPercent
                                 || d.FreeGo < _settings.DiskSpaceCriticalGo;

                    if (critical)
                        return "critical";
                }

                // ============================================================
                //  Priorité 2 : WARNING
                // ============================================================

                // 2a. SMART en état warning (secteurs réalloués, température élevée…)
                foreach (var s in _lastSmart)
                {
                    if (s.Available && s.Status == "Warning")
                        return "warning";
                }

                // 2b. Espace disque faible
                foreach (var d in _lastDisks)
                {
                    bool warn = d.FreePercent < _settings.DiskSpaceWarnPercent
                             || d.FreeGo < _settings.DiskSpaceWarnGo;

                    if (warn)
                        return "warning";
                }

                return "ok";
            }
        }
        
        /// <summary>
        /// Recharge la config à chaud et purge les alertes obsolètes.
        /// </summary>
        public void ReloadConfig()
        {
            try
            {
                _settings.Reload();

                // Purger les alertes : elles ne sont plus valides
                _history.Clear();

                CoreLog.Write("RomMonitorEngine : config rechargée + historique alertes purgé");
                OnUpdate?.Invoke();
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur ReloadConfig RomMonitorEngine : " + ex.Message);
            }
        }                
    }
}