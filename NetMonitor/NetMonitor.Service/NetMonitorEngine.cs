using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetMonitor.Service.Detectors;
using NetMonitor.Service.Models;
using NetMonitor.Service.Services;

namespace NetMonitor.Service
{
    public class NetMonitorEngine
    {
        private readonly NetMonitorSettings _settings;
        private readonly List<IConnectionAwareDetector> _connDetectors;
        private readonly DetectorContext _context;
        private readonly EtwConnectionWatcher _etw;

        private readonly ConcurrentQueue<ConnectionInfo> _recentConnections = new();
        private const int MaxRecentConnections = 1000;

        // Tableau 3 : historique (toutes les alertes)
        private readonly List<IntrusionEvent> _alerts = new();

        // Tableau 1 : intrusions actives (non bloquées)
        private readonly List<IntrusionEvent> _activeIntrusions = new();

        // Tableau 2 : IP bloquées
        private readonly List<BlockedIpInfo> _blockedIps = new();

        private readonly object _alertsLock = new();

        private System.Threading.Timer? _evictTimer;

        private static readonly string BlockedIpsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "blocked_ips.json");

        public bool IsRunning => _etw.IsRunning;
        public DateTime LastUpdateTime { get; private set; } = DateTime.MinValue;

        // ------------------------------------------------------------
        //  Sévérité (basée sur les intrusions ACTIVES uniquement)
        // ------------------------------------------------------------
        public string WorstSeverity
        {
            get
            {
                lock (_alertsLock)
                {
                    if (_activeIntrusions.Count == 0) return "ok";
                    if (_activeIntrusions.Any(a => a.Severity == Severity.Critical)) return "critical";
                    return "warning";
                }
            }
        }

        // ------------------------------------------------------------
        //  Problèmes actifs (pour le Tray : tableau 1 seulement)
        // ------------------------------------------------------------
        public List<ProblemItem> GetProblems(int maxItems = 20)
        {
            var problems = new List<ProblemItem>();

            lock (_alertsLock)
            {
                foreach (var a in _activeIntrusions)
                {
                    problems.Add(new ProblemItem
                    {
                        Timestamp = a.Timestamp,
                        Severity = a.Severity == Severity.Critical || a.Severity == Severity.High
                            ? "critical"
                            : "warning",
                        Category = a.Source,
                        Message = a.Message
                    });
                }
            }

            return problems
                .OrderByDescending(p => p.Severity == "critical")
                .ThenByDescending(p => p.Timestamp)
                .Take(maxItems)
                .ToList();
        }

        // ------------------------------------------------------------
        //  Constructeur
        // ------------------------------------------------------------
        public NetMonitorEngine(NetMonitorSettings settings)
        {
            _settings = settings;
            _context = new DetectorContext(settings);
            _etw = new EtwConnectionWatcher();
            _etw.ConnectionOpened += OnConnectionOpened;

            _connDetectors = new List<IConnectionAwareDetector>
            {
                new PortScanDetector(),
                new BeaconingDetector(),
            };
        }

        public void Start()
        {
            _etw.Start();
            _evictTimer = new System.Threading.Timer(
                _ => _context.EvictStale(TimeSpan.FromMinutes(30)),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));

