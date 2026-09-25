using System;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Orchestrateur principal : boucle de rafraîchissement toutes les X secondes.
    /// </summary>
    public class SystemMonitorEngine
    {
        private readonly SystemMonitorSettings _settings;
        private readonly AlertManager _alertManager;
        private readonly AlertHistory _history;
        private readonly HardwareMonitorService _hardware;
        private readonly HistoryBuffer _measureHistory;    
        
        private Timer _timer;
        private bool _isRunning;
        private SystemSnapshot _lastSnapshot = new();

        public SystemSnapshot LastSnapshot => _lastSnapshot;
        public DateTime LastUpdateTime { get; private set; } = DateTime.MinValue;
        public bool IsRunning => _isRunning;

        public event Action OnUpdate;
        
        public SystemMonitorEngine(SystemMonitorSettings settings)
        {
            _settings = settings;
            
            // Historique des alertes (existant)
            _history = new AlertHistory();
            _history.Clear();
            _alertManager = new AlertManager(settings, _history);

            // ? NOUVEAU : historique des mesures
            _measureHistory = new HistoryBuffer(capacity: 60);

            // Collecte matérielle
            _hardware = new HardwareMonitorService();
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            CoreLog.Write($"SystemMonitorEngine démarré (intervalle = {_settings.Interval} s)");

            _timer = new Timer(_settings.Interval * 1000);
            _timer.Elapsed += (s, e) => Tick();
            _timer.AutoReset = true;
            _timer.Start();

            // Premier check immédiat
            Task.Run(() => Tick());
        }

        public void Stop()
        {
            _isRunning = false;
            _timer?.Stop();
            CoreLog.Write("SystemMonitorEngine arrêté");
        }

        /// <summary>
        /// Force un Tick immédiat (appelé par IPC).
        /// </summary>
        public void ForceTick()
        {
            CoreLog.Write("ForceTick demandé via IPC");
            Task.Run(() => Tick());
        }

        private void Tick()
        {
            if (!_isRunning) return;

            try
            {
                var snap = _hardware.GetSnapshot();
                _lastSnapshot = snap;
                LastUpdateTime = DateTime.Now;

                // ? NOUVEAU : ajouter à l'historique
                var gpu = snap.Gpus.Count > 0 ? snap.Gpus[0] : null;

                _measureHistory.Add(new HistoryPoint
                {
                    Timestamp = snap.Timestamp,
                    CpuUsage = snap.Cpu.UsagePercent,
                    RamUsage = snap.Ram.UsagePercent,
                    CpuTemp = snap.Cpu.Temperature,
                    GpuUsage = gpu?.UsagePercent,
                    GpuTemp = gpu?.Temperature
                });

                // Vérification des seuils
                CheckThresholds(snap);

                OnUpdate?.Invoke();
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Tick : " + ex.Message);
            }
        }

        public void Dispose()
        {
            Stop();
            _hardware?.Dispose();
        }
        
        // ============================================================
        //  Vérification des seuils
        // ============================================================
        private void CheckThresholds(SystemSnapshot snap)
        {
            // --- CPU ---
            if (_settings.AlertOnHighCpu &&
                snap.Cpu.UsagePercent >= _settings.CpuThresholdPercent)
            {
                var alert = new Alert
                {
                    Timestamp = DateTime.Now,
                    Type = AlertType.CpuHigh,
                    Severity = "Critical",
                    Target = "CPU",
                    Message = $"Utilisation CPU : {snap.Cpu.UsagePercent:F1}% (seuil : {_settings.CpuThresholdPercent}%)",
                    EmailSent = false
                };

                if (_alertManager.CanSendAlert(alert.Type, alert.Target))
                {
                    _ = SendCpuAlertEmailAsync(snap.Cpu);
                    alert.EmailSent = true;
                    _alertManager.Add(alert);
                }
            }

            // --- RAM ---
            if (_settings.AlertOnHighRam &&
                snap.Ram.UsagePercent >= _settings.RamThresholdPercent)
            {
                var alert = new Alert
                {
                    Timestamp = DateTime.Now,
                    Type = AlertType.RamHigh,
                    Severity = "Critical",
                    Target = "RAM",
                    Message = $"Utilisation RAM : {snap.Ram.UsagePercent:F1}% ({snap.Ram.UsedGB:F1} / {snap.Ram.TotalGB:F1} Go, seuil : {_settings.RamThresholdPercent}%)",
                    EmailSent = false
                };

                if (_alertManager.CanSendAlert(alert.Type, alert.Target))
                {
                    _ = SendRamAlertEmailAsync(snap.Ram);
                    alert.EmailSent = true;
                    _alertManager.Add(alert);
                }
            }

            // --- Température CPU ---
            if (_settings.AlertOnHighTemp &&
                snap.Cpu.Temperature.HasValue &&
                snap.Cpu.Temperature.Value >= _settings.TempThresholdCelsius)
            {
                var alert = new Alert
                {
                    Timestamp = DateTime.Now,
                    Type = AlertType.TempHigh,
                    Severity = "Critical",
                    Target = "CPU Temp",
                    Message = $"Température CPU : {snap.Cpu.Temperature.Value:F1}°C (seuil : {_settings.TempThresholdCelsius}°C)",
                    EmailSent = false
                };

                if (_alertManager.CanSendAlert(alert.Type, alert.Target))
                {
                    _ = SendTempAlertEmailAsync(snap.Cpu);
                    alert.EmailSent = true;
                    _alertManager.Add(alert);
                }
            }
        }

        // ============================================================
        //  Envoi email d'alerte CPU
        // ============================================================
        private async Task SendCpuAlertEmailAsync(CpuInfo cpu)
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
                    <p><b>Processeur :</b> {cpu.Name}</p>
                    <p><b>Utilisation :</b> <span style='color:#c0392b'>{cpu.UsagePercent:F1} %</span></p>
                    <p><b>Seuil configuré :</b> {_settings.CpuThresholdPercent} %</p>
                    {(cpu.Temperature.HasValue ? $"<p><b>Température :</b> {cpu.Temperature.Value:F1}°C</p>" : "")}
                    {(cpu.FrequencyMHz.HasValue ? $"<p><b>Fréquence :</b> {cpu.FrequencyMHz.Value:F0} MHz</p>" : "")}";

                await EmailSender.SendAsync(
                    cfg,
                    $"[ALERTE] CPU saturé sur {Environment.MachineName}",
                    body,
                    isHtml: true);

                CoreLog.Write($"[EMAIL] Alerte CPU envoyée ({cpu.UsagePercent:F1}%)");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email CPU : " + ex.Message);
            }
        }

        // ============================================================
        //  Envoi email d'alerte RAM
        // ============================================================
        private async Task SendRamAlertEmailAsync(RamInfo ram)
        {
            try
            {
                var cfg = EmailConfig.Load();

                if (string.IsNullOrEmpty(cfg.Server))
                    return;

                string body = $@"
                    <p><b>Mémoire utilisée :</b> <span style='color:#c0392b'>{ram.UsagePercent:F1} %</span></p>
                    <p><b>Utilisée :</b> {ram.UsedGB:F1} Go</p>
                    <p><b>Libre :</b> {ram.FreeGB:F1} Go</p>
                    <p><b>Totale :</b> {ram.TotalGB:F1} Go</p>
                    <p><b>Seuil configuré :</b> {_settings.RamThresholdPercent} %</p>";

                await EmailSender.SendAsync(
                    cfg,
                    $"[ALERTE] RAM saturée sur {Environment.MachineName}",
                    body,
                    isHtml: true);

                CoreLog.Write($"[EMAIL] Alerte RAM envoyée ({ram.UsagePercent:F1}%)");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email RAM : " + ex.Message);
            }
        }

        // ============================================================
        //  Envoi email d'alerte Température
        // ============================================================
        private async Task SendTempAlertEmailAsync(CpuInfo cpu)
        {
            try
            {
                var cfg = EmailConfig.Load();

                if (string.IsNullOrEmpty(cfg.Server))
                    return;

                string body = $@"
                    <p><b>Processeur :</b> {cpu.Name}</p>
                    <p><b>Température :</b> <span style='color:#c0392b'>{cpu.Temperature:F1} °C</span></p>
                    <p><b>Seuil configuré :</b> {_settings.TempThresholdCelsius} °C</p>
                    <p><b>Utilisation CPU :</b> {cpu.UsagePercent:F1} %</p>";

                await EmailSender.SendAsync(
                    cfg,
                    $"[ALERTE] Température CPU élevée sur {Environment.MachineName}",
                    body,
                    isHtml: true);

                CoreLog.Write($"[EMAIL] Alerte température envoyée ({cpu.Temperature:F1}°C)");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email temp : " + ex.Message);
            }
        }
        
        public List<Alert> GetAlerts() => _alertManager.GetAlerts();

        public void ClearAlerts()
        {
            _history.Clear();
            CoreLog.Write("Historique des alertes vidé par IPC");
            OnUpdate?.Invoke();
        }
        
        public List<HistoryPoint> GetHistory() => _measureHistory.GetAll();

        public void ClearHistory()
        {
            _measureHistory.Clear();
            CoreLog.Write("Historique des mesures vidé");
        }                             
    }
}