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
        private EmailDigestScheduler? _emailScheduler;
        private ProcessTrustService? _processTrust;

        private readonly ConcurrentQueue<ConnectionInfo> _recentConnections = new();
        private const int MaxRecentConnections = 1000;
        
        private readonly List<PendingProcessInfo> _pendingProcesses = new();
        private readonly object _pendingLock = new();        

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

        // Dernier blocage réussi (exposé via /api/summary et les pushs)
        public LastBlockInfo? LastBlock { get; private set; }
        private readonly object _lastBlockLock = new();
        private static readonly System.Text.Json.JsonSerializerOptions JsonFileOpts = new()
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
                                    
        // Journal d'audit pour les alertes ignorées (whitelist)
        private readonly List<IntrusionEvent> _auditHistory = new();

        // Compteurs de tentatives observées sur des IP déjà bloquées     
        private readonly ConcurrentDictionary<string, BlockedIpRetryInfo> _blockedIpRetries = new();
        private bool _retriesDirty = false;        

        private static readonly string AuditFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "audit_history.json");
            
        private static readonly string BlockedIpRetriesFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "blocked_ip_retries.json");            
            
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
            _trustedScanners = new TrustedScannerService(settings.ScannerPatterns);
            _geoIp = new GeoIpService(settings.GeoIpProvider, settings.GeoIpEnabled, settings.ReverseDnsEnabled);
            if (settings.EnableProcessTrust)
            {
                _processTrust = new ProcessTrustService();
                _context.ProcessTrust = _processTrust;
                CoreLog.Info("ProcessTrust : activé");
            }            

            _etw = new EtwConnectionWatcher();
            _etw.ConnectionOpened += OnConnectionOpened;

            _connDetectors = new List<IConnectionAwareDetector>
            {
                new PortScanDetector(),
                new BeaconingDetector(),
                new BlacklistIpDetector(_threatIntel),
                new SuspiciousOutboundDetector(),
            };

            if (_processTrust != null)
                _connDetectors.Add(new UntrustedProcessDetector(_processTrust));

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
            _threatIntel?.Start();

            _emailScheduler = new EmailDigestScheduler(_settings);
            _emailScheduler.Start();

            _etw.Start();
            _evictTimer = new System.Threading.Timer(
                _ => _context.EvictStale(TimeSpan.FromMinutes(30)),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));

            // --- Initialisation de la capture WinDivert ---
            _packetChannel = Channel.CreateBounded<RawPacket>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
            _capture = new PacketCaptureService("ip", _packetChannel.Writer);
            _capture.OnError += (s, ex) =>
            {
                CoreLog.Error($"Erreur capture WinDivert : {ex.Message}");
                ReportProblem(
                    // ex.Source peut valoir "WinDivertRestart" (échec après retry) ou null (erreur générique)
                    category: string.IsNullOrEmpty(ex.Source) ? "WinDivert" : ex.Source,
                    message:  ex.Message,
                    severity: "critical");
            };
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

            // ... code existant : nettoyage des IPs auto-bloquées temporaires ...

            var expired = _autoBlockedTempIps
                .Where(kv => kv.Value <= now)
                .Select(kv => kv.Key)
                .ToList();

            if (expired.Count > 0)
            {
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

            // ✅ NOUVEAU : purge des retries > 7 jours
            var cutoff = now.AddDays(-7);
            var staleRetries = _blockedIpRetries
                .Where(kv => kv.Value.LastRetryAt < cutoff)
                .Select(kv => kv.Key)
                .ToList();

            if (staleRetries.Count > 0)
            {
                foreach (var ip in staleRetries)
                    _blockedIpRetries.TryRemove(ip, out _);

                CoreLog.Info($"[INTERCEPTIONS] {staleRetries.Count} entrée(s) purgée(s) (> 7 jours)");
                _retriesDirty = true;
            }

            // ✅ NOUVEAU : sauvegarde si nécessaire
            if (_retriesDirty)
                SaveBlockedIpRetries();
                
            // ✅ NOUVEAU : Purge des processus en attente qui n'existent plus
            lock (_pendingLock)
            {
                var deadProcesses = new List<string>();

                // On utilise le 'now' déjà déclaré au début de la méthode
                foreach (var p in _pendingProcesses)
                {
                    // Si le processus est en attente depuis plus de 5 minutes
                    if ((now - p.FirstSeen).TotalMinutes > 5)
                    {
                        bool isAlive = false;
                        if (p.Pid > 0)
                        {
                            try
                            {
                                using var proc = System.Diagnostics.Process.GetProcessById(p.Pid);
                                isAlive = !proc.HasExited;
                            }
                            catch { isAlive = false; }
                        }

                        if (!isAlive)
                            deadProcesses.Add(p.ProcessPath);
                    }
                }

                if (deadProcesses.Count > 0)
                {
                    foreach (var path in deadProcesses)
                        _pendingProcesses.RemoveAll(p => p.ProcessPath == path);
                    
                    CoreLog.Debug($"[PENDING] Purge auto : {deadProcesses.Count} processus mort(s) retiré(s).");
                }
            }
        }

        public void Stop()
        {
            _threatIntel?.Stop();

            _emailScheduler?.Stop();
            _emailScheduler?.Dispose();
            _emailScheduler = null;

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

        public List<BlockedIpRetryInfo> GetBlockedIpRetries()
        {
            return _blockedIpRetries.Values
                .OrderByDescending(r => r.RetryCount)
                .ThenByDescending(r => r.LastRetryAt)
                .ToList();
        }

        public void ClearBlockedIpRetries()
        {
            _blockedIpRetries.Clear();
            SaveBlockedIpRetries();
            CoreLog.Info("Compteurs de tentatives sur IP bloquée vidés");
        }

        private void RegisterBlockedIpRetry(string ip, string? originalDetector, DateTime blockedAt)
        {
            var now = DateTime.UtcNow;

            _blockedIpRetries.AddOrUpdate(
                ip,
                _ => new BlockedIpRetryInfo
                {
                    Ip = ip,
                    OriginalDetector = string.IsNullOrWhiteSpace(originalDetector) ? "-" : originalDetector,
                    BlockedAt = blockedAt,
                    FirstRetryAt = now,
                    LastRetryAt = now,
                    RetryCount = 1
                },
                (_, existing) =>
                {
                    existing.LastRetryAt = now;
                    existing.RetryCount++;
                    return existing;
                });

            _retriesDirty = true;   // ← ajout
        }

        public bool BlockIp(string ip, out string errorMessage,
                            string? source = null, string? message = null,
                            string? details = null, string? severity = null,
                            bool isAutomatic = false)
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
            
            lock (_lastBlockLock)
            {
                LastBlock = new LastBlockInfo
                {
                    Ip = ip,
                    Detector = source,
                    Timestamp = DateTime.UtcNow,
                    Automatic = isAutomatic
                };
            }

            // Résolution GeoIP en tâche de fond (ne bloque pas le thread ETW)
            _ = Task.Run(() => FinalizeBlockedIpGeoAsync(ip));

            return true;
        }
        
        public bool BlockIp(string ip, string? source, string? message, string? details)
            => BlockIp(ip, out _, source, message, details, null);

        /// <summary>
        /// Résout les informations GeoIP d'une IP bloquée en tâche de fond, puis
        /// met à jour l'entrée dans _blockedIps et persiste sur disque.
        ///
        /// Appelée :
        ///   - après chaque blocage (manuel ou auto) via fire-and-forget
        ///   - au démarrage pour les IPs bloquées sans GeoResolvedAt (backfill)
        ///
        /// Retry : 3 tentatives avec backoff (5s, 10s, 15s) pour absorber
        /// les micro-coupures réseau de l'API ipwho.is.
        /// </summary>
        private async Task FinalizeBlockedIpGeoAsync(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return;
            if (_geoIp == null || !_geoIp.IsEnabled) return;

            GeoInfo? geo = null;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    // includeReverseDns: false → on économise 2s par IP,
                    // le reverse DNS n'étant pas utilisé dans les stats bloquées.
                    geo = await _geoIp.ResolveAsync(ip, includeReverseDns: false);

                    if (geo != null && !string.IsNullOrEmpty(geo.CountryCode))
                        break;   // succès
                }
                catch (Exception ex)
                {
                    CoreLog.Warn($"[GEOIP] {ip} tentative {attempt}/3 : {ex.Message}");
                }

                if (attempt < 3)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(5 * attempt)); }
                    catch { }
                }
            }

            if (geo == null || string.IsNullOrEmpty(geo.CountryCode))
            {
                CoreLog.Warn($"[GEOIP] {ip} non résolue après 3 tentatives (sera retentée au prochain démarrage)");
                return;
            }

            // Mise à jour de l'entrée bloquée
            bool updated = false;
            lock (_alertsLock)
            {
                var entry = _blockedIps.FirstOrDefault(b =>
                    !b.IsProcess && string.Equals(b.Ip, ip, StringComparison.OrdinalIgnoreCase));

                if (entry == null) return;   // l'IP a été débloquée entre-temps

                entry.Country       = geo.Country;
                entry.CountryCode   = geo.CountryCode;
                entry.City          = geo.City;
                entry.Isp           = geo.Isp;
                entry.Org           = geo.Org;
                entry.IsPrivate     = geo.IsPrivate;
                entry.ReverseDns    = geo.ReverseDns;
                entry.GeoResolvedAt = DateTime.UtcNow;
                updated = true;
            }

            if (updated)
            {
                SaveBlockedIps();
                CoreLog.Info($"[GEOIP] {ip} enrichie : {geo.Country} ({geo.CountryCode}) — {geo.Isp}");
            }
        }

        /// <summary>
        /// Force manuellement la sévérité d'une IP bloquée, ou l'efface
        /// pour revenir à l'évaluation automatique via SeverityScorer.
        ///
        /// ⚠ La sévérité manuelle est PERDUE si l'IP est débloquée puis re-bloquée
        /// (attribut du blocage, pas de l'IP).
        /// </summary>
        public bool SetManualSeverity(string ip, Severity? newSeverity)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;

            BlockedIpInfo? entry;
            lock (_alertsLock)
            {
                entry = _blockedIps.FirstOrDefault(b =>
                    !b.IsProcess && string.Equals(b.Ip, ip, StringComparison.OrdinalIgnoreCase));

                if (entry == null) return false;

                entry.ManualSeverity = newSeverity;
                entry.ManualSeveritySetAt = newSeverity.HasValue ? DateTime.UtcNow : null;

                if (newSeverity.HasValue)
                {
                    // Override manuel → on écrit directement
                    entry.Severity = newSeverity.Value.ToString();
                }
                else
                {
                    // Retour à l'auto → ré-évalue avec les données persistées
                    var autoSev = SeverityScorer.Evaluate(
                        entry.Ip,
                        entry.ReverseDns,
                        entry.Isp,
                        entry.Org,
                        entry.Source,
                        _settings);
                    entry.Severity = autoSev.ToString();
                }
            }

            SaveBlockedIps();
            CoreLog.Info($"[SEVERITY] IP {ip} → {(newSeverity?.ToString() ?? "auto")}");
            return true;
        }

        public bool UnblockIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;

            _autoBlockedTempIps.TryRemove(ip, out _);
            _blockedIpRetries.TryRemove(ip, out _);
            SaveBlockedIpRetries();

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
            var list = System.Text.Json.JsonSerializer.Deserialize<List<IntrusionEvent>>(json, JsonFileOpts);
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

                // ? NOUVEAU : migration des blocked_ips sans severity
                // (récupère la sévérité depuis l'historique des alertes)
                int migrated = 0;
                foreach (var b in _blockedIps)
                {
                    if (!string.IsNullOrWhiteSpace(b.Severity) && b.Severity != "-")
                        continue;   // déjà renseignée

                    var original = _alerts.FirstOrDefault(a =>
                        string.Equals(a.RemoteIp, b.Ip, StringComparison.OrdinalIgnoreCase));

                    if (original != null && original.Severity != Severity.Info)
                    {
                        b.Severity = original.Severity.ToString();
                        migrated++;
                    }
                }

                if (migrated > 0)
                {
                    CoreLog.Info($"LoadHistory : {migrated} IP(s) bloquée(s) enrichie(s) avec la sévérité de l'historique");
                }
            }

            // ? SaveBlockedIps est appelé HORS du lock (il prend son propre lock en interne)
            SaveBlockedIps();

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

                var json = System.Text.Json.JsonSerializer.Serialize(snapshot, JsonFileOpts);
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
                var list = System.Text.Json.JsonSerializer.Deserialize<List<BlockedIpInfo>>(json, JsonFileOpts);

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

                // ============================================================
                //  Migration one-shot de la sévérité (D6)
                //  Ré-évalue toutes les IPs bloquées sans override manuel.
                // ============================================================
                if (_settings.SeverityVersion < 2)
                {
                    CoreLog.Info($"[MIGRATION] Passage de SeverityVersion {_settings.SeverityVersion} → 2");
                    int migrated = 0;
                    int skipped = 0;

                    lock (_alertsLock)
                    {
                        foreach (var b in _blockedIps)
                        {
                            if (b.IsProcess) continue;              // pas de GeoIP pour les processus
                            if (b.ManualSeverity.HasValue)          // override manuel → ne pas toucher
                            {
                                skipped++;
                                continue;
                            }

                            var newSev = SeverityScorer.Evaluate(
                                b.Ip,
                                b.ReverseDns,
                                b.Isp,
                                b.Org,
                                b.Source,
                                _settings);

                            string newSevStr = newSev.ToString();
                            if (b.Severity != newSevStr)
                            {
                                b.Severity = newSevStr;
                                migrated++;
                            }
                        }
                    }

                    _settings.SeverityVersion = 2;
                    _settings.Save();
                    SaveBlockedIps();

                    CoreLog.Info($"[MIGRATION] {migrated} IP(s) mise(s) à jour, {skipped} ignorée(s) (override manuel)");
                }
                                
                // ============================================================
                //  Backfill : résoudre les GeoIP manquants en tâche de fond
                //  (typiquement les IPs bloquées avant la mise à jour, ou
                //   dont la résolution avait échoué précédemment)
                // ============================================================
                List<string> toResolve;
                lock (_alertsLock)
                {
                    toResolve = _blockedIps
                        .Where(b => !b.IsProcess && !b.GeoResolvedAt.HasValue)
                        .Select(b => b.Ip)
                        .Where(i => !string.IsNullOrWhiteSpace(i))
                        .Distinct()
                        .ToList();
                }

                if (toResolve.Count > 0 && _geoIp != null && _geoIp.IsEnabled)
                {
                    CoreLog.Info($"[GEOIP] Backfill au démarrage : {toResolve.Count} IP(s) bloquée(s) à enrichir");
                    _ = Task.Run(async () =>
                    {
                        var opts = new ParallelOptions { MaxDegreeOfParallelism = 3 };
                        try
                        {
                            await Parallel.ForEachAsync(toResolve, opts,
                                async (ip, ct) =>
                                {
                                    await FinalizeBlockedIpGeoAsync(ip);
                                    await Task.Delay(200, ct);  // léger throttle entre IPs
                                });
                            CoreLog.Info($"[GEOIP] Backfill terminé ({toResolve.Count} IP(s) traitée(s))");
                        }
                        catch (Exception ex)
                        {
                            CoreLog.Warn($"[GEOIP] Backfill interrompu : {ex.Message}");
                        }
                    });
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

                var json = System.Text.Json.JsonSerializer.Serialize(snapshot, JsonFileOpts);

                File.WriteAllText(BlockedIpsFile, json);
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"SaveBlockedIps : {ex.Message}");
            }
        }

        public void LoadBlockedIpRetries()
        {
            try
            {
                if (!File.Exists(BlockedIpRetriesFile)) return;

                var json = File.ReadAllText(BlockedIpRetriesFile);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<BlockedIpRetryInfo>>(json, JsonFileOpts);

                if (list == null || list.Count == 0) return;

                // Purge : ne garde que les entrées dont la dernière tentative < 7 jours
                var cutoff = DateTime.UtcNow.AddDays(-7);
                var fresh = list.Where(r => r.LastRetryAt >= cutoff).ToList();

                int purged = list.Count - fresh.Count;

                _blockedIpRetries.Clear();
                foreach (var r in fresh)
                    _blockedIpRetries[r.Ip] = r;

                CoreLog.Info($"Interceptions restaurées : {fresh.Count} entrée(s)" +
                             (purged > 0 ? $" ({purged} expirée(s) purgée(s))" : ""));
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"LoadBlockedIpRetries : {ex.Message}");
            }
        }

        private void SaveBlockedIpRetries()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BlockedIpRetriesFile)!);

                var snapshot = _blockedIpRetries.Values
                    .OrderByDescending(r => r.LastRetryAt)
                    .ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(snapshot, JsonFileOpts);

                File.WriteAllText(BlockedIpRetriesFile, json);
                _retriesDirty = false;
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"SaveBlockedIpRetries : {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        //  Rechargement config / force scan
        // ------------------------------------------------------------
        public void ReloadConfig()
        {
            bool oldAutoBlock = _settings.AutoBlock;

            _settings.Reload();
            _processTrust?.Reload();

            if (oldAutoBlock != _settings.AutoBlock)
            {
                CoreLog.Info($"[CONFIG] Changement de mode AutoBlock : {oldAutoBlock} -> {_settings.AutoBlock}");
                _emailScheduler?.OnModeChanged(oldAutoBlock, _settings.AutoBlock);
            }
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
            // IP déjà bloquée ? enregistre la tentative et ignore l'alerte
            lock (_alertsLock)
            {
                if (!string.IsNullOrWhiteSpace(ci.RemoteIp))
                {
                    var blocked = _blockedIps.FirstOrDefault(b =>
                        !b.IsProcess && string.Equals(b.Ip, ci.RemoteIp, StringComparison.OrdinalIgnoreCase));

                    if (blocked != null)
                    {
                        RegisterBlockedIpRetry(ci.RemoteIp, blocked.Source, blocked.BlockedAt);
                        return;
                    }
                }
            }

            // NOUVEAU : les processus non approuvés vont dans une liste dédiée,
            // SAUF si l'IP distante est whitelistée (Microsoft/Google/Cloudflare).
            // NOTE : on route même si ProcessPath est vide (processus protégés,
            // droits insuffisants, PID recyclé) ? évite qu'ils tombent dans
            // l'historique avec une sévérité Info inutile.
            if (r.IsPendingProcess)
            {
                bool isWhitelisted = !string.IsNullOrWhiteSpace(ci.RemoteIp) && FirewallHelper.IsCriticalIp(ci.RemoteIp);

                if (!isWhitelisted)
                {
                    AddOrUpdatePendingProcess(r, ci);
                    return;
                }
                // Sinon : on laisse tomber vers AddIntrusion qui va détecter le
                // whitelist et mettre l'alerte dans _auditHistory.
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
                ProcessPath = r.ProcessPath,
                TrustReason = r.TrustReason,               
                Details = r.Details
            };

            AddIntrusion(evt);
        }

        private void AddOrUpdatePendingProcess(DetectorResult r, ConnectionInfo ci)
        {
            // ✅ Filtre les processus système légitimes (PID noyau, noms connus,
            //    chemins Microsoft avec signature valide) → jamais dans le tableau 1bis
            if (SystemProcessFilter.ShouldIgnore(ci.Pid, ci.ProcessName, r.ProcessPath))
            {
                CoreLog.Debug($"[PENDING] Ignoré (processus système) : PID={ci.Pid} name={ci.ProcessName} path={r.ProcessPath}");
                return;
            }

            // Fallback si le chemin n'a pas pu être résolu (processus protégé, PID recyclé…)
            string pathKey = string.IsNullOrWhiteSpace(r.ProcessPath)
                ? $"[no-path]::{ci.ProcessName ?? "?"}"
                : r.ProcessPath;
            string displayPath = string.IsNullOrWhiteSpace(r.ProcessPath)
                ? $"(chemin inaccessible — PID {ci.Pid})"
                : r.ProcessPath;
            string reason = string.IsNullOrWhiteSpace(r.ProcessPath)
                ? "chemin inaccessible (droits insuffisants ou processus protégé)"
                : (r.TrustReason ?? "");

            lock (_pendingLock)
            {
                var existing = _pendingProcesses.FirstOrDefault(p =>
                    string.Equals(p.ProcessPath, pathKey, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    _pendingProcesses.Add(new PendingProcessInfo
                    {
                        ProcessPath = pathKey,
                        ProcessName = ci.ProcessName ?? "?",
                        TrustReason = reason,
                        RemoteIp = ci.RemoteIp,
                        RemotePort = ci.RemotePort,
                        Pid = ci.Pid,
                        FirstSeen = DateTime.UtcNow,
                        LastSeen = DateTime.UtcNow,
                        Count = 1,
                        Severity = r.Severity,
                        Detector = "UntrustedProcess"
                    });

                    CoreLog.Info($"[PENDING] Nouveau processus à examiner : {ci.ProcessName} ({displayPath})");
                }
                else
                {
                    existing.LastSeen = DateTime.UtcNow;
                    existing.Count++;
                    existing.Pid = ci.Pid;
                    existing.RemoteIp = ci.RemoteIp;
                    existing.RemotePort = ci.RemotePort;
                    if (r.Severity > existing.Severity)
                        existing.Severity = r.Severity;
                }

                if (_pendingProcesses.Count > 200)
                    _pendingProcesses.RemoveAt(0);
            }
        }

        public List<PendingProcessInfo> GetPendingProcesses()
        {
            lock (_pendingLock) return _pendingProcesses.ToList();
        }

        public bool WhitelistProcess(string processPath)
        {
            if (string.IsNullOrWhiteSpace(processPath)) return false;
            if (_processTrust == null) return false;

            // Cherche le processus en attente pour récupérer son nom
            PendingProcessInfo? pending;
            lock (_pendingLock)
            {
                pending = _pendingProcesses.FirstOrDefault(p =>
                    string.Equals(p.ProcessPath, processPath, StringComparison.OrdinalIgnoreCase));
            }

            if (pending == null) return false;

            var entry = new ProcessTrustInfo
            {
                Name = pending.ProcessName,
                Path = pending.ProcessPath,
                Enabled = true
            };

            if (!_processTrust.AddToWhitelist(entry)) return false;

            lock (_pendingLock)
            {
                _pendingProcesses.RemoveAll(p =>
                    string.Equals(p.ProcessPath, processPath, StringComparison.OrdinalIgnoreCase));
            }

            // Trace dans l'audit
            lock (_alertsLock)
            {
                _auditHistory.Add(new IntrusionEvent
                {
                    Timestamp = DateTime.UtcNow,
                    Source = "UntrustedProcess",
                    Severity = Severity.Info,
                    Message = $"[WHITELIST MANUELLE] {pending.ProcessName} ajouté à la liste blanche",
                    RemoteIp = pending.RemoteIp,
                    RemotePort = pending.RemotePort,
                    Pid = pending.Pid,
                    ProcessName = pending.ProcessName,
                    ProcessPath = pending.ProcessPath,
                    TrustReason = "Ajouté à la liste blanche manuellement",
                    Details = processPath
                });
                if (_auditHistory.Count > 500) _auditHistory.RemoveAt(0);
            }
            SaveAudit();

            CoreLog.Info($"Processus whitelisté : {pending.ProcessName} ({processPath})");
            return true;
        }

        public bool BlockProcess(string processPath, out string errorMessage)
        {
            errorMessage = "";
            if (string.IsNullOrWhiteSpace(processPath))
            {
                errorMessage = "Chemin vide.";
                return false;
            }

            if (!FirewallHelper.BlockProcess(processPath, out errorMessage))
                return false;

            PendingProcessInfo? pending;
            lock (_pendingLock)
            {
                pending = _pendingProcesses.FirstOrDefault(p =>
                    string.Equals(p.ProcessPath, processPath, StringComparison.OrdinalIgnoreCase));
            }

            if (pending != null)
            {
                lock (_alertsLock)
                {
                    _blockedIps.Add(new BlockedIpInfo
                    {
                        Ip = pending.ProcessName,         // nom court pour l'affichage
                        ProcessPath = pending.ProcessPath,
                        IsProcess = true,
                        BlockedAt = DateTime.UtcNow,
                        Source = "UntrustedProcess",
                        Message = pending.TrustReason ?? "Processus bloqué",
                        Details = pending.ProcessPath,
                        Severity = pending.Severity.ToString(),   // ← restauré (comme avant)
                    });
                }

                lock (_pendingLock)
                {
                    _pendingProcesses.RemoveAll(p =>
                        string.Equals(p.ProcessPath, processPath, StringComparison.OrdinalIgnoreCase));
                }

                SaveBlockedIps();
                CoreLog.Info($"Processus bloqué : {pending.ProcessName} ({processPath})");
            }

            return true;
        }

        /// <summary>
        /// Retire un processus de la liste d'attente sans le whitelister ni le bloquer.
        /// Utile pour les faux positifs qu'on ne veut plus voir.
        /// </summary>
        public bool DismissPendingProcess(string processPathOrKey)
        {
            if (string.IsNullOrWhiteSpace(processPathOrKey)) return false;

            lock (_pendingLock)
            {
                // On cherche par chemin exact (qui peut être un chemin classique ou "[no-path]::Nom")
                int removed = _pendingProcesses.RemoveAll(p =>
                    string.Equals(p.ProcessPath, processPathOrKey, StringComparison.OrdinalIgnoreCase));

                if (removed > 0)
                {
                    CoreLog.Info($"[PENDING] Processus retiré manuellement de la liste : {processPathOrKey}");
                    return true;
                }
            }

            return false;
        }

        public bool UnblockProcess(string processPath)
        {
            if (string.IsNullOrWhiteSpace(processPath)) return false;

            FirewallHelper.UnblockProcess(processPath);

            bool removed = false;
            lock (_alertsLock)
            {
                removed = _blockedIps.RemoveAll(b => b.IsProcess &&
                    string.Equals(b.ProcessPath, processPath, StringComparison.OrdinalIgnoreCase)) > 0;
            }

            if (removed) SaveBlockedIps();
            return removed;
        }

        public List<ProcessTrustInfo> GetWhitelistEntries()
        {
            return _processTrust?.GetAll() ?? new List<ProcessTrustInfo>();
        }

        public bool RemoveWhitelistEntry(string path)
        {
            return _processTrust?.RemoveFromWhitelist(path) ?? false;
        }

        public bool SetWhitelistEntryEnabled(string path, bool enabled)
        {
            return _processTrust?.SetEnabled(path, enabled) ?? false;
        }

        // ------------------------------------------------------------
        //  Traitement des paquets bruts (WinDivert)
        // ------------------------------------------------------------
        private void RaiseIntrusionPacket(string source, DetectorResult r, NetworkEvent evt)
        {
            // Détermine qui est "remote" et qui est "local" selon le sens du paquet
            string remoteIp   = evt.IsOutbound ? evt.DestIp   : evt.SourceIp;
            int    remotePort = evt.IsOutbound ? evt.DestPort : evt.SourcePort;
            int    localPort  = evt.IsOutbound ? evt.SourcePort : evt.DestPort;

            // IP distante déjà bloquée ? enregistre la tentative et ignore
            lock (_alertsLock)
            {
                if (!string.IsNullOrWhiteSpace(remoteIp))
                {
                    var blocked = _blockedIps.FirstOrDefault(b =>
                        !b.IsProcess && string.Equals(b.Ip, remoteIp, StringComparison.OrdinalIgnoreCase));

                    if (blocked != null)
                    {
                        RegisterBlockedIpRetry(remoteIp, blocked.Source, blocked.BlockedAt);
                        return;
                    }
                }
            }

            var intrusion = new IntrusionEvent
            {
                Timestamp = evt.Timestamp,
                Source = source,
                Severity = r.Severity,
                Message = r.Message,
                RemoteIp = remoteIp,
                RemotePort = remotePort,
                LocalPort = localPort,
                Pid = 0,
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
            // ============================================================
            //  Évaluation de la sévérité via SeverityScorer (D5)
            //  ⚠ N'interfère PAS avec la décision de bloquer : c'est purement cosmétique.
            // ============================================================
            var giForSeverity = string.IsNullOrWhiteSpace(evt.RemoteIp)
                ? null
                : _geoIp?.TryGetCached(evt.RemoteIp);
            evt.Severity = SeverityScorer.Evaluate(
                evt.RemoteIp,
                giForSeverity?.ReverseDns,
                giForSeverity?.Isp,
                giForSeverity?.Org,
                evt.Source,
                _settings);

            bool isWhitelisted = !string.IsNullOrWhiteSpace(evt.RemoteIp) && FirewallHelper.IsCriticalIp(evt.RemoteIp);

            // --- 1. Détection des scanners de recherche légitimes ---
            if (_trustedScanners != null && !string.IsNullOrWhiteSpace(evt.RemoteIp))
            {
                var known = _trustedScanners.TryGetCached(evt.RemoteIp);

                if (known == true)
                {
                    evt.Severity = Severity.Trusted;   // ← au lieu de Info
                    evt.Message = $"[SCANNER-LÉGITIME] {evt.Message}";
                    evt.TrustReason = "Scanner de recherche légitime (reverse DNS)";

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
                evt.Severity = Severity.Safe;   // ← au lieu de Info
                evt.Message = $"[WHITELIST] {evt.Message}";
                evt.TrustReason = "IP de confiance (Microsoft / Google / Cloudflare / réseau local)";

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

            // --- 3. BLOCAGE AUTOMATIQUE (si activé) ---
            // ⚠ Plus de condition sur la sévérité : on bloque TOUT ce qui a une IP
            if (_settings.AutoBlock && !string.IsNullOrWhiteSpace(evt.RemoteIp))
            {
                if (BlockIp(evt.RemoteIp, out string fwError,
                            evt.Source, evt.Message, evt.Details, evt.Severity.ToString(),
                            isAutomatic: true))
                {
                    CoreLog.Write($"[AUTO-BLOCK] IP {evt.RemoteIp} bloquée automatiquement " +
                                  $"(détecteur: {evt.Source}, sévérité: {evt.Severity})");

                    // Maintien 60s dans les actives pour visibilité Tray
                    _autoBlockedTempIps[evt.RemoteIp!] = DateTime.UtcNow.AddSeconds(AutoBlockVisibilitySeconds);
                }
                else
                {
                    CoreLog.Warn($"[AUTO-BLOCK] Échec du blocage de {evt.RemoteIp} : {fwError}");
                }
            }

            // --- 4. Traitement normal (UNE SEULE FOIS) ---
            bool isNewAlert = false;

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

            // --- 5. Enqueue email (le scheduler décide quand envoyer) ---
            // Option B : envoyer pour Warning et plus (exclure Trusted/Safe/Whitelisted)
            if (isNewAlert &&
                (evt.Severity == Severity.Warning ||
                 evt.Severity == Severity.High ||
                 evt.Severity == Severity.Critical))
            {
                _emailScheduler?.Enqueue(evt);
            }
        }

        // ============================================================
        //  Problèmes persistants exposés à l'UI (popup, journal…)
        // ============================================================
        private readonly ConcurrentDictionary<string, ProblemItem> _persistentProblems = new();

        public void ReportProblem(string category, string message, string severity = "critical")
        {
            // Clé stable : même problème => même clé => pas de doublon
            string id = $"{category}:{message}";
            _persistentProblems[id] = new ProblemItem
            {
                Timestamp = DateTime.UtcNow,
                Severity  = severity,
                Category  = category,
                Message   = message
            };
            CoreLog.Warn($"[PROBLEM] {category} : {message}");
        }

        public List<ProblemItem> GetPersistentProblems()
        {
            return _persistentProblems.Values
                .OrderByDescending(p => p.Timestamp)
                .ToList();
        }

        public void ClearPersistentProblems()
        {
            _persistentProblems.Clear();
        }
        
        public void LoadAudit()
        {
            try
            {
                if (!File.Exists(AuditFile)) return;
                var json = File.ReadAllText(AuditFile);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<IntrusionEvent>>(json, JsonFileOpts);
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

                var json = System.Text.Json.JsonSerializer.Serialize(snapshot, JsonFileOpts);
                File.WriteAllText(AuditFile, json);
            }
            catch (Exception ex) { CoreLog.Warn($"SaveAudit : {ex.Message}"); }
        }

        public List<IntrusionEvent> GetAuditHistory()
        {
            lock (_alertsLock) return _auditHistory.ToList();
        }

        public List<AuditGroupedEntry> GetAuditHistoryGrouped()
        {
            lock (_alertsLock)
            {
                return _auditHistory
                    .GroupBy(a => new
                    {
                        a.Pid,
                        a.RemoteIp,
                        a.LocalPort,
                        a.RemotePort,
                        ProcessName = a.ProcessName ?? ""
                    })
                    .Select(g =>
                    {
                        var first = g.OrderBy(x => x.Timestamp).First();
                        var last  = g.OrderBy(x => x.Timestamp).Last();
                        var mostSevere = g.OrderByDescending(x => (int)x.Severity).First();

                        return new AuditGroupedEntry
                        {
                            FirstSeen   = first.Timestamp,
                            LastSeen    = last.Timestamp,
                            Count       = g.Count(),

                            Severity    = mostSevere.Severity,
                            Source      = last.Source,
                            RemoteIp    = last.RemoteIp,
                            RemotePort  = last.RemotePort,
                            LocalPort   = last.LocalPort,
                            Pid         = last.Pid,
                            ProcessName = last.ProcessName,
                            ProcessPath = last.ProcessPath,
                            TrustReason = mostSevere.TrustReason,
                            Details     = mostSevere.Details
                        };
                    })
                    .OrderByDescending(x => x.LastSeen)
                    .ToList();
            }
        }
        
        public void ClearAudit()
        {
            lock (_alertsLock) _auditHistory.Clear();
            SaveAudit();
            CoreLog.Info("Historique d'audit (whitelist) vidé");
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
    
    public sealed class LastBlockInfo
    {
        public string Ip { get; set; } = "";
        public string? Detector { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Automatic { get; set; }
    }    
}