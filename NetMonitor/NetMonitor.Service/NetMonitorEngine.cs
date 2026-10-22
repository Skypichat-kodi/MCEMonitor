using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using NetMonitor.Service.Capture;
using NetMonitor.Service.Detectors;
using NetMonitor.Service.Models;
using NetMonitor.Service.Services;

namespace NetMonitor.Service
{
    public class NetMonitorEngine
    {
        private readonly NetMonitorSettings _settings;
        private readonly List<IConnectionAwareDetector> _connDetectors;
        private readonly List<IDetector> _pktDetectors;
        private readonly DetectorContext _context;
        private readonly EtwConnectionWatcher _etw;
        
        private ThreatIntelService? _threatIntel;
        private TrustedScannerService? _trustedScanners;
        private GeoIpService? _geoIp;

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
        private System.Threading.Timer? _autoBlockCleanupTimer;

        // IPs auto-bloquées à retirer des actives après expiration
        private readonly ConcurrentDictionary<string, DateTime> _autoBlockedTempIps = new();

        private const int AutoBlockVisibilitySeconds = 60;   // ? Ajustable
        
        private PacketCaptureService? _capture;
        private Channel<RawPacket>? _packetChannel;
        private Task? _consumerTask;

        private static readonly string BlockedIpsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "blocked_ips.json");

