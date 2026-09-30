using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly BsodHistory _bsodHistory;
        private DateTime _lastBsodCheck = DateTime.MinValue;
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _ramHistory = new();

        private Timer _timer;
        private bool _isRunning;
        private SystemSnapshot _lastSnapshot = new();

        public SystemSnapshot LastSnapshot => _lastSnapshot;
        public DateTime LastUpdateTime { get; private set; } = DateTime.MinValue;
        public bool IsRunning => _isRunning;

        public event Action OnUpdate;

        public string WorstSeverity
        {
            get
            {
                try
                {
                    var problems = GetProblems();

                    if (problems.Count == 0)
                        return "ok";

                    if (problems.Any(p => p.Severity == "critical"))
                        return "critical";

                    return "warning";
                }
                catch
                {
                    return "ok";
                }
            }
        }

        public SystemMonitorEngine(SystemMonitorSettings settings)
        {
            _settings = settings;

            // Historique des alertes — on CONSERVE ce qui existe déjà sur disque.
            // Le constructeur d'AlertHistory charge automatiquement le fichier JSON.
            _history = new AlertHistory();
            _alertManager = new AlertManager(settings, _history);

            // Historique des mesures
            _measureHistory = new HistoryBuffer(capacity: 60);

            // Historique des BSOD
            _bsodHistory = new BsodHistory();

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

            Task.Run(() => Tick());
            Task.Run(() => ScanForBsods(force: true));
        }

        public void Stop()
        {
            _isRunning = false;
            _timer?.Stop();
            CoreLog.Write("SystemMonitorEngine arrêté");
        }

        public void ForceTick()
        {
            CoreLog.Write("ForceTick demandé via IPC");
            Task.Run(() => Tick());
        }

        /// <summary>
        /// True tant que les buffers CPU/RAM n'ont pas atteint leur taille cible.
        /// Utilisé pour éviter les fausses alertes au démarrage du service
        /// (phase de chauffe de 60 s après le boot).
        /// </summary>
        private bool IsWarmupPhase
        {
            get
            {
                int windowSize = Math.Max(2, 60 / Math.Max(1, _settings.Interval));
                return _cpuHistory.Count < windowSize || _ramHistory.Count < windowSize;
            }
        }

        private void Tick()
        {
            if (!_isRunning) return;

            try
            {
                var snap = _hardware.GetSnapshot();
                _lastSnapshot = snap;
                LastUpdateTime = DateTime.Now;

                double smoothedCpu = GetSmoothedCpuUsage(snap.Cpu.UsagePercent);
                snap.Cpu.UsagePercent = smoothedCpu;

                double smoothedRam = GetSmoothedRamUsage(snap.Ram.UsagePercent);
                snap.Ram.UsagePercent = smoothedRam;

                var gpu = snap.Gpus.Count > 0 ? snap.Gpus[0] : null;

                _measureHistory.Add(new HistoryPoint
                {
                    Timestamp = snap.Timestamp,
                    CpuUsage = smoothedCpu,
                    RamUsage = smoothedRam,
                    CpuTemp = snap.Cpu.Temperature,
                    GpuUsage = gpu?.UsagePercent,
                    GpuTemp = gpu?.Temperature
                });

                // Ignorer les seuils CPU/RAM tant que la moyenne
                // n'est pas stabilisée (phase de chauffe de 60 s au démarrage)
                if (!IsWarmupPhase)
                {
                    CheckThresholds(snap);
                }
                else
                {
                    CoreLog.Write($"Tick : warmup en cours ({_cpuHistory.Count}/{Math.Max(2, 60 / Math.Max(1, _settings.Interval))} échantillons)");
                }

                ScanForBsods();

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

            // --- Température GPU (NOUVEAU) ---
            if (_settings.AlertOnHighGpuTemp)
            {
                foreach (var gpu in snap.Gpus)
                {
                    if (!gpu.Temperature.HasValue)
                        continue;

                    if (gpu.Temperature.Value < _settings.GpuTempThresholdCelsius)
                        continue;

                    var alert = new Alert
                    {
                        Timestamp = DateTime.Now,
                        Type = AlertType.GpuTempHigh,
                        Severity = "Critical",
                        Target = gpu.Name,
                        Message = $"Température GPU {gpu.Name} : {gpu.Temperature.Value:F1}°C (seuil : {_settings.GpuTempThresholdCelsius}°C)",
                        EmailSent = false
                    };

                    if (_alertManager.CanSendAlert(alert.Type, alert.Target))
                    {
                        _ = SendGpuTempAlertEmailAsync(gpu);
                        alert.EmailSent = true;
                        _alertManager.Add(alert);
                    }
                }
            }
        }

        // ============================================================
        //  Envoi email d'alerte CPU  (avec top processus)
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

                // === Capture des top processus (CPU + RAM) ===
                // Task.Run pour ne pas bloquer le thread du moteur pendant l'échantillonnage.
                var report = await Task.Run(() =>
                    TopProcessesCollector.Collect(topN: 5, sampleMs: 1000));

                string processesHtml =
                    TopProcessesCollector.BuildHtmlBlock(report, forCpu: true) +
                    TopProcessesCollector.BuildHtmlBlock(report, forCpu: false);

                string body = $@"
                    <p><b>Processeur :</b> {cpu.Name}</p>
                    <p><b>Utilisation :</b> <span style='color:#c0392b'>{cpu.UsagePercent:F1} %</span></p>
                    <p><b>Seuil configuré :</b> {_settings.CpuThresholdPercent} %</p>
                    {(cpu.Temperature.HasValue ? $"<p><b>Température :</b> {cpu.Temperature.Value:F1}°C</p>" : "")}
                    {(cpu.FrequencyMHz.HasValue ? $"<p><b>Fréquence :</b> {cpu.FrequencyMHz.Value:F0} MHz</p>" : "")}
                    {processesHtml}";

                await EmailSender.SendAsync(
                    cfg,
                    $"[ALERTE] CPU saturé sur {Environment.MachineName}",
                    body,
                    isHtml: true);

                CoreLog.Write($"[EMAIL] Alerte CPU envoyée ({cpu.UsagePercent:F1}%) avec top processus");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email CPU : " + ex.Message);
            }
        }

        // ============================================================
        //  Envoi email d'alerte RAM  (avec top processus)
        // ============================================================
        private async Task SendRamAlertEmailAsync(RamInfo ram)
        {
            try
            {
                var cfg = EmailConfig.Load();

                if (string.IsNullOrEmpty(cfg.Server))
                    return;

                // === Capture des top processus (RAM + CPU) ===
                var report = await Task.Run(() =>
                    TopProcessesCollector.Collect(topN: 5, sampleMs: 1000));

                string processesHtml =
                    TopProcessesCollector.BuildHtmlBlock(report, forCpu: false) +
                    TopProcessesCollector.BuildHtmlBlock(report, forCpu: true);

                string body = $@"
                    <p><b>Mémoire utilisée :</b> <span style='color:#c0392b'>{ram.UsagePercent:F1} %</span></p>
                    <p><b>Utilisée :</b> {ram.UsedGB:F1} Go</p>
                    <p><b>Libre :</b> {ram.FreeGB:F1} Go</p>
                    <p><b>Totale :</b> {ram.TotalGB:F1} Go</p>
                    <p><b>Seuil configuré :</b> {_settings.RamThresholdPercent} %</p>
                    {processesHtml}";

                await EmailSender.SendAsync(
                    cfg,
                    $"[ALERTE] RAM saturée sur {Environment.MachineName}",
                    body,
                    isHtml: true);

                CoreLog.Write($"[EMAIL] Alerte RAM envoyée ({ram.UsagePercent:F1}%) avec top processus");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email RAM : " + ex.Message);
            }
        }

        // ============================================================
        //  Envoi email d'alerte Température CPU
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

        // ============================================================
        //  Envoi email d'alerte Température GPU (NOUVEAU)
        // ============================================================
        private async Task SendGpuTempAlertEmailAsync(GpuInfo gpu)
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
                    <p><b>GPU :</b> {gpu.Name}</p>
                    <p><b>Température :</b> <span style='color:#c0392b'>{gpu.Temperature:F1} °C</span></p>
                    <p><b>Seuil configuré :</b> {_settings.GpuTempThresholdCelsius} °C</p>
                    <p><b>Utilisation GPU :</b> {gpu.UsagePercent:F1} %</p>
                    {(gpu.VramUsedMB.HasValue && gpu.VramTotalMB.HasValue
                        ? $"<p><b>VRAM :</b> {gpu.VramUsedMB.Value:F0} / {gpu.VramTotalMB.Value:F0} Mo</p>"
                        : "")}";

                await EmailSender.SendAsync(
                    cfg,
                    $"[ALERTE] Température GPU élevée sur {Environment.MachineName}",
                    body,
                    isHtml: true);

                CoreLog.Write($"[EMAIL] Alerte temp GPU envoyée ({gpu.Name} : {gpu.Temperature.Value:F1}°C)");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email temp GPU : " + ex.Message);
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

        public void ScanForBsods(bool force = false)
        {
            if (!force && (DateTime.Now - _lastBsodCheck).TotalMinutes < 5)
                return;

            _lastBsodCheck = DateTime.Now;

            try
            {
                var bsods = BsodReader.GetRecentBsods(30);

                foreach (var b in bsods)
                    _bsodHistory.Add(b);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur ScanForBsods : " + ex.Message);
            }
        }

        public List<BsodInfo> GetBsods() => _bsodHistory.GetAll();

        public void ClearBsods()
        {
            _bsodHistory.Clear();
            CoreLog.Write("Historique BSOD vidé");
        }

        /// <summary>
        /// Retourne la liste des problèmes ACTIFS (seuils actuellement dépassés).
        /// </summary>
        public List<ProblemItem> GetProblems(int maxItems = 20)
        {
            var problems = new List<ProblemItem>();

            try
            {
                var snap = _lastSnapshot;

                if (snap == null)
                    return problems;

                // --- CPU actif ? ---
                if (_settings.AlertOnHighCpu &&
                    snap.Cpu.UsagePercent >= _settings.CpuThresholdPercent)
                {
                    problems.Add(new ProblemItem
                    {
                        Timestamp = DateTime.Now,
                        Severity = "critical",
                        Category = "CpuHigh",
                        Message = $"Utilisation CPU : {snap.Cpu.UsagePercent:F1}% (seuil : {_settings.CpuThresholdPercent}%)"
                    });
                }

                // --- RAM active ? ---
                if (_settings.AlertOnHighRam &&
                    snap.Ram.UsagePercent >= _settings.RamThresholdPercent)
                {
                    problems.Add(new ProblemItem
                    {
                        Timestamp = DateTime.Now,
                        Severity = "critical",
                        Category = "RamHigh",
                        Message = $"Utilisation RAM : {snap.Ram.UsagePercent:F1}% ({snap.Ram.UsedGB:F1} / {snap.Ram.TotalGB:F1} Go, seuil : {_settings.RamThresholdPercent}%)"
                    });
                }

                // --- Température CPU active ? ---
                if (_settings.AlertOnHighTemp &&
                    snap.Cpu.Temperature.HasValue &&
                    snap.Cpu.Temperature.Value >= _settings.TempThresholdCelsius)
                {
                    problems.Add(new ProblemItem
                    {
                        Timestamp = DateTime.Now,
                        Severity = "critical",
                        Category = "TempHigh",
                        Message = $"Température CPU : {snap.Cpu.Temperature.Value:F1}°C (seuil : {_settings.TempThresholdCelsius}°C)"
                    });
                }

                // --- Température GPU active ? (NOUVEAU) ---
                if (_settings.AlertOnHighGpuTemp)
                {
                    foreach (var gpu in snap.Gpus)
                    {
                        if (gpu.Temperature.HasValue &&
                            gpu.Temperature.Value >= _settings.GpuTempThresholdCelsius)
                        {
                            problems.Add(new ProblemItem
                            {
                                Timestamp = DateTime.Now,
                                Severity = "critical",
                                Category = "GpuTempHigh",
                                Message = $"Température GPU {gpu.Name} : {gpu.Temperature.Value:F1}°C (seuil : {_settings.GpuTempThresholdCelsius}°C)"
                            });
                        }
                    }
                }

                // --- BSOD récents (< 24h) ---
                var bsods = _bsodHistory.GetAll();
                var bsodLimit = DateTime.Now.AddHours(-24);

                if (bsods != null)
                {
                    foreach (var b in bsods.Where(x => x.Timestamp >= bsodLimit))
                    {
                        problems.Add(new ProblemItem
                        {
                            Timestamp = b.Timestamp,
                            Severity = "critical",
                            Category = "BSOD",
                            Message = $"{b.BugCheckCode} - {b.BugCheckName}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur GetProblems : " + ex.Message);
            }

            return problems
                .OrderByDescending(p => p.Severity == "critical")
                .ThenByDescending(p => p.Timestamp)
                .Take(maxItems)
                .ToList();
        }

        /// <summary>
        /// Recharge la config à chaud SANS toucher à l'historique des alertes.
        /// (L'historique est désormais découplé de la config : le modifier
        ///  ne doit pas effacer les alertes passées.)
        /// </summary>
        public void ReloadConfig()
        {
            try
            {
                _settings.Reload();

                CoreLog.Write("Engine : config rechargée (historique alertes conservé)");
                OnUpdate?.Invoke();
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur ReloadConfig : " + ex.Message);
            }
        }

        private double GetSmoothedCpuUsage(double currentUsage)
        {
            // Nombre d'échantillons à garder (60s / intervalle)
            int windowSize = Math.Max(2, 60 / Math.Max(1, _settings.Interval));

            _cpuHistory.Enqueue(currentUsage);

            while (_cpuHistory.Count > windowSize)
                _cpuHistory.Dequeue();

            return _cpuHistory.Average();
        }

        private double GetSmoothedRamUsage(double currentUsage)
        {
            // Nombre d'échantillons à garder (60s / intervalle)
            int windowSize = Math.Max(2, 60 / Math.Max(1, _settings.Interval));

            _ramHistory.Enqueue(currentUsage);

            while (_ramHistory.Count > windowSize)
                _ramHistory.Dequeue();

            return _ramHistory.Average();
        }
    }
}