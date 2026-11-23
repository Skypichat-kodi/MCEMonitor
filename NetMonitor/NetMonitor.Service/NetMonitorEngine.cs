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

        // Microsoft IP ranges + overrides manuels "Safe ?"
        private MicrosoftIpRangeService? _msRanges;
        private readonly ConcurrentDictionary<string, DateTime> _manualSafeIps = new();
        
        // Google IP ranges (crawlers officiels)
        private GoogleIpRangeService? _googleRanges;        

        private static readonly string ManualSafeFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "manual_safe_ips.json");

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

        // Refresh périodique des géolocs anciennes (rotation par lot)
        private DateTime _lastGeoRefresh = DateTime.MinValue;
        private DateTime _lastGeoRefreshCursor = DateTime.MinValue;
        private const int GeoRefreshBatchSize = 50;
        private static readonly TimeSpan GeoRefreshInterval = TimeSpan.FromHours(12);

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

        private static readonly string DataVersionFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "data_version.txt");

        private const int CurrentDataVersion = 4;

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

        private static readonly object _saveBlockedLock = new();
            
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

                    if (_activeIntrusions.Any(a => a.Severity == Severity.Critical))
                        return "critical";

                    // ────────────────────────────────────────────────────────
                    //  Ignorer les catégories informatives (Bot, Crawler, Trusted,
                    //  Safe, Whitelist) : ce ne sont PAS des menaces pour l'utilisateur.
                    //  Sans ce filtre, un simple scan de Shodan faisait passer le
                    //  tray en orange pendant 24h.
                    // ────────────────────────────────────────────────────────
                    bool hasRealThreat = _activeIntrusions.Any(a =>
                        a.Severity != Severity.Bot &&
                        a.Severity != Severity.Crawler &&
                        a.Severity != Severity.Trusted &&
                        a.Severity != Severity.Safe &&
                        a.Severity != Severity.Whitelisted);

                    return hasRealThreat ? "warning" : "ok";
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
            // Le service ignore tout des réglages : on lui passe des accesseurs
            // plutôt qu'une copie, pour qu'il lise toujours la valeur courante
            // (niveau de la liste) et qu'il puisse enregistrer la date de mise à jour.
            _threatIntel = new ThreatIntelService(
                () => _settings.BlacklistLevel,
                () => _settings.BlacklistLastUpdate,
                dt =>
                {
                    _settings.BlacklistLastUpdate = dt;
                    try
                    {
                        _settings.Save();
                    }
                    catch (Exception ex)
                    {
                        CoreLog.Warn($"ThreatIntel : sauvegarde de la date impossible : {ex.Message}");
                    }
                });
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

            _msRanges = new MicrosoftIpRangeService(_settings.MicrosoftIpRangeEnabled,
                                                     _settings.MicrosoftIpRangeRefreshHours);
                                                     
            _googleRanges = new GoogleIpRangeService(_settings.GoogleIpRangeEnabled,
                                                     _settings.GoogleIpRangeRefreshHours);
            _googleRanges.Start();

            // Migration des données au démarrage ═══
            MigrateDataIfNeeded();
                                 
            _msRanges.Start();
            LoadManualSafeIps();

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
            
            // Refresh périodique des géolocs anciennes (toutes les 12h, lot de 50)
            if (DateTime.UtcNow - _lastGeoRefresh >= GeoRefreshInterval)
            {
                _lastGeoRefresh = DateTime.UtcNow;
                RefreshOldGeoIps();
            }            
        }

        /// <summary>
        /// Refresh périodique des géolocs anciennes, par lots de 50, toutes les 12h.
        ///
        /// Rotation : on prend les 50 IPs dont GeoResolvedAt est le plus ancien,
        /// uniquement celles résolues avant _lastGeoRefreshCursor. Après traitement,
        /// le curseur avance → au bout d'un tour complet, il est réinitialisé.
        ///
        /// Garanties :
        ///   - Aucune IP n'est rafraîchie deux fois avant que toutes les autres
        ///     n'aient eu leur tour.
        ///   - Si le quota API est épuisé, le lot est reporté au prochain cycle.
        ///   - Délai d'1s entre chaque appel (pas de rafale).
        /// </summary>
        private void RefreshOldGeoIps()
        {
            if (_geoIp == null || !_geoIp.IsEnabled) return;
            if (_geoIp.IsQuotaExceeded)
            {
                CoreLog.Debug("[GEOIP-REFRESH] Quota épuisé — lot reporté au prochain cycle");
                return;
            }

            // ✅ Priorité 1 : rattraper les IPs jamais résolues (GeoResolvedAt == null)
            //    (typiquement celles bloquées pendant une période de quota épuisé)
            List<string> missing;
            lock (_alertsLock)
            {
                missing = _blockedIps
                    .Where(b => !b.IsProcess
                                && !string.IsNullOrWhiteSpace(b.Ip)
                                && !b.GeoResolvedAt.HasValue)
                    .Select(b => b.Ip)
                    .Take(GeoRefreshBatchSize)
                    .ToList();
            }

            if (missing.Count > 0)
            {
                CoreLog.Info($"[GEOIP-REFRESH] Priorité : {missing.Count} IP(s) jamais résolue(s) à rattraper");
                _ = Task.Run(async () =>
                {
                    int ok = 0, ko = 0;
                    foreach (var ip in missing)
                    {
                        if (_geoIp.IsQuotaExceeded) { ko = missing.Count - ok; break; }
                        await FinalizeBlockedIpGeoAsync(ip);
                        bool resolved;
                        lock (_alertsLock)
                        {
                            var e = _blockedIps.FirstOrDefault(b => !b.IsProcess
                                && string.Equals(b.Ip, ip, StringComparison.OrdinalIgnoreCase));
                            resolved = e?.GeoResolvedAt.HasValue == true;
                        }
                        if (resolved) ok++; else ko++;
                        await Task.Delay(1000);
                    }
                    CoreLog.Info($"[GEOIP-REFRESH] Rattrapage terminé : {ok} OK, {ko} KO");
                });
                return;   // on ne traite pas le lot "anciennes" ce cycle-ci
            }

            List<string> batch;
            lock (_alertsLock)
            {
                var cursor = _lastGeoRefreshCursor == DateTime.MinValue
                    ? DateTime.UtcNow
                    : _lastGeoRefreshCursor;

                batch = _blockedIps
                    .Where(b => !b.IsProcess
                                && !string.IsNullOrWhiteSpace(b.Ip)
                                && b.GeoResolvedAt.HasValue
                                && b.GeoResolvedAt.Value < cursor)
                    .OrderBy(b => b.GeoResolvedAt)
                    .Take(GeoRefreshBatchSize)
                    .Select(b => b.Ip)
                    .ToList();
            }

            if (batch.Count == 0)
            {
                if (_lastGeoRefreshCursor != DateTime.MinValue)
                {
                    CoreLog.Info("[GEOIP-REFRESH] Tour complet — curseur réinitialisé");
                    _lastGeoRefreshCursor = DateTime.MinValue;
                }
                return;
            }

            CoreLog.Info($"[GEOIP-REFRESH] Lot de {batch.Count} IP(s) à rafraîchir " +
                         $"(curseur: {_lastGeoRefreshCursor:yyyy-MM-dd HH:mm})");

            // Avance le curseur AVANT le traitement async
            _lastGeoRefreshCursor = DateTime.UtcNow;

            _ = Task.Run(async () =>
            {
                int ok = 0, ko = 0, skip = 0;
                foreach (var ip in batch)
                {
                    if (_geoIp.IsQuotaExceeded)
                    {
                        skip = batch.Count - ok - ko;
                        CoreLog.Warn($"[GEOIP-REFRESH] Quota épuisé en cours de lot " +
                                     $"({ok} OK, {ko} KO, {skip} reportée(s))");
                        break;
                    }

                    _geoIp.InvalidateCache(ip);
                    await FinalizeBlockedIpGeoAsync(ip);

                    bool updated;
                    lock (_alertsLock)
                    {
                        var entry = _blockedIps.FirstOrDefault(b =>
                            !b.IsProcess && string.Equals(b.Ip, ip, StringComparison.OrdinalIgnoreCase));
                        updated = entry?.GeoResolvedAt.HasValue == true;
                    }

                    if (updated) ok++; else ko++;
                    await Task.Delay(1000);
                }

                CoreLog.Info($"[GEOIP-REFRESH] Lot terminé : {ok} OK, {ko} KO, {skip} reportée(s)");
            });
        }

        public void Stop()
        {
            CoreLog.Write("NetMonitorEngine.Stop() : démarrage de l'arrêt propre");     
        
            _threatIntel?.Stop();
            _threatIntel?.Dispose();
            _threatIntel = null;

            _msRanges?.Stop();
            
            _googleRanges?.Stop();
            _googleRanges?.Dispose();
            _googleRanges = null;            

            _emailScheduler?.Stop();
            _emailScheduler?.Dispose();
            _emailScheduler = null;

            _capture?.Stop();
            _packetChannel?.Writer.TryComplete();
            try { _consumerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _capture?.Dispose();

            _evictTimer?.Dispose();
            _evictTimer = null;

            _autoBlockCleanupTimer?.Dispose();
            _autoBlockCleanupTimer = null;
            
            _etw.Stop();

            // Dispose _geoIp en dernier : les appels ResolveAsync() en vol (depuis
            // AddIntrusion ou FinalizeBlockedIpGeoAsync) sont alors terminés ou verront
            // le flag _disposed et abandonneront immédiatement.
            _geoIp?.Dispose();
            _geoIp = null;

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
            lock (_alertsLock)
            {
                if (_geoIp != null && _geoIp.IsEnabled)
                {
                    foreach (var a in _alerts)
                    {
                        if (string.IsNullOrWhiteSpace(a.RemoteIp)) continue;
                        if (!string.IsNullOrWhiteSpace(a.CountryCode)) continue;

                        var geo = _geoIp.TryGetCached(a.RemoteIp);
                        if (geo != null)
                        {
                            a.Country     = geo.Country;
                            a.CountryCode = geo.CountryCode;
                            a.Isp         = geo.Isp;
                            a.Org         = geo.Org;        
                            a.ReverseDns  = geo.ReverseDns; 
                        }
                        else
                        {
                            _geoIp.ResolveInBackground(a.RemoteIp);
                        }
                    }
                }
                return _alerts.ToList();
            }
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
            lock (_alertsLock)
            {
                if (_geoIp != null && _geoIp.IsEnabled)
                {
                    foreach (var a in _activeIntrusions)
                    {
                        if (string.IsNullOrWhiteSpace(a.RemoteIp)) continue;
                        if (!string.IsNullOrWhiteSpace(a.CountryCode)) continue;

                        var geo = _geoIp.TryGetCached(a.RemoteIp);
                        if (geo != null)
                        {
                            a.Country     = geo.Country;
                            a.CountryCode = geo.CountryCode;
                            a.Isp         = geo.Isp;
                            a.Org         = geo.Org;        
                            a.ReverseDns  = geo.ReverseDns; 
                        }
                        else
                        {
                            _geoIp.ResolveInBackground(a.RemoteIp);
                        }
                    }
                }
                return _activeIntrusions.ToList();
            }
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
            lock (_alertsLock)
            {
                // Garantie de cohérence : si un override manuel existe, Severity DOIT
                // refléter cet override. Évite toute incohérence entre la donnée
                // persistée et ce qui est affiché.
                foreach (var b in _blockedIps)
                {
                    if (b.ManualSeverity.HasValue)
                    {
                        string expected = b.ManualSeverity.Value.ToString();
                        if (b.Severity != expected)
                        {
                            CoreLog.Warn($"[SEVERITY] Incohérence {b.Ip} : " +
                                         $"Severity='{b.Severity}' vs Manual='{expected}' → correction");
                            b.Severity = expected;
                        }
                    }
                }
                return _blockedIps.ToList();
            }
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

            _retriesDirty = true;   
        }

        public bool BlockIp(string ip, out string errorMessage,
                            string? source = null, string? message = null,
                            string? details = null, string? severity = null,
                            bool isAutomatic = false,
                            bool forceManual = false,
                            IpOrigin origin = IpOrigin.Unknown)
        {
                    errorMessage = "";

            if (string.IsNullOrWhiteSpace(ip))
            {
                errorMessage = "IP vide.";
                return false;
            }

            if (FirewallHelper.IsCriticalIp(ip) && !forceManual)
            {
                errorMessage = $"L'adresse {ip} fait partie d'une plage critique et ne peut pas être bloquée.";
                CoreLog.Warn($"Blocage refusé : {ip} (IP critique, non forcé)");
                return false;
            }

            if (FirewallHelper.IsCriticalIp(ip) && forceManual)
            {
                CoreLog.Warn($"[FORCE] Blocage manuel d'une IP critique : {ip}");
            }

            bool alreadyBlocked = false;

            //Vérifier si c'est un scanner connu ---
            bool isKnownScanner = false;
            if (_trustedScanners != null && !string.IsNullOrWhiteSpace(ip))
            {
                var known = _trustedScanners.TryGetCached(ip);
                if (known == true) isKnownScanner = true;
            }

            lock (_alertsLock)
            {
                alreadyBlocked = _blockedIps.Any(b => b.Ip == ip);

                if (!alreadyBlocked)
                {
                    _blockedIps.Add(new BlockedIpInfo
                    {
                        Ip              = ip,
                        BlockedAt       = DateTime.UtcNow,
                        Source          = source ?? "",
                        Message         = message ?? "",
                        Details         = details,
                        Severity        = severity ?? "-",
                        IsKnownScanner  = isKnownScanner,
                        IsOutbound      = false,

                        // L'origine était perdue au blocage : le tableau des IP bloquées
                        // affichait « — » alors que la ligne des intrusions l'indiquait.
                        // Elle vient maintenant de l'événement qui a déclenché le blocage,
                        // ou de l'appelant pour une action manuelle.
                        Origin          = origin
                    });
                }
                _activeIntrusions.RemoveAll(a => a.RemoteIp == ip);
            }

            // Si déjà bloquée : rien de plus à faire
            if (alreadyBlocked)
                return true;

            // Resynchronisation complète des règles (recalcul de tous les blocs)
            var allIps = GetBlockedIps().Where(b => !b.IsProcess).Select(b => b.Ip).ToList();
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
        
        public bool BlockIp(string ip, string? source, string? message, string? details,
                            IpOrigin origin = IpOrigin.Unknown)
            => BlockIp(ip, out _, source, message, details, null, origin: origin);

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
        ///
        /// ⚠ Le backoff s'applique AUSSI quand la réponse est vide (pas seulement
        ///   en cas d'exception) — sinon les 3 tentatives s'enchaînent en rafale
        ///   et déclenchent le rate-limit de l'API.
        /// </summary>
        private async Task FinalizeBlockedIpGeoAsync(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return;
            if (_geoIp == null || !_geoIp.IsEnabled) return;

            if (_geoIp.IsQuotaExceeded)
            {
                CoreLog.Debug($"[GEOIP] {ip} reportée (quota épuisé)");
                return;
            }
            
            GeoInfo? geo = null;
            string lastReason = "inconnue";

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (_geoIp.IsQuotaExceeded)
                {
                    lastReason = "quota épuisé";
                    break;
                }                       
                try
                {
                    var result = await _geoIp.ResolveAsync(ip, includeReverseDns: true);

                    // Succès = CountryCode non vide ET ResolvedAt valide
                    if (result != null &&
                        !string.IsNullOrEmpty(result.CountryCode) &&
                        result.ResolvedAt != DateTime.MinValue)
                    {
                        geo = result;
                        break;
                    }

                    lastReason = result == null
                        ? "réponse nulle"
                        : (result.ResolvedAt == DateTime.MinValue
                            ? "échec API (non caché)"
                            : "CountryCode vide");
                }
                catch (Exception ex)
                {
                    lastReason = ex.Message;
                }

                CoreLog.Warn($"[GEOIP] {ip} tentative {attempt}/3 : {lastReason}");

                if (attempt < 3)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(5 * attempt)); }
                    catch { }
                }
            }

            if (geo == null)
            {
                CoreLog.Warn($"[GEOIP] {ip} non résolue après 3 tentatives ({lastReason}) " +
                             $"(sera retentée au prochain démarrage)");
                return;
            }

            // Vérifier si c'est un scanner connu ---
            bool isKnownScanner = false;
            if (_trustedScanners != null)
            {
                var known = _trustedScanners.TryGetCached(ip);
                if (known == true)
                {
                    isKnownScanner = true;
                }
                else if (!known.HasValue)
                {
                    _trustedScanners.CheckInBackground(ip);
                }
            }

            // Mise à jour de l'entrée bloquée (une seule fois)
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

                if (isKnownScanner && !entry.IsKnownScanner)
                    entry.IsKnownScanner = true;

                // ✅ AJOUT : calcul de l'origine dès qu'on a le GeoIP
                var _ = SeverityScorer.Evaluate(
                    ip, geo.ReverseDns, geo.Isp, geo.Org,
                    entry.Source,
                    Enum.TryParse<Severity>(entry.Severity, out var curSev) ? curSev : Severity.Warning,
                    _settings, _googleRanges, _msRanges, _threatIntel,
                    out var computedOrigin);
                entry.Origin = computedOrigin;

                updated = true;
            }

            if (updated)
            {
                SaveBlockedIps();
                CoreLog.Info($"[GEOIP] {ip} enrichie : {geo.Country} ({geo.CountryCode}) — {geo.Isp} " +
                             $"{(isKnownScanner ? "[SCANNER CONNU]" : "")}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Réévaluation globale (migration v3)
        //  Corrige les IPs dégradées en Warning par l'ancienne migration v2.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retourne la sévérité de base d'un détecteur donné.
        /// Utilisé pour réévaluer correctement une IP bloquée.
        ///
        /// Pour Blacklist : refait la vraie vérification (l'IP peut avoir été
        /// retirée de la liste entre-temps) → Critical si toujours présente.
        ///
        /// Pour les autres : utilise la sévérité typique du détecteur.
        /// Limite connue : UntrustedProcess peut renvoyer Warning OU High selon
        /// le cas, impossible de le savoir a posteriori sans stocker la valeur
        /// exacte au moment du blocage.
        /// </summary>
        private Severity GetBaseSeverityForDetector(string? source, string? ip)
        {
            if (string.IsNullOrWhiteSpace(source))
                return Severity.Warning;

            // Blacklist : le détecteur a jugé l'IP dangereuse au moment du blocage.
            // Même si elle est retirée plus tard de la liste IPsum, elle reste un
            // risque historique. On retourne Critical par défaut.
            // (On NE vérifie PAS _threatIntel.IsBlacklisted car il n'est pas encore
            // chargé au moment de la migration au démarrage.)
            if (source.Equals("Blacklist", StringComparison.OrdinalIgnoreCase))
                return Severity.Critical;

            // Détecteurs à sévérité fixe
            return source switch
            {
                "PortScan"           => Severity.High,
                "SynFlood"           => Severity.High,
                "IcmpFlood"          => Severity.High,
                "Beaconing"          => Severity.High,
                "SuspiciousOutbound" => Severity.High,
                "DnsSpoof"           => Severity.Warning,
                "UntrustedProcess"   => Severity.Warning,   // approximation
                _                    => Severity.Warning
            };
        }

        /// <summary>
        /// Réévalue la sévérité de TOUTES les IPs bloquées (hors processus)
        /// en utilisant la sévérité de base du détecteur au lieu de tomber
        /// sur Warning par défaut.
        ///
        /// Appelée :
        ///  - Au démarrage (migration SeverityVersion < 3)
        ///  - Optionnellement via IPC (à venir)
        ///
        /// ⚠ Ne touche PAS aux IPs avec un override manuel (ManualSeverity.HasValue).
        /// </summary>
        public int ReevaluateAllBlockedIps() => ReanalyzeBlockedIps().Reclassified;

        /// <summary>
        /// Réanalyse tous les blocages d'IP : recalcule sévérité et origine avec la
        /// blacklist et les réglages du moment, et signale en plus les blocages qui
        /// ne seraient plus décidés aujourd'hui.
        ///
        /// Aucune IP n'est débloquée ici : le service rapporte, l'utilisateur décide.
        /// Les sévérités forcées à la main sont préservées.
        /// </summary>
        public ReevaluationReport ReanalyzeBlockedIps()
        {
            var report = new ReevaluationReport();

            lock (_alertsLock)
            {
                foreach (var b in _blockedIps)
                {
                    if (b.IsProcess) continue;

                    report.Analysed++;

                    if (b.ManualSeverity.HasValue)
                    {
                        report.ManualOverrides++;
                        continue;
                    }

                    var baseSeverity = GetBaseSeverityForDetector(b.Source, b.Ip);

                    var newSeverity = SeverityScorer.Evaluate(
                        b.Ip,
                        b.ReverseDns,
                        b.Isp,
                        b.Org,
                        b.Source,
                        baseSeverity,
                        _settings,
                        _googleRanges,
                        _msRanges,
                        _threatIntel,
                        out var newOrigin);

                    string newStr = newSeverity.ToString();
                    bool originChanged = b.Origin != newOrigin;

                    if (b.Severity != newStr || originChanged)
                    {
                        CoreLog.Info($"[REEVALUATE] {b.Ip} : '{b.Severity}' → '{newStr}', " +
                                     $"origin '{b.Origin}' → '{newOrigin}' " +
                                     $"(détecteur='{b.Source}', base='{baseSeverity}')");
                        b.Severity = newStr;
                        b.Origin   = newOrigin;
                        report.Reclassified++;
                    }

                    // Obsolescence : le blocage ne serait plus décidé aujourd'hui.
                    // On ne juge que si le blocage automatique est actif : sinon
                    // tous les blocages sont volontaires et doivent rester.
                    // Une IP encore blacklistée reste légitime, quel que soit le
                    // réglage : c'est la liste qui la désigne, pas un seuil.
                    bool encoreBlacklistee = _threatIntel?.IsBlacklisted(b.Ip) == true;

                    if (_settings.AutoBlock && !encoreBlacklistee && !WouldBlockNow(newSeverity))
                    {
                        report.Obsolete.Add(new ObsoleteBlock
                        {
                            Ip       = b.Ip,
                            Severity = newStr,
                            Source   = b.Source,
                            Reason   = $"plus dans la blacklist, sévérité {newStr} non bloquante"
                        });
                    }
                }
            }

            if (report.Reclassified > 0) SaveBlockedIps();

            CoreLog.Info($"[REEVALUATE] Terminé : {report.Analysed} IP(s) analysée(s), " +
                         $"{report.Reclassified} reclassée(s), {report.Obsolete.Count} obsolète(s), " +
                         $"{report.ManualOverrides} override(s) manuel(s) préservé(s)");

            return report;
        }

        /// <summary>
        /// Répond à la question : cette sévérité déclencherait-elle encore un
        /// blocage automatique avec les réglages en vigueur ?
        /// </summary>
        private bool WouldBlockNow(Severity severity) => severity switch
        {
            Severity.Whitelisted => false,
            Severity.Trusted     => false,
            Severity.Safe        => false,
            Severity.ToCheck     => false,   // jamais bloqué automatiquement
            Severity.Critical    => _settings.AutoBlockCritical,
            Severity.High        => _settings.AutoBlockCritical,
            Severity.Warning     => _settings.AutoBlockWarning,
            Severity.Info        => _settings.AutoBlockWarning,
            Severity.Bot         => _settings.BlockBots,
            Severity.Crawler     => _settings.BlockCrawlers,
            _                    => true     // inconnu : on ne touche pas
        };

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
                    var baseSeverity = GetBaseSeverityForDetector(entry.Source, entry.Ip);

                    var autoSev = SeverityScorer.Evaluate(
                        entry.Ip,
                        entry.ReverseDns,
                        entry.Isp,
                        entry.Org,
                        entry.Source,
                        baseSeverity,
                        _settings,
                        _googleRanges,
                        _msRanges,          
                        _threatIntel,       
                        out var autoOrigin); 
                    entry.Severity = autoSev.ToString();
                    entry.Origin = autoOrigin;  
                }
            }

            SaveBlockedIps();

            // Trace les 3 valeurs pour diagnostiquer immédiatement toute incohérence
            var entryAfter = _blockedIps.FirstOrDefault(b =>
                !b.IsProcess && string.Equals(b.Ip, ip, StringComparison.OrdinalIgnoreCase));

            CoreLog.Info(
                $"[SEVERITY] IP {ip} → demandé='{(newSeverity?.ToString() ?? "auto")}', " +
                $"Severity='{entryAfter?.Severity}', " +
                $"ManualSeverity='{entryAfter?.ManualSeverity?.ToString() ?? "null"}'");

            return true;
        }

        /// <summary>
        /// Force manuellement l'origine d'une IP bloquée, ou la recalcule automatiquement.
        ///
        /// ⚠ L'origine manuelle est PERDUE si l'IP est débloquée puis re-bloquée
        /// (attribut du blocage, pas de l'IP).
        /// </summary>
        public bool SetBlockedIpOrigin(string ip, string origin)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(origin))
                return false;

            BlockedIpInfo? entry;
            lock (_alertsLock)
            {
                entry = _blockedIps.FirstOrDefault(b =>
                    !b.IsProcess && string.Equals(b.Ip, ip, StringComparison.OrdinalIgnoreCase));

                if (entry == null) return false;

                if (string.Equals(origin, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    // Recalcul auto via SeverityScorer
                    var geo = _geoIp?.TryGetCached(ip);
                    SeverityScorer.Evaluate(
                        ip,
                        geo?.ReverseDns,
                        geo?.Isp,
                        geo?.Org,
                        entry.Source,
                        Enum.TryParse<Severity>(entry.Severity, out var s) ? s : Severity.Warning,
                        _settings,
                        _googleRanges,
                        _msRanges,
                        _threatIntel,
                        out var computed);
                    entry.Origin = computed;
                }
                else if (Enum.TryParse<IpOrigin>(origin, true, out var parsed))
                {
                    entry.Origin = parsed;
                }
                else
                {
                    return false;   // origine invalide
                }
            }

            SaveBlockedIps();
            CoreLog.Info($"[ORIGIN] IP {ip} → '{entry.Origin}' (demandé: {origin})");
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

            var allIps = GetBlockedIps().Where(b => !b.IsProcess).Select(b => b.Ip).ToList();
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
                if (!File.Exists(HistoryFile))
                {
                    SaveHistory();
                    return;
                }
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

            // ✅ Amorcer le cache GeoIP avec les géolocs déjà connues de l'historique
            //    (évite que GetAlerts() déclenche des résolutions en boucle sur l'UI)
            if (_geoIp != null && _geoIp.IsEnabled)
            {
                int seeded = 0;
                lock (_alertsLock)
                {
                    foreach (var a in _alerts)
                    {
                        if (string.IsNullOrWhiteSpace(a.RemoteIp)) continue;
                        if (string.IsNullOrWhiteSpace(a.CountryCode)) continue;

                        _geoIp.SeedCache(a.RemoteIp, new GeoInfo
                        {
                            Ip          = a.RemoteIp,
                            Country     = a.Country ?? "",
                            CountryCode = a.CountryCode,
                            Isp         = a.Isp ?? "",
                            ResolvedAt  = a.Timestamp   // approximation : la géoloc a été faite au moment de l'alerte
                        });
                        seeded++;
                    }
                }
                if (seeded > 0)
                    CoreLog.Info($"[GEOIP] Cache amorcé : {seeded} IP(s) depuis alert_history.json");
            }

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

        /// <summary>
        /// Libère TOUTES les IPs bloquées du pare-feu d'un coup.
        /// ⚠ Les processus bloqués sont conservés (gérés séparément via UnblockProcess).
        /// </summary>
        public bool ClearAllBlockedIps()
        {
            int removedCount;
            lock (_alertsLock)
            {
                removedCount = _blockedIps.RemoveAll(b => !b.IsProcess);
                _autoBlockedTempIps.Clear();
            }

            if (removedCount == 0)
            {
                CoreLog.Info("[CLEAR-ALL] Aucune IP bloquée à libérer.");
                return true;
            }

            CoreLog.Info($"[CLEAR-ALL] {removedCount} IP(s) retirée(s) de la liste interne — nettoyage du pare-feu…");

            // ⚠ On garde la synchro pour être sûr que le pare-feu est propre avant de répondre.
            //   Mais on évite le double appel DeleteAllBlockRules + SyncFirewallRules.
            var remainingIps = GetBlockedIps()
                .Where(b => !b.IsProcess)
                .Select(b => b.Ip)
                .ToList();

            // DeleteAllBlockRules supprime TOUTES les règles NetMonitor-Block-*
            // SyncFirewallRules reconstruit seulement ce qui reste (normalement vide)
            FirewallHelper.DeleteAllBlockRules();
            bool syncOk = FirewallHelper.SyncFirewallRules(remainingIps);

            _blockedIpRetries.Clear();
            SaveBlockedIpRetries();
            SaveBlockedIps();

            CoreLog.Info($"[CLEAR-ALL] {removedCount} IP(s) libérée(s) du pare-feu (sync: {(syncOk ? "OK" : "ÉCHEC")})");

            return syncOk;
        }
        
        // ------------------------------------------------------------
        //  Persistance des IP bloquées
        // ------------------------------------------------------------
        public void LoadBlockedIps()
        {
            try
            {
                if (!File.Exists(BlockedIpsFile))
                {
                    // Crée un fichier vide pour matérialiser l'état (0 IP bloquée)
                    SaveBlockedIps();
                    return;
                }

                var json = File.ReadAllText(BlockedIpsFile);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<BlockedIpInfo>>(json, JsonFileOpts);

                if (list == null || list.Count == 0)
                {
                    // Fichier vide : s'assure qu'il n'y a pas de règles orphelines
                    FirewallHelper.SyncFirewallRules(Enumerable.Empty<string>());
                    return;
                }

                // Mise à jour des anciennes IP bloquées
                if (_trustedScanners != null)
                {
                    foreach (var b in list)
                    {
                        if (!b.IsProcess && !b.IsKnownScanner)
                        {
                            if (_trustedScanners.TryGetCached(b.Ip) == true)
                                b.IsKnownScanner = true;
                        }
                    }
                }
                
                // Déduplication : un même processus a pu être bloqué plusieurs fois avant
                // le correctif de BlockProcess (qui n'effectuait aucune vérification). On ne
                // garde que l'entrée la plus récente par clé — chemin du processus, sinon IP —
                // car les doublons s'affichent en double et font échouer les dictionnaires
                // indexés par nom de processus.
                int avantDedup = list.Count;
                list = list
                    .GroupBy(b => b.IsProcess
                                    ? "P|" + (b.ProcessPath ?? b.Ip)
                                    : "I|" + b.Ip,
                             StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(b => b.BlockedAt).First())
                    .ToList();
                int doublonsSupprimes = avantDedup - list.Count;
                if (doublonsSupprimes > 0)
                    CoreLog.Info($"LoadBlockedIps : {doublonsSupprimes} doublon(s) supprimé(s)");

                lock (_alertsLock)
                {
                    _blockedIps.Clear();
                    _blockedIps.AddRange(list);
                }

                // Persiste le nettoyage une seule fois
                if (doublonsSupprimes > 0)
                    SaveBlockedIps();

                // ✅ Amorcer le cache GeoIP avec les géolocs déjà connues
                //    → aucune requête réseau pour ces IPs au démarrage ni ensuite
                if (_geoIp != null && _geoIp.IsEnabled)
                {
                    int seeded = 0;
                    foreach (var b in list)
                    {
                        if (b.IsProcess) continue;
                        if (string.IsNullOrWhiteSpace(b.Ip)) continue;
                        if (!b.GeoResolvedAt.HasValue) continue;
                        if (string.IsNullOrWhiteSpace(b.CountryCode)) continue;

                        _geoIp.SeedCache(b.Ip, new GeoInfo
                        {
                            Ip          = b.Ip,
                            Country     = b.Country ?? "",
                            CountryCode = b.CountryCode,
                            City        = b.City ?? "",
                            Isp         = b.Isp ?? "",
                            Org         = b.Org ?? "",
                            IsPrivate   = b.IsPrivate,
                            ReverseDns  = b.ReverseDns,
                            ResolvedAt  = b.GeoResolvedAt.Value
                        });
                        seeded++;
                    }
                    CoreLog.Info($"[GEOIP] Cache amorcé : {seeded} IP(s) depuis blocked_ips.json");
                }

                CoreLog.Info($"Chargement {list.Count} IP bloquées depuis le fichier");

                // Reconstruit TOUTES les règles d'un coup (migration des anciennes règles
                // individuelles vers les blocs groupés incluse)
                bool syncOk = FirewallHelper.SyncFirewallRules(
                    list.Where(b => !b.IsProcess).Select(b => b.Ip));
                CoreLog.Info($"Sync pare-feu au démarrage : {(syncOk ? "OK" : "ÉCHEC")}");

                // Rétablit les règles des processus bloqués : SyncFirewallRules ne traite
                // que les IP, donc un vidage des règles laisserait ces processus affichés
                // comme bloqués sans aucune règle active.
                FirewallHelper.SyncProcessRules(
                    list.Where(b => b.IsProcess).Select(b => b.ProcessPath ?? ""));

                // ============================================================
                //  Migration one-shot de la sévérité (v3)
                //
                //  Corrige les IPs dégradées en Warning par l'ancienne
                //  migration v2 (qui utilisait b.Severity en base, laquelle
                //  avait déjà été dégradée à Warning → cercle vicieux).
                //
                //  v3 utilise GetBaseSeverityForDetector() qui donne la vraie
                //  sévérité selon le détecteur d'origine (Blacklist = Critical,
                //  PortScan = High, etc.), avec relance réelle de la vérification
                //  Blacklist pour les IPs qui en viennent.
                // ============================================================
                if (_settings.SeverityVersion < 4)
                {
                    CoreLog.Info($"[MIGRATION] SeverityVersion {_settings.SeverityVersion} → 4 (fix Blacklist = Critical)");

                    int reevaluated = ReevaluateAllBlockedIps();

                    _settings.SeverityVersion = 4;
                    _settings.Save();

                    CoreLog.Info($"[MIGRATION] {reevaluated} IP(s) corrigée(s) — passage en v4 terminé");
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
            lock (_saveBlockedLock)   // ✅ un seul writer à la fois
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(BlockedIpsFile)!);

                    List<BlockedIpInfo> snapshot;
                    lock (_alertsLock) snapshot = _blockedIps.ToList();

                    var json = System.Text.Json.JsonSerializer.Serialize(snapshot, JsonFileOpts);

                    // ✅ Écriture atomique : tmp + rename → pas de fichier à moitié écrit
                    //    et Windows accepte le Move même si un lecteur (UI) a le fichier ouvert.
                    string tmpFile = BlockedIpsFile + ".tmp";
                    File.WriteAllText(tmpFile, json);

                    // Move avec écrasement (Windows : File.Move(src, dst, overwrite:true) sur .NET 5+)
                    try
                    {
                        File.Move(tmpFile, BlockedIpsFile, overwrite: true);
                    }
                    catch (IOException)
                    {
                        // Fallback : si le rename échoue (antivirus, lecteur lent), on retente
                        // après un court délai, puis on écrit directement en dernier recours.
                        try { Thread.Sleep(50); File.Move(tmpFile, BlockedIpsFile, overwrite: true); }
                        catch (IOException)
                        {
                            File.WriteAllText(BlockedIpsFile, json);
                            try { File.Delete(tmpFile); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    CoreLog.Warn($"SaveBlockedIps : {ex.Message}");
                }
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
        // ⚠️ EnableProcessTrust n'est PAS rechargé à chaud (nécessite un redémarrage)
        // Raison : UntrustedProcessDetector capture _processTrust au constructeur.
        public void ReloadConfig()
        {
            bool oldAutoBlock        = _settings.AutoBlock;
            bool oldMsEnabled        = _settings.MicrosoftIpRangeEnabled;
            int  oldMsRefreshHours   = _settings.MicrosoftIpRangeRefreshHours;
            bool oldGoogleEnabled    = _settings.GoogleIpRangeEnabled;
            int  oldGoogleRefreshHrs = _settings.GoogleIpRangeRefreshHours;
            bool oldGeoEnabled       = _settings.GeoIpEnabled;
            string oldGeoProvider    = _settings.GeoIpProvider;
            bool oldReverseDns       = _settings.ReverseDnsEnabled;

            _settings.Reload();
            _processTrust?.Reload();

            // ============================================================
            //  1. Patterns scanners (toujours propagés, pas cher)
            // ============================================================
            _trustedScanners?.UpdatePatterns(_settings.ScannerPatterns);

            // ============================================================
            //  2. Ranges Microsoft — reconstruire si toggle ou refreshHours changé
            // ============================================================
            if (oldMsEnabled != _settings.MicrosoftIpRangeEnabled
                || oldMsRefreshHours != _settings.MicrosoftIpRangeRefreshHours)
            {
                CoreLog.Info($"[CONFIG] MicrosoftIpRange : ({oldMsEnabled}, {oldMsRefreshHours}h) → " +
                             $"({_settings.MicrosoftIpRangeEnabled}, {_settings.MicrosoftIpRangeRefreshHours}h)");

                _msRanges?.Stop();
                _msRanges = new MicrosoftIpRangeService(
                    _settings.MicrosoftIpRangeEnabled,
                    _settings.MicrosoftIpRangeRefreshHours);
                _msRanges.Start();
            }

            // ============================================================
            //  3. Ranges Google — idem
            // ============================================================
            if (oldGoogleEnabled != _settings.GoogleIpRangeEnabled
                || oldGoogleRefreshHrs != _settings.GoogleIpRangeRefreshHours)
            {
                CoreLog.Info($"[CONFIG] GoogleIpRange : ({oldGoogleEnabled}, {oldGoogleRefreshHrs}h) → " +
                             $"({_settings.GoogleIpRangeEnabled}, {_settings.GoogleIpRangeRefreshHours}h)");

                _googleRanges?.Stop();
                _googleRanges?.Dispose();
                _googleRanges = new GoogleIpRangeService(
                    _settings.GoogleIpRangeEnabled,
                    _settings.GoogleIpRangeRefreshHours);
                _googleRanges.Start();
            }

            // ============================================================
            //  4. Mode AutoBlock — notifier le scheduler email
            // ============================================================
            if (oldAutoBlock != _settings.AutoBlock)
            {
                CoreLog.Info($"[CONFIG] Changement de mode AutoBlock : {oldAutoBlock} → {_settings.AutoBlock}");
                _emailScheduler?.OnModeChanged(oldAutoBlock, _settings.AutoBlock);
            }

            // ============================================================
            //  5. GeoIP — reconstruire si l'un des trois réglages a changé
            // ============================================================
            if (oldGeoEnabled != _settings.GeoIpEnabled
                || !string.Equals(oldGeoProvider, _settings.GeoIpProvider, StringComparison.Ordinal)
                || oldReverseDns != _settings.ReverseDnsEnabled)
            {
                CoreLog.Info($"[CONFIG] GeoIp : activé {oldGeoEnabled} → {_settings.GeoIpEnabled}, " +
                             $"fournisseur « {oldGeoProvider} » → « {_settings.GeoIpProvider} », " +
                             $"reverse DNS {oldReverseDns} → {_settings.ReverseDnsEnabled}");

                // L'ancien objet n'est volontairement pas libéré : des résolutions
                // peuvent être en vol et le champ _geoIp est lu sans verrou. Il est
                // abandonné au ramasse-miettes, ce qui évite une ObjectDisposedException
                // pour un réglage qui ne change qu'à la main et très rarement.
                _geoIp = new GeoIpService(
                    _settings.GeoIpProvider,
                    _settings.GeoIpEnabled,
                    _settings.ReverseDnsEnabled);
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
                Timestamp   = ci.Timestamp,
                Source      = source,
                Severity    = r.Severity,
                Message     = r.Message,
                RemoteIp    = ci.RemoteIp,
                RemotePort  = ci.RemotePort,
                LocalPort   = ci.LocalPort,
                Pid         = ci.Pid,
                ProcessName = ci.ProcessName,
                ProcessPath = r.ProcessPath,
                TrustReason = r.TrustReason,               
                Details     = r.Details,
                IsOutbound  = ci.IsOutbound, 
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
                    Timestamp   = DateTime.UtcNow,
                    Source      = "UntrustedProcess",
                    Severity    = Severity.Info,
                    Message     = $"[WHITELIST MANUELLE] {pending.ProcessName} ajouté à la liste blanche",
                    RemoteIp    = pending.RemoteIp,
                    RemotePort  = pending.RemotePort,
                    Pid         = pending.Pid,
                    ProcessName = pending.ProcessName,
                    ProcessPath = pending.ProcessPath,
                    TrustReason = "Ajouté à la liste blanche manuellement",
                    Details     = processPath
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
                    // Un même processus peut être bloqué plusieurs fois (il réapparaît après
                    // le blocage). On met à jour l'entrée existante au lieu d'en ajouter une
                    // seconde : sinon la liste contient des doublons et les dictionnaires
                    // indexés par nom de processus (affichage web, IPC) lèvent
                    // « An item with the same key has already been added ».
                    var existing = _blockedIps.FirstOrDefault(b =>
                        b.IsProcess &&
                        string.Equals(b.ProcessPath, pending.ProcessPath, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        existing.BlockedAt = DateTime.UtcNow;
                        existing.Severity  = pending.Severity.ToString();
                        existing.Message   = pending.TrustReason ?? existing.Message;
                    }
                    else
                    {
                        _blockedIps.Add(new BlockedIpInfo
                        {
                            Ip              = pending.ProcessName,         // nom court pour l'affichage
                            ProcessPath     = pending.ProcessPath,
                            IsProcess       = true,
                            BlockedAt       = DateTime.UtcNow,
                            Source          = "UntrustedProcess",
                            Message         = pending.TrustReason ?? "Processus bloqué",
                            Details         = pending.ProcessPath,
                            Severity        = pending.Severity.ToString(),   // ← restauré (comme avant)
                            Origin          = pending.Origin
                        });
                    }
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
                Details = r.Details,
                IsOutbound = evt.IsOutbound
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
                evt.Severity,
                _settings,
                _googleRanges,
                _msRanges,         
                _threatIntel,    
                out var origin);  
            evt.Origin = origin;  

            // Marquer les scanners connus (sans les ignorer)
            if (_trustedScanners != null && !string.IsNullOrWhiteSpace(evt.RemoteIp))
            {
                var known = _trustedScanners.TryGetCached(evt.RemoteIp);
                if (known == true)
                {
                    evt.IsKnownScanner = true;
                    evt.TrustReason = "Scanner de recherche légitime (bloqué en mode strict)";
                }
                else if (!known.HasValue)
                {
                    _trustedScanners.CheckInBackground(evt.RemoteIp);
                }
            }

            // --- 1. Détection des scanners de recherche légitimes ---
            if (_trustedScanners != null && !string.IsNullOrWhiteSpace(evt.RemoteIp))
            {
                // ⚠ NE PAS écraser Bot/Crawler
                if (evt.Severity == Severity.Bot || evt.Severity == Severity.Crawler)
                {
                    // On laisse la sévérité telle quelle (déjà décidée par SeverityScorer)
                    CoreLog.Debug($"[CRAWLER] {evt.RemoteIp} → {evt.Severity} (ignoré pour le check scanner)");
                }
                else
                {
                    var known = _trustedScanners.TryGetCached(evt.RemoteIp);

                    if (known != true && giForSeverity != null)
                    {
                        bool isOrgMatch = MatchesScannerPatterns(giForSeverity.Org, _settings.ScannerPatterns) ||
                                          MatchesScannerPatterns(giForSeverity.Isp, _settings.ScannerPatterns);
                        if (isOrgMatch) known = true;
                    }

                    if (known == true)
                    {
                        evt.IsKnownScanner = true;
                        evt.Severity = Severity.Trusted;   // ← Modifié
                        evt.Message = $"[SCANNER CONNU] {evt.Message}";
                        evt.TrustReason = "Scanner de recherche légitime (bloqué en mode strict)";
                    }
                    else if (!known.HasValue)
                    {
                        _trustedScanners.CheckInBackground(evt.RemoteIp);
                    }
                }
            }

            // --- 2a. Override manuel "Safe ?" ---
            if (!string.IsNullOrWhiteSpace(evt.RemoteIp) && _manualSafeIps.ContainsKey(evt.RemoteIp))
            {
                evt.Severity = Severity.Safe;
                evt.Message = $"[WHITELIST] {evt.Message}";
                evt.TrustReason = "Marqué Safe manuellement";

                lock (_alertsLock)
                {
                    _auditHistory.Add(evt);
                    if (_auditHistory.Count > 500) _auditHistory.RemoveAt(0);
                }
                SaveAudit();

                _geoIp?.ResolveInBackground(evt.RemoteIp);
                CoreLog.Debug($"[SAFE?] {evt.RemoteIp} → Safe (override manuel)");
                return;
            }

            // --- 2b. Cas "Microsoft-like" — vérifier dans le JSON officiel ---
            bool isMSLike = !string.IsNullOrWhiteSpace(evt.RemoteIp) && FirewallHelper.IsMicrosoftLike(evt.RemoteIp);

            if (isMSLike)
            {
                bool inJson = _msRanges?.IsMicrosoftIp(evt.RemoteIp!) == true;

                if (inJson)
                {
                    // ✓ Vraie IP Microsoft → Safe (audit)
                    evt.Severity = Severity.Safe;
                    evt.Message = $"[WHITELIST] {evt.Message}";
                    evt.TrustReason = "IP Microsoft vérifiée (liste officielle)";

                    lock (_alertsLock)
                    {
                        _auditHistory.Add(evt);
                        if (_auditHistory.Count > 500) _auditHistory.RemoveAt(0);
                    }
                    SaveAudit();

                    _geoIp?.ResolveInBackground(evt.RemoteIp!);
                    CoreLog.Debug($"[MS-VERIFIED] {evt.RemoteIp} → Safe (liste officielle)");
                    return;
                }
                else
                {
                    // ✗ Absente du JSON → ToCheck (tableau 1, pas d'auto-block)
                    evt.Severity = Severity.ToCheck;
                    evt.Message = $"[À VÉRIFIER] {evt.Message}";
                    evt.TrustReason = "Ressemble à Microsoft mais absente de la liste officielle";
                    CoreLog.Warn($"[MS-SUSPECT] {evt.RemoteIp} → ToCheck " +
                                 $"(JSON: {(_msRanges?.IsLoaded == true ? "chargé" : "non chargé")})");
                    // ↓ Continue vers les intrusions actives
                }
            }
            // --- 2c. Autres IP critiques (Google / Cloudflare / privé) → Safe ---
            else if (!string.IsNullOrWhiteSpace(evt.RemoteIp) && FirewallHelper.IsCriticalIp(evt.RemoteIp))
            {
                evt.Severity = Severity.Safe;
                evt.Message = $"[WHITELIST] {evt.Message}";
                evt.TrustReason = "IP de confiance (Google / Cloudflare / réseau local)";

                lock (_alertsLock)
                {
                    _auditHistory.Add(evt);
                    if (_auditHistory.Count > 500) _auditHistory.RemoveAt(0);
                }
                SaveAudit();

                _geoIp?.ResolveInBackground(evt.RemoteIp);
                CoreLog.Debug($"[FILTRÉ] {evt.RemoteIp} → Safe (autre IP critique)");
                return;
            }

            // --- 3. BLOCAGE AUTOMATIQUE (si activé) ---
            if (_settings.AutoBlock && !string.IsNullOrWhiteSpace(evt.RemoteIp))
            {
                bool shouldBlock = false;

                if (evt.IsKnownScanner)
                {
                    shouldBlock = _settings.BlockKnownScanners;
                }
                else if (evt.Severity == Severity.Critical || evt.Severity == Severity.High)
                {
                    shouldBlock = _settings.AutoBlockCritical;
                }
                else if (evt.Severity == Severity.Warning || evt.Severity == Severity.Info)
                {
                    shouldBlock = _settings.AutoBlockWarning;
                }
                else if (evt.Severity == Severity.Bot)
                {
                    shouldBlock = _settings.BlockBots;        
                }
                else if (evt.Severity == Severity.Crawler)
                {
                    shouldBlock = _settings.BlockCrawlers;    
                }
                // On ne bloque jamais Safe, Whitelisted, ToCheck.

                if (shouldBlock)
                {
                    if (BlockIp(evt.RemoteIp, out string fwError,
                                evt.Source, evt.Message, evt.Details, evt.Severity.ToString(),
                                isAutomatic: true, origin: evt.Origin))
                    {
                        CoreLog.Write($"[AUTO-BLOCK] IP {evt.RemoteIp} bloquée automatiquement " +
                                      $"(détecteur: {evt.Source}, sévérité: {evt.Severity})");

                        _autoBlockedTempIps[evt.RemoteIp!] = DateTime.UtcNow.AddSeconds(AutoBlockVisibilitySeconds);
                    }
                    else
                    {
                        CoreLog.Warn($"[AUTO-BLOCK] Échec du blocage de {evt.RemoteIp} : {fwError}");
                    }
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

            // --- 5. Enqueue email ---
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
                if (!File.Exists(AuditFile))
                {
                    SaveAudit();
                    return;
                }
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

                        // GeoIP sur l'IP distante du groupe
                        string? country = null, countryCode = null, isp = null, org = null, reverseDns = null;
                        if (_geoIp != null && _geoIp.IsEnabled && !string.IsNullOrWhiteSpace(last.RemoteIp))
                        {
                            var geo = _geoIp.TryGetCached(last.RemoteIp);
                            if (geo != null)
                            {
                                country     = geo.Country;
                                countryCode = geo.CountryCode;
                                isp         = geo.Isp;
                                org         = geo.Org;       
                                reverseDns  = geo.ReverseDns;
                            }
                            else
                            {
                                _geoIp.ResolveInBackground(last.RemoteIp);
                            }
                        }

                        return new AuditGroupedEntry
                        {
                            FirstSeen       = first.Timestamp,
                            LastSeen        = last.Timestamp,
                            Count           = g.Count(),
                            Severity        = mostSevere.Severity,
                            Source          = last.Source,
                            RemoteIp        = last.RemoteIp,
                            RemotePort      = last.RemotePort,
                            LocalPort       = last.LocalPort,
                            Pid             = last.Pid,
                            ProcessName     = last.ProcessName,
                            ProcessPath     = last.ProcessPath,
                            TrustReason     = mostSevere.TrustReason,
                            Details         = mostSevere.Details,
                            IsKnownScanner  = g.Any(x => x.IsKnownScanner),
                            Country         = country,
                            CountryCode     = countryCode,
                            Isp             = isp,
                            Org             = org,          
                            ReverseDns      = reverseDns,                        
                            IsOutbound      = last.IsOutbound
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
            // _geoIp est initialisé dans le constructeur — jamais null en pratique.
            // Le check défensif précédent créait une 2ᵉ instance non disposée (fuite HttpClient).
            return await StatsBuilder.BuildAsync(this, _geoIp!, topIps, topCountries,
                autoBlockMode: _settings.AutoBlock);
        }
        
        /// <summary>
        /// Vérifie si une valeur (Org ou Isp) correspond à un des patterns de scanner.
        /// Nettoie les espaces et les extensions (.com, .net) pour faciliter la comparaison.
        /// Ex: "ONYPHE SAS" -> "onyphesas" contient "onyphe" -> MATCH
        /// Ex: "Palo Alto Networks, Inc" -> "paloaltonetworksinc" contient "paloaltonetworks" -> MATCH
        /// </summary>
        private static bool MatchesScannerPatterns(string? value, string[] patterns)
        {
            if (string.IsNullOrWhiteSpace(value) || patterns == null || patterns.Length == 0) 
                return false;
            
            string normalizedValue = value.ToLowerInvariant().Replace(" ", "").Replace(",", "");
            
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                
                string normalizedPattern = pattern.ToLowerInvariant()
                                                 .Replace(" ", "")
                                                 .Replace(".com", "")
                                                 .Replace(".net", "")
                                                 .Replace(".org", "");
                                                 
                if (normalizedValue.Contains(normalizedPattern)) 
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Marque une IP comme "Safe" manuellement (bouton "Safe ?" du tableau 1).
        /// - Migre toutes les alertes ToCheck de cette IP vers _auditHistory.
        /// - Persiste un override pour que les futures alertes sur cette IP
        ///   soient automatiquement classées Safe (bypass du check JSON Microsoft).
        /// </summary>
        public bool MarkSafe(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;

            int migrated = 0;
            lock (_alertsLock)
            {
                var toMigrate = _activeIntrusions
                    .Where(a => string.Equals(a.RemoteIp, ip, StringComparison.OrdinalIgnoreCase)
                             && a.Severity == Severity.ToCheck)
                    .ToList();

                foreach (var evt in toMigrate)
                {
                    _activeIntrusions.Remove(evt);
                    _alerts.Remove(evt);

                    evt.Severity = Severity.Safe;
                    evt.TrustReason = "Marqué Safe manuellement";
                    if (evt.Message?.StartsWith("[À VÉRIFIER]") == true)
                        evt.Message = evt.Message.Substring("[À VÉRIFIER] ".Length);

                    _auditHistory.Add(evt);
                    migrated++;
                }

                if (_auditHistory.Count > 500) _auditHistory.RemoveAt(0);
                _manualSafeIps[ip] = DateTime.UtcNow;
            }

            SaveAudit();
            SaveHistory();
            SaveManualSafeIps();
            CoreLog.Info($"[SAFE?] {ip} → Safe (manuel, {migrated} alerte(s) migrée(s))");
            return migrated > 0;
        }

        private void LoadManualSafeIps()
        {
            try
            {
                if (!File.Exists(ManualSafeFile))
                {
                    SaveManualSafeIps();
                    return;
                }
                var json = File.ReadAllText(ManualSafeFile);
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
                if (dict == null) return;
                foreach (var kv in dict) _manualSafeIps[kv.Key] = kv.Value;
                CoreLog.Info($"[SAFE?] {dict.Count} override(s) 'Safe' chargé(s)");
            }
            catch (Exception ex) { CoreLog.Warn($"LoadManualSafeIps : {ex.Message}"); }
        }

        private void SaveManualSafeIps()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ManualSafeFile)!);
                var json = System.Text.Json.JsonSerializer.Serialize(
                    _manualSafeIps.ToDictionary(kv => kv.Key, kv => kv.Value),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ManualSafeFile, json);
            }
            catch (Exception ex) { CoreLog.Warn($"SaveManualSafeIps : {ex.Message}"); }
        }
        // ═══════════════════════════════════════════════════════════════════════
        //  Migration des données persistées
        //  Objectif : mettre à jour les anciens enregistrements avec les nouveaux
        //  champs (IsOutbound, Severity Bot/Crawler/Safe/ToCheck...) sans perdre
        //  les données existantes.
        // ═══════════════════════════════════════════════════════════════════════
        private void MigrateDataIfNeeded()
        {
            int currentVersion = 0;
            try
            {
                if (File.Exists(DataVersionFile))
                    currentVersion = int.Parse(File.ReadAllText(DataVersionFile).Trim());
            }
            catch { }

            if (currentVersion >= CurrentDataVersion)
                return;

            CoreLog.Info($"[MIGRATION] Données v{currentVersion} → v{CurrentDataVersion} — début");

            try
            {
                MigrateIntrusionEvents(_alerts, "alert_history");
                MigrateIntrusionEvents(_activeIntrusions, "active_intrusions");
                MigrateIntrusionEvents(_auditHistory, "audit_history");
                MigrateBlockedIps(_blockedIps);

                SaveHistory();
                SaveAudit();
                SaveBlockedIps();

                Directory.CreateDirectory(Path.GetDirectoryName(DataVersionFile)!);
                File.WriteAllText(DataVersionFile, CurrentDataVersion.ToString());

                CoreLog.Info($"[MIGRATION] Terminée avec succès");
            }
            catch (Exception ex)
            {
                CoreLog.Error($"[MIGRATION] Échec : {ex.Message}");
            }
        }

        private void MigrateIntrusionEvents(List<IntrusionEvent> events, string label)
        {
            int mOutbound = 0, mSeverity = 0;

            foreach (var evt in events)
            {
                // ─── IsOutbound ───
                if (!evt.IsOutbound && IsLikelyOutboundSource(evt.Source))
                {
                    evt.IsOutbound = true;
                    mOutbound++;
                }

                // ─── Severity : re-classification complète ───
                var newSev = ReclassifySeverity(
                    evt.RemoteIp,
                    evt.Severity,
                    evt.Source,
                    evt.IsKnownScanner);

                if (newSev != evt.Severity)
                {
                    evt.Severity = newSev;
                    mSeverity++;
                }
            }

            if (mOutbound > 0 || mSeverity > 0)
            {
                CoreLog.Info($"[MIGRATION] {label} : {mOutbound} IsOutbound, " +
                             $"{mSeverity} Severity réparée ({events.Count} événements)");
            }
        }

        private void MigrateBlockedIps(List<BlockedIpInfo> blockedList)
        {
            int mOutbound = 0, mSeverity = 0;

            foreach (var b in blockedList)
            {
                // IsOutbound
                if (!b.IsOutbound && IsLikelyOutboundSource(b.Source))
                {
                    b.IsOutbound = true;
                    mOutbound++;
                }

                // Severity : ne pas toucher si override manuel
                if (b.ManualSeverity.HasValue) continue;

                var newSev = ReclassifySeverity(
                    b.Ip,
                    Enum.TryParse<Severity>(b.Severity, out var sev) ? sev : Severity.Warning,
                    b.Source,
                    b.IsKnownScanner);

                string newSevStr = newSev.ToString();
                if (b.Severity != newSevStr)
                {
                    b.Severity = newSevStr;
                    mSeverity++;
                }
            }

            if (mOutbound > 0 || mSeverity > 0)
            {
                CoreLog.Info($"[MIGRATION] blocked_ips : {mOutbound} IsOutbound, " +
                             $"{mSeverity} Severity réparée ({blockedList.Count} IPs)");
            }
        }

        /// <summary>
        /// Re-classifie une sévérité en ré-appliquant la logique de AddIntrusion
        /// (Safe/ToCheck/Bot/Crawler), mais sans créer d'alerte.
        /// Utilisé uniquement par la migration.
        /// </summary>
        private Severity ReclassifySeverity(
            string? remoteIp,
            Severity currentSeverity,
            string? source,
            bool isKnownScanner)
        {
            if (string.IsNullOrWhiteSpace(remoteIp))
                return currentSeverity;

            // 1. Override manuel "Safe ?" → Safe
            if (_manualSafeIps.ContainsKey(remoteIp))
                return Severity.Safe;

            // 2. Microsoft-like
            if (FirewallHelper.IsMicrosoftLike(remoteIp))
            {
                if (_msRanges?.IsMicrosoftIp(remoteIp) == true)
                    return Severity.Safe;
                return Severity.ToCheck;
            }

            // 3. Autres IP critiques (Google / Cloudflare / privé) → Safe
            if (FirewallHelper.IsCriticalIp(remoteIp))
                return Severity.Safe;

            // 4. Google officiel → Bot
            if (_googleRanges?.IsGoogleIp(remoteIp) == true)
                return Severity.Bot;

            // 5. Crawler générique (Yandex, Baidu...) → Crawler
            var gi = _geoIp?.TryGetCached(remoteIp);
            if (MatchesCrawlerPatterns(gi?.Isp) || MatchesCrawlerPatterns(gi?.Org))
                return Severity.Crawler;

            // ═══════════════════════════════════════════════════════════════
            // 6. NOUVEAU : Scanners (Palo Alto, Shodan, Censys...) → Trusted
            //    Doit passer AVANT le Critical du détecteur Blacklist
            // ═══════════════════════════════════════════════════════════════
            if (isKnownScanner ||
                MatchesScannerPatterns(gi?.Isp, _settings.ScannerPatterns) ||
                MatchesScannerPatterns(gi?.Org, _settings.ScannerPatterns))
                return Severity.Trusted;

            // 7. Détecteur critique → conserver
            if (currentSeverity == Severity.Critical || currentSeverity == Severity.High)
                return currentSeverity;

            // 8. Domaine malveillant → Critical
            if (MatchesScannerPatterns(gi?.Isp, _settings.CriticalPatterns)
                || MatchesScannerPatterns(gi?.Org, _settings.CriticalPatterns))
                return Severity.Critical;

            // 9. Safe patterns → Safe
            if (MatchesScannerPatterns(gi?.Isp, _settings.SafePatterns)
                || MatchesScannerPatterns(gi?.Org, _settings.SafePatterns))
                return Severity.Safe;

            // 10. Défaut
            return Severity.Warning;
        }

        private bool MatchesCrawlerPatterns(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (_settings.CrawlerPatterns == null) return false;

            string normalized = value.ToLowerInvariant();
            return _settings.CrawlerPatterns.Any(p =>
                !string.IsNullOrWhiteSpace(p)
                && normalized.Contains(p.ToLowerInvariant()));
        }

        private static bool IsLikelyOutboundSource(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            return source.Contains("UntrustedProcess", StringComparison.OrdinalIgnoreCase)
                || source.Contains("Beaconing", StringComparison.OrdinalIgnoreCase)
                || source.Contains("SuspiciousOutbound", StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// Re-classifie toutes les IPs bloquées qui matchent un pattern récemment ajouté.
        /// Appelé après AddPattern() pour mettre à jour les badges existants.
        /// </summary>
        public int ReclassifyBlockedIpsByPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return 0;

            string normalized = pattern.Trim().ToLowerInvariant();
            int updated = 0;

            lock (_alertsLock)
            {
                foreach (var b in _blockedIps)
                {
                    if (b.IsProcess) continue;
                    if (b.ManualSeverity.HasValue) continue;   // ne pas toucher aux overrides manuels

                    // Vérifie si l'IP matche le pattern (via ISP, Org ou ReverseDns)
                    bool matches =
                        (!string.IsNullOrWhiteSpace(b.Isp)        && b.Isp.Contains(normalized, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(b.Org)        && b.Org.Contains(normalized, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(b.ReverseDns) && b.ReverseDns.Contains(normalized, StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(b.Ip, pattern, StringComparison.OrdinalIgnoreCase);

                    if (!matches) continue;

                    var newSev = ReclassifySeverity(
                        b.Ip,
                        Enum.TryParse<Severity>(b.Severity, out var sev) ? sev : Severity.Warning,
                        b.Source,
                        b.IsKnownScanner);

                    string newSevStr = newSev.ToString();
                    if (b.Severity != newSevStr)
                    {
                        b.Severity = newSevStr;
                        updated++;
                    }
                }
            }

            if (updated > 0)
            {
                SaveBlockedIps();
                CoreLog.Info($"[PATTERN] {updated} IP(s) bloquée(s) re-classifiée(s) après ajout du pattern '{pattern}'");
            }

            return updated;
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