        private static readonly string HistoryFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "alert_history.json"); 
                    
        // Journal d'audit pour les alertes ignorées (whitelist)
        private readonly List<IntrusionEvent> _auditHistory = new();

        private static readonly string AuditFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "audit_history.json");
            
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
            _threatIntel = new ThreatIntelService();
            _trustedScanners = new TrustedScannerService(settings.TrustedScannerPatterns);
            _geoIp = new GeoIpService(settings.GeoIpProvider, settings.GeoIpEnabled, settings.ReverseDnsEnabled);

            _etw = new EtwConnectionWatcher();
            _etw.ConnectionOpened += OnConnectionOpened;

            _connDetectors = new List<IConnectionAwareDetector>
            {
                new PortScanDetector(),
                new BeaconingDetector(),
                new BlacklistIpDetector(_threatIntel),      // NOUVEAU
                new SuspiciousOutboundDetector(),           // NOUVEAU
            };

            // Liste des détecteurs basés sur les paquets bruts (WinDivert)
            _pktDetectors = new List<IDetector>
            {
                new SynFloodDetector(),
                new IcmpFloodDetector(),
                new DnsSpoofDetector(),
                new BlacklistIpDetector(_threatIntel),      // NOUVEAU (écoute aussi les paquets bruts)
            };
        }

        public void Start()
        {
            _threatIntel?.Start(); // NOUVEAU : démarre le service (charge la blacklist)

            _etw.Start();
            _evictTimer = new System.Threading.Timer(
                _ => _context.EvictStale(TimeSpan.FromMinutes(30)),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));

            // --- Initialisation de la capture WinDivert ---
            _packetChannel = Channel.CreateBounded<RawPacket>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
            _capture = new PacketCaptureService("ip", _packetChannel.Writer);
            _capture.OnError += (s, ex) => CoreLog.Error($"Erreur capture WinDivert : {ex.Message}");
            _capture.Start();

            _consumerTask = Task.Run(() => ConsumePacketsAsync(_packetChannel.Reader));
            
            // Timer de nettoyage des IPs auto-bloquées (toutes les 10 s)
            _autoBlockCleanupTimer = new System.Threading.Timer(
                _ => CleanupAutoBlockedIps(),
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(10));            

            CoreLog.Write("NetMonitorEngine démarré (surveillance ETW + WinDivert).");
        }

        private void CleanupAutoBlockedIps()
        {
            var now = DateTime.UtcNow;

            var expired = _autoBlockedTempIps
                .Where(kv => kv.Value <= now)
                .Select(kv => kv.Key)
                .ToList();

            if (expired.Count == 0) return;

            lock (_alertsLock)
            {
                foreach (var ip in expired)
                {
                    int removed = _activeIntrusions.RemoveAll(a => a.RemoteIp == ip);
                    _autoBlockedTempIps.TryRemove(ip, out _);

                    if (removed > 0)
                        CoreLog.Debug($"[AUTO-BLOCK] IP {ip} retirée des actives après {AutoBlockVisibilitySeconds}s ({removed} entrée(s))");
                }
            }
        }

        public void Stop()
        {
            _threatIntel?.Stop(); // NOUVEAU : arrête le watcher de fichier

            _capture?.Stop();
            _packetChannel?.Writer.TryComplete();
            try { _consumerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _capture?.Dispose();

            _evictTimer?.Dispose();
            _evictTimer?.Dispose();
            _evictTimer = null;

            _autoBlockCleanupTimer?.Dispose();
            _autoBlockCleanupTimer = null;            
            _evictTimer = null;
            
            _etw.Stop();
            CoreLog.Write("NetMonitorEngine arrêté.");
        }

        // ------------------------------------------------------------
        //  Consommation des paquets bruts (WinDivert)
        // ------------------------------------------------------------
        private async Task ConsumePacketsAsync(ChannelReader<RawPacket> reader)
        {
            await foreach (var raw in reader.ReadAllAsync())
            {
                var evt = PacketParser.Parse(raw);
                if (evt == null) continue;

                // Log de debug (seulement si Verbose = true)
                if (CoreLog.Verbose)
                {
                    CoreLog.Debug($"[PKT] {evt.Protocol} {evt.SourceIp}:{evt.SourcePort} -> {evt.DestIp}:{evt.DestPort} (len={evt.Length})");
                }

                // Appel des détecteurs de paquets
                foreach (var det in _pktDetectors)
                {
                    DetectorResult? result = null;
                    try { result = det.Inspect(evt, _context); }
                    catch (Exception ex) { CoreLog.Write($"Détecteur Paquet {det.Name} : {ex.Message}"); }

                    if (result is { IsThreat: true })
                        RaiseIntrusionPacket(det.Name, result, evt);
                }
            }
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
            SaveHistory();
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

        public bool BlockIp(string ip, out string errorMessage,
                            string? source = null, string? message = null,
                            string? details = null, string? severity = null)
        {
            errorMessage = "";

            if (string.IsNullOrWhiteSpace(ip))
            {
                errorMessage = "IP vide.";
                return false;
            }

            if (FirewallHelper.IsCriticalIp(ip))
            {
                errorMessage = $"L'adresse {ip} fait partie d'une plage critique et ne peut pas être bloquée.";
                CoreLog.Warn($"Blocage refusé : {ip} (IP critique)");
                return false;
            }

            bool alreadyBlocked = false;

            lock (_alertsLock)
            {
                alreadyBlocked = _blockedIps.Any(b => b.Ip == ip);

                if (!alreadyBlocked)
                {
                    _blockedIps.Add(new BlockedIpInfo
                    {
                        Ip = ip,
                        BlockedAt = DateTime.UtcNow,
                        Source = source ?? "",
                        Message = message ?? "",
                        Details = details,
                        Severity = severity ?? "-"
                    });
                }
                _activeIntrusions.RemoveAll(a => a.RemoteIp == ip);
            }

            // Si déjà bloquée : rien de plus à faire
            if (alreadyBlocked)
                return true;

            // Resynchronisation complète des règles (recalcul de tous les blocs)
            var allIps = GetBlockedIps().Select(b => b.Ip).ToList();
            bool syncOk = FirewallHelper.SyncFirewallRules(allIps);

            if (!syncOk)
            {
                errorMessage = "Erreur lors de la synchronisation des règles pare-feu (netsh).";
                return false;
            }

            _context.ForgetIp(ip);
            SaveBlockedIps();
            CoreLog.Info($"IP {ip} bloquée et retirée des intrusions actives");
            return true;
        }
        
        public bool BlockIp(string ip, string? source, string? message, string? details)
            => BlockIp(ip, out _, source, message, details, null);

        public bool UnblockIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;

            // ? NOUVEAU : arrête le maintien en actives (si auto-bloquée récemment)
            _autoBlockedTempIps.TryRemove(ip, out _);

            bool removed = false;
            lock (_alertsLock)
            {
                removed = _blockedIps.RemoveAll(b => b.Ip == ip) > 0;
            }

            if (!removed) return false;

            var allIps = GetBlockedIps().Select(b => b.Ip).ToList();
            bool syncOk = FirewallHelper.SyncFirewallRules(allIps);

            SaveBlockedIps();
            CoreLog.Info($"IP {ip} débloquée (sync pare-feu: {(syncOk ? "OK" : "ÉCHEC")})");
            return syncOk;
        }

        // ------------------------------------------------------------
        //  Persistance de l'historique (Problème 1)
        // ------------------------------------------------------------
        public void LoadHistory()
        {
            try
            {
                if (!File.Exists(HistoryFile)) return;
                var json = File.ReadAllText(HistoryFile);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<IntrusionEvent>>(json);
                if (list == null) return;

                lock (_alertsLock)
                {
                    _alerts.Clear();
                    _alerts.AddRange(list);
                    // On restaure aussi les intrusions actives (moins de 24h) pour l'UI
                    _activeIntrusions.Clear();
                    _activeIntrusions.AddRange(list.Where(a => (DateTime.UtcNow - a.Timestamp).TotalHours < 24));

                    // ? NOUVEAU : retire des actives les IPs déjà bloquées
                    // (évite le doublon visuel après un redémarrage rapide)
                    var blockedIps = new HashSet<string>(
                        _blockedIps.Select(b => b.Ip),
                        StringComparer.OrdinalIgnoreCase);

                    if (blockedIps.Count > 0)
                    {
                        int removed = _activeIntrusions.RemoveAll(a =>
                            !string.IsNullOrWhiteSpace(a.RemoteIp) && blockedIps.Contains(a.RemoteIp));

                        if (removed > 0)
                            CoreLog.Info($"LoadHistory : {removed} alerte(s) retirée(s) des actives (IP déjà bloquée)");
                    }
                }
                CoreLog.Info($"Historique restauré : {_alerts.Count} alertes, {_activeIntrusions.Count} actives.");

                // Pré-charger les scanners légitimes connus (popule le cache en arrière-plan)
                if (_trustedScanners != null)
                {
                    var distinctIps = _alerts
                        .Where(a => !string.IsNullOrWhiteSpace(a.RemoteIp))
                        .Select(a => a.RemoteIp!)
                        .Distinct()
                        .Take(200)
                        .ToList();

                    foreach (var ip in distinctIps)
                        _trustedScanners.CheckInBackground(ip);
                }
                
                if (_geoIp != null)
                {
                    var distinctIps = _alerts
                        .Where(a => !string.IsNullOrWhiteSpace(a.RemoteIp))
                        .Select(a => a.RemoteIp!)
                        .Distinct()
                        .ToList();

                    foreach (var ip in distinctIps)
                        _geoIp.ResolveInBackground(ip);
                }                
            }
            catch (Exception ex) { CoreLog.Warn($"LoadHistory : {ex.Message}"); }
        }

        private void SaveHistory()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile)!);
                List<IntrusionEvent> snapshot;
                lock (_alertsLock) snapshot = _alerts.ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(snapshot,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HistoryFile, json);
            }
            catch (Exception ex) { CoreLog.Warn($"SaveHistory : {ex.Message}"); }
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

                if (list == null || list.Count == 0)
                {
                    // Fichier vide : s'assure qu'il n'y a pas de règles orphelines
                    FirewallHelper.SyncFirewallRules(Enumerable.Empty<string>());
                    return;
                }

                lock (_alertsLock)
                {
                    _blockedIps.Clear();
                    _blockedIps.AddRange(list);
                }

                CoreLog.Info($"Chargement {list.Count} IP bloquées depuis le fichier");

                // Reconstruit TOUTES les règles d'un coup (migration des anciennes règles
                // individuelles vers les blocs groupés incluse)
                bool syncOk = FirewallHelper.SyncFirewallRules(list.Select(b => b.Ip));
                CoreLog.Info($"Sync pare-feu au démarrage : {(syncOk ? "OK" : "ÉCHEC")}");
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
            // Si l'IP est déjà bloquée, on ignore l'alerte
            lock (_alertsLock)
            {
                if (!string.IsNullOrWhiteSpace(ci.RemoteIp) &&
                    _blockedIps.Any(b => b.Ip == ci.RemoteIp))
                    return;
            }

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

            AddIntrusion(evt);
        }

        // ------------------------------------------------------------
        //  Traitement des paquets bruts (WinDivert)
        // ------------------------------------------------------------
        private void RaiseIntrusionPacket(string source, DetectorResult r, NetworkEvent evt)
        {
            // Si l'IP source est déjà bloquée, on ignore
            lock (_alertsLock)
            {
                if (!string.IsNullOrWhiteSpace(evt.SourceIp) &&
                    _blockedIps.Any(b => b.Ip == evt.SourceIp))
                    return;
            }

            var intrusion = new IntrusionEvent
            {
                Timestamp = evt.Timestamp,
                Source = source,
                Severity = r.Severity,
                Message = r.Message,
                RemoteIp = evt.SourceIp,
                RemotePort = evt.SourcePort,
                LocalPort = evt.DestPort,
                Pid = 0, // Pas de PID avec un paquet brut
                ProcessName = "Network Packet",
                Details = r.Details
            };

            AddIntrusion(intrusion);
        }

        // ------------------------------------------------------------
        //  Méthode commune d'ajout d'alerte
        // ------------------------------------------------------------
        private void AddIntrusion(IntrusionEvent evt)
        {
            bool isNewAlert = false;
            bool isWhitelisted = !string.IsNullOrWhiteSpace(evt.RemoteIp) && FirewallHelper.IsCriticalIp(evt.RemoteIp);

            // --- 1. Détection des scanners de recherche légitimes (Shadowserver, Censys…) ---
            if (_trustedScanners != null && !string.IsNullOrWhiteSpace(evt.RemoteIp))
            {
                var known = _trustedScanners.TryGetCached(evt.RemoteIp);

                if (known == true)
                {
                    evt.Severity = Severity.Info;
                    evt.Message = $"[SCANNER-LÉGITIME] {evt.Message}";

                    lock (_alertsLock)
                    {
                        _auditHistory.Add(evt);
                        if (_auditHistory.Count > 500) _auditHistory.RemoveAt(0);
                    }
                    SaveAudit();

                    CoreLog.Debug($"[SCANNER] Alerte ignorée : {evt.RemoteIp} est un scanner de recherche légitime ({evt.Source})");
                    return;
                }

                if (!known.HasValue)
                    _trustedScanners.CheckInBackground(evt.RemoteIp);
            }

            // --- 2. Filtre whitelist (Microsoft, Google, Cloudflare, réseau local) ---
            if (isWhitelisted)
            {
                evt.Severity = Severity.Info;
                evt.Message = $"[WHITELIST] {evt.Message}";

                lock (_alertsLock)
                {
                    _auditHistory.Add(evt);
                    if (_auditHistory.Count > 500) _auditHistory.RemoveAt(0);
                }
                SaveAudit();

                if (!string.IsNullOrWhiteSpace(evt.RemoteIp))
                    _geoIp?.ResolveInBackground(evt.RemoteIp);

                CoreLog.Debug($"[FILTRÉ] Alerte ignorée pour IP critique : {evt.RemoteIp} ({evt.Source})");
                return;
            }

            // --- 3. BLOCAGE AUTOMATIQUE ---
            bool wasAutoBlocked = false;

            if (_settings.AutoBlock
                && (evt.Severity == Severity.High || evt.Severity == Severity.Critical)
                && !string.IsNullOrWhiteSpace(evt.RemoteIp))
            {
                if (BlockIp(evt.RemoteIp, out string fwError,
                            evt.Source, evt.Message, evt.Details, evt.Severity.ToString()))
                {
                    CoreLog.Write($"[AUTO-BLOCK] IP {evt.RemoteIp} bloquée automatiquement " +
                                  $"(détecteur: {evt.Source}, sévérité: {evt.Severity})");
                    wasAutoBlocked = true;

                    // ? NOUVEAU : on garde l'IP visible dans les actives pendant 60s
                    // (BlockIp a vidé les actives ? on la remet pour le Tray)
                    _autoBlockedTempIps[evt.RemoteIp!] = DateTime.UtcNow.AddSeconds(AutoBlockVisibilitySeconds);
                }
                else
                {
                    CoreLog.Warn($"[AUTO-BLOCK] Échec du blocage de {evt.RemoteIp} : {fwError}");
                }
            }

            // --- 4. Traitement normal ---
            lock (_alertsLock)
            {
                // Toujours dans l'historique
                _alerts.Add(evt);
                if (_alerts.Count > 500) _alerts.RemoveAt(0);

                // ? NOUVEAU : ajouter aux actives si :
                //    - pas auto-bloquée (comportement normal)
                //    - OU auto-bloquée (visibilité 60s pour le Tray)
                _activeIntrusions.Add(evt);
                if (_activeIntrusions.Count > 200) _activeIntrusions.RemoveAt(0);

                var limit = DateTime.UtcNow.AddMinutes(-60);
                var recentDuplicates = _alerts.Where(a =>
                    a.RemoteIp == evt.RemoteIp &&
                    a.Source == evt.Source &&
                    a.Timestamp >= limit).ToList();

                if (recentDuplicates.Count == 1) isNewAlert = true;
            }

            CoreLog.Write($"[ALERT] {evt.Severity} | {evt.Source} | {evt.Message}");
            SaveHistory();

            // Email : toujours envoyé (même si auto-bloquée)
            if (isNewAlert && (evt.Severity == Severity.Critical || evt.Severity == Severity.High))
            {
                _ = SendAlertEmailAsync(evt);
            }

            // --- 4. Traitement normal ---
            lock (_alertsLock)
            {
                // Toujours ajouter à l'historique (traçabilité)
                _alerts.Add(evt);
                if (_alerts.Count > 500) _alerts.RemoveAt(0);

                // Ajouter aux intrusions actives UNIQUEMENT si pas auto-bloquée
                // (si bloquée, elle est déjà traitée ? pas besoin de la voir dans les actives)
                if (!wasAutoBlocked)
                {
                    _activeIntrusions.Add(evt);
                    if (_activeIntrusions.Count > 200) _activeIntrusions.RemoveAt(0);
                }

                var limit = DateTime.UtcNow.AddMinutes(-60);
                var recentDuplicates = _alerts.Where(a =>
                    a.RemoteIp == evt.RemoteIp &&
                    a.Source == evt.Source &&
                    a.Timestamp >= limit).ToList();

                if (recentDuplicates.Count == 1) isNewAlert = true;
            }

            CoreLog.Write($"[ALERT] {evt.Severity} | {evt.Source} | {evt.Message}");
            SaveHistory();

            // Email + notification Tray : toujours envoyés (même si auto-bloquée)
            if (isNewAlert && (evt.Severity == Severity.Critical || evt.Severity == Severity.High))
            {
                _ = SendAlertEmailAsync(evt);
            }

            // --- 4. Traitement normal (ajout aux listes) ---
            lock (_alertsLock)
            {
                _alerts.Add(evt);
                if (_alerts.Count > 500) _alerts.RemoveAt(0);

                _activeIntrusions.Add(evt);
                if (_activeIntrusions.Count > 200) _activeIntrusions.RemoveAt(0);

                var limit = DateTime.UtcNow.AddMinutes(-60);
                var recentDuplicates = _alerts.Where(a =>
                    a.RemoteIp == evt.RemoteIp &&
                    a.Source == evt.Source &&
                    a.Timestamp >= limit).ToList();

                if (recentDuplicates.Count == 1) isNewAlert = true;
            }

            CoreLog.Write($"[ALERT] {evt.Severity} | {evt.Source} | {evt.Message}");
            SaveHistory();

            if (isNewAlert && (evt.Severity == Severity.Critical || evt.Severity == Severity.High))
            {
                _ = SendAlertEmailAsync(evt);
            }
        }
        
        public void LoadAudit()
        {
            try
            {
                if (!File.Exists(AuditFile)) return;
                var json = File.ReadAllText(AuditFile);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<IntrusionEvent>>(json);
                if (list != null)
                {
                    lock (_alertsLock)
                    {
                        _auditHistory.Clear();
                        _auditHistory.AddRange(list);
                    }
                }
            }
            catch (Exception ex) { CoreLog.Warn($"LoadAudit : {ex.Message}"); }
        }

        private void SaveAudit()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AuditFile)!);
                List<IntrusionEvent> snapshot;
                lock (_alertsLock) snapshot = _auditHistory.ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(snapshot,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AuditFile, json);
            }
            catch (Exception ex) { CoreLog.Warn($"SaveAudit : {ex.Message}"); }
        }

        public List<IntrusionEvent> GetAuditHistory()
        {
            lock (_alertsLock) return _auditHistory.ToList();
        }
        
        public void ClearAudit()
        {
            lock (_alertsLock) _auditHistory.Clear();
            SaveAudit();
            CoreLog.Info("Historique d'audit (whitelist) vidé");
        }
        
        private async Task SendAlertEmailAsync(IntrusionEvent evt)
        {
            try
            {
                var cfg = EmailConfig.Load();
                if (string.IsNullOrEmpty(cfg.Server)) return;

                string body = $@"
                    <p><b>Détecteur :</b> {evt.Source}</p>
                    <p><b>Sévérité :</b> <span style='color:#c0392b'>{evt.Severity}</span></p>
                    <p><b>IP distante :</b> {evt.RemoteIp}:{evt.RemotePort}</p>
                    <p><b>Processus :</b> {evt.ProcessName} (PID {evt.Pid})</p>
                    <p><b>Message :</b> {evt.Message}</p>
                    {(string.IsNullOrEmpty(evt.Details) ? "" : $"<pre style='background:#f4f4f4;padding:8px'>{evt.Details}</pre>")}";

                await EmailSender.SendAsync(cfg, $"[ALERTE] Intrusion détectée sur {Environment.MachineName}", body, isHtml: true);
                CoreLog.Write($"[EMAIL] Alerte envoyée pour {evt.Source}");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email alerte : " + ex.Message);
            }
        }
        
        public async Task<GlobalStats> GetStatsAsync(int topIps = 30, int topCountries = 15)
        {
            if (_geoIp == null)
                _geoIp = new GeoIpService(_settings.GeoIpProvider, _settings.GeoIpEnabled, _settings.ReverseDnsEnabled);
            return await StatsBuilder.BuildAsync(this, _geoIp, topIps, topCountries);
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