            CoreLog.Write("NetMonitorEngine démarré (surveillance ETW).");
        }

        public void Stop()
        {
            _evictTimer?.Dispose();
            _evictTimer = null;
            _etw.Stop();
            CoreLog.Write("NetMonitorEngine arrêté.");
        }

        // ------------------------------------------------------------
        //  Connexions récentes
        // ------------------------------------------------------------
        public IReadOnlyList<ConnectionInfo> GetRecentConnections(int max = 300)
            => _recentConnections.Reverse().Take(max).ToList();

        // ------------------------------------------------------------
        //  Tableau 3 : historique
        // ------------------------------------------------------------
        public List<IntrusionEvent> GetAlerts()
        {
            lock (_alertsLock) return _alerts.ToList();
        }

        public void ClearAlerts()
        {
            lock (_alertsLock) _alerts.Clear();
            CoreLog.Info("Historique des intrusions vidé");
        }

        // ------------------------------------------------------------
        //  Tableau 1 : intrusions actives
        // ------------------------------------------------------------
        public List<IntrusionEvent> GetActiveIntrusions()
        {
            lock (_alertsLock) return _activeIntrusions.ToList();
        }

        public void ClearActiveIntrusions()
        {
            lock (_alertsLock) _activeIntrusions.Clear();
            CoreLog.Info("Intrusions actives vidées");
        }

        // ------------------------------------------------------------
        //  Tableau 2 : IP bloquées
        // ------------------------------------------------------------
        public List<BlockedIpInfo> GetBlockedIps()
        {
            lock (_alertsLock) return _blockedIps.ToList();
        }

        public bool BlockIp(string ip, out string errorMessage, string? source = null, string? message = null, string? details = null)
        {
            if (!FirewallHelper.BlockIp(ip, out errorMessage)) return false;

            lock (_alertsLock)
            {
                if (!_blockedIps.Any(b => b.Ip == ip))
                {
                    _blockedIps.Add(new BlockedIpInfo
                    {
                        Ip = ip,
                        BlockedAt = DateTime.UtcNow,
                        Source = source ?? "",
                        Message = message ?? "",
                        Details = details
                    });
                }
                _activeIntrusions.RemoveAll(a => a.RemoteIp == ip);
            }

            _context.ForgetIp(ip);
            SaveBlockedIps();
            CoreLog.Info($"IP {ip} bloquée et retirée des intrusions actives");
            return true;
        }

        // Surcharge pour compatibilité
        public bool BlockIp(string ip, string? source = null, string? message = null, string? details = null)
            => BlockIp(ip, out _, source, message, details);

        public bool UnblockIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;

            bool fw = FirewallHelper.UnblockIp(ip);

            lock (_alertsLock) _blockedIps.RemoveAll(b => b.Ip == ip);
            SaveBlockedIps();

            CoreLog.Info($"IP {ip} débloquée");
            return fw;
        }

        // ------------------------------------------------------------
        //  Persistance des IP bloquées
        // ------------------------------------------------------------
        public void LoadBlockedIps()
        {
            try
            {
                if (!File.Exists(BlockedIpsFile)) return;

                var json = File.ReadAllText(BlockedIpsFile);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<BlockedIpInfo>>(json);

                if (list == null || list.Count == 0) return;

                lock (_alertsLock)
                {
                    _blockedIps.Clear();
                    _blockedIps.AddRange(list);
                }

                CoreLog.Info($"Chargement {list.Count} IP bloquées depuis le fichier");

                // Synchro : recrée les règles pare-feu manquantes
                foreach (var b in list)
                {
                    if (!FirewallHelper.IsAlreadyBlocked(b.Ip))
                    {
                        CoreLog.Info($"Sync : règle pare-feu manquante pour {b.Ip}, recréation...");
                        FirewallHelper.BlockIp(b.Ip);
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"LoadBlockedIps : {ex.Message}");
            }
        }

        private void SaveBlockedIps()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BlockedIpsFile)!);

                List<BlockedIpInfo> snapshot;
                lock (_alertsLock) snapshot = _blockedIps.ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(snapshot,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(BlockedIpsFile, json);
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"SaveBlockedIps : {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        //  Rechargement config / force scan
        // ------------------------------------------------------------
        public void ReloadConfig()
        {
            _settings.Reload();
        }

        public void ForceScan()
        {
            CoreLog.Write("ForceScan demandé (no-op en mode ETW).");
        }

        // ------------------------------------------------------------
        //  Traitement des connexions ETW
        // ------------------------------------------------------------
        private void OnConnectionOpened(object? sender, ConnectionInfo ci)
        {
            _recentConnections.Enqueue(ci);
            while (_recentConnections.Count > MaxRecentConnections)
                _recentConnections.TryDequeue(out _);

            LastUpdateTime = DateTime.Now;

            ci.ProcessName = ProcessResolver.Resolve(ci.Pid);

            foreach (var det in _connDetectors)
            {
                DetectorResult? result = null;
                try { result = det.InspectConnection(ci, _context); }
                catch (Exception ex) { CoreLog.Write($"Détecteur {det.Name} : {ex.Message}"); }

                if (result is { IsThreat: true })
                    RaiseIntrusion(det.Name, result, ci);
            }
        }

        private void RaiseIntrusion(string source, DetectorResult r, ConnectionInfo ci)
        {
            var evt = new IntrusionEvent
            {
                Timestamp = ci.Timestamp,
                Source = source,
                Severity = r.Severity,
                Message = r.Message,
                RemoteIp = ci.RemoteIp,
                RemotePort = ci.RemotePort,
                LocalPort = ci.LocalPort,
                Pid = ci.Pid,
                ProcessName = ci.ProcessName,
                Details = r.Details
            };

            lock (_alertsLock)
            {
                // Tableau 3 : historique
                _alerts.Add(evt);
                if (_alerts.Count > 500) _alerts.RemoveAt(0);

                // Tableau 1 : intrusions actives
                _activeIntrusions.Add(evt);
                if (_activeIntrusions.Count > 200) _activeIntrusions.RemoveAt(0);
            }

            CoreLog.Write($"[ALERT] {evt.Severity} | {evt.Source} | {evt.Message}");
        }
    }

    public class ProblemItem
    {
        public DateTime Timestamp { get; set; }
        public string Severity { get; set; } = "";
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
    }
}