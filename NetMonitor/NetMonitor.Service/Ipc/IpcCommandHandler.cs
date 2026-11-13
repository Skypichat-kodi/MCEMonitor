using System;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Ipc
{
    public class IpcCommandHandler
    {
        private readonly NetMonitorEngine _engine;
        private readonly NetMonitorSettings _settings;

        public IpcCommandHandler(NetMonitorEngine engine, NetMonitorSettings settings)
        {
            _engine = engine;
            _settings = settings;
        }

        public void Handle(string command, NamedPipeServerStream server)
        {
            // ═══════════════════════════════════════════════════════════
            //  Commandes *-b64 (payload JSON encodé en base64)
            //  Résout les injections via | et & dans les valeurs libres
            //  (messages, mots de passe, patterns…).
            //  ⚠ Doivent être testées AVANT les commandes legacy.
            // ═══════════════════════════════════════════════════════════
            if (command.StartsWith("block-ip-b64 ", StringComparison.OrdinalIgnoreCase))
            {
                HandleBlockIpB64(command, server);
                return;
            }

            if (command.StartsWith("set-web-config-b64 ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetWebConfigB64(command, server);
                return;
            }

            if (command.StartsWith("add-pattern-b64 ", StringComparison.OrdinalIgnoreCase))
            {
                HandleAddPatternB64(command, server);
                return;
            }

            if (command.StartsWith("set-manual-severity-b64 ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetManualSeverityB64(command, server);
                return;
            }

            if (command.StartsWith("toggle-whitelist-entry-b64 ", StringComparison.OrdinalIgnoreCase))
            {
                HandleToggleWhitelistEntryB64(command, server);
                return;
            }

            if (command.StartsWith("set-web-config ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetWebConfig(command, server);
                return;
            }

            if (command.StartsWith("block-ip ", StringComparison.OrdinalIgnoreCase))
            {
                HandleBlockIp(command, server);
                return;
            }

            if (command.StartsWith("unblock-ip ", StringComparison.OrdinalIgnoreCase))
            {
                HandleUnblockIp(command, server);
                return;
            }

            if (command.StartsWith("net.unblockip ", StringComparison.OrdinalIgnoreCase))
            {
                string ip = command.Substring("net.unblockip ".Length).Trim();
                if (string.IsNullOrWhiteSpace(ip))
                {
                    IpcResponse.Error(server, "IP manquante");
                }
                else
                {
                    // Utilise le moteur pour retirer l'IP de la liste interne
                    // ET resynchroniser les règles pare-feu (blocks groupés)
                    bool ok = _engine.UnblockIp(ip);
                    IpcResponse.Json(server, new { status = ok ? "ok" : "error", unblocked = ok, ip });
                }
                return;
            }
            
            if (command.StartsWith("whitelist-process ", StringComparison.OrdinalIgnoreCase))
            {
                string path = command.Substring("whitelist-process ".Length).Trim();
                bool ok = _engine.WhitelistProcess(path);
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", whitelisted = ok, path });
                return;
            }

            if (command.StartsWith("block-process ", StringComparison.OrdinalIgnoreCase))
            {
                string path = command.Substring("block-process ".Length).Trim();
                bool ok = _engine.BlockProcess(path, out string err);
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", blocked = ok, path, message = err });
                return;
            }

            if (command.StartsWith("unblock-process ", StringComparison.OrdinalIgnoreCase))
            {
                string path = command.Substring("unblock-process ".Length).Trim();
                bool ok = _engine.UnblockProcess(path);
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", unblocked = ok, path });
                return;
            }

            if (command.StartsWith("unwhitelist-process ", StringComparison.OrdinalIgnoreCase))
            {
                string path = command.Substring("unwhitelist-process ".Length).Trim();
                bool ok = _engine.RemoveWhitelistEntry(path);
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", unwhitelisted = ok, path });
                return;
            }

            if (command.StartsWith("toggle-whitelist-entry ", StringComparison.OrdinalIgnoreCase))
            {
                string payload = command.Substring("toggle-whitelist-entry ".Length).Trim();
                int sep = payload.LastIndexOf('|');
                if (sep <= 0)
                {
                    IpcResponse.Error(server, "format invalide");
                    return;
                }
                string path = payload.Substring(0, sep);
                string enabledStr = payload.Substring(sep + 1).Trim();
                bool enabled = enabledStr.Equals("true", StringComparison.OrdinalIgnoreCase);

                bool ok = _engine.SetWhitelistEntryEnabled(path, enabled);
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", toggled = ok, path, enabled });
                return;
            }
            
            if (command.StartsWith("dismiss-pending ", StringComparison.OrdinalIgnoreCase))
            {
                // On prend tout ce qui suit la commande, car un chemin peut contenir des espaces
                string path = command.Substring("dismiss-pending ".Length).Trim();
                
                if (string.IsNullOrWhiteSpace(path))
                {
                    IpcResponse.Error(server, "Chemin manquant");
                    return;
                }

                bool ok = _engine.DismissPendingProcess(path);
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", dismissed = ok, path });
                return;
            }            

            if (command.StartsWith("set-manual-severity ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetManualSeverity(server, command);
                return;
            }

            if (command.StartsWith("mark-safe ", StringComparison.OrdinalIgnoreCase))
            {
                string ip = command.Substring("mark-safe ".Length).Trim();
                bool ok = _engine.MarkSafe(ip);
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", ip, marked = ok });
                return;
            }
 
            if (command.StartsWith("add-pattern ", StringComparison.OrdinalIgnoreCase))
            {
                HandleAddPattern(command, server);
                return;
            } 
                              
            switch (command.ToLowerInvariant())
            {
                case "get-status":              HandleGetStatus(server); break;
                case "get-problems":            HandleGetProblems(server); break;
                case "get-alerts":              HandleGetAlerts(server); break;
                case "get-connections":         HandleGetConnections(server); break;
                case "get-config":              HandleGetConfig(server); break;
                case "clear-alerts":            HandleClearAlerts(server); break;
                case "reload-config":           HandleReloadConfig(server); break;
                case "force-scan":              HandleForceScan(server); break;
                case "shutdown":                HandleShutdown(server); break;
                case "get-web-status":          HandleGetWebStatus(server); break;
                case "get-active":              HandleGetActiveIntrusions(server); break;
                case "clear-active":            HandleClearActiveIntrusions(server); break;
                case "get-blocked":             HandleGetBlockedIps(server); break;
                case "get-audit":               HandleGetAudit(server); break;
                case "clear-audit":             HandleClearAudit(server); break;
                case "export-pdf":              HandleExportPdf(server); break;
                case "get-stats":               HandleGetStats(server); break;
                case "get-pending-processes":   HandleGetPendingProcesses(server); break;
                case "get-blocked-retries":     HandleGetBlockedIpRetries(server); break;
                case "clear-blocked-retries":   HandleClearBlockedIpRetries(server); break;
                case "get-whitelist":           HandleGetWhitelist(server); break;
                case "get-audit-grouped":       HandleGetAuditGrouped(server); break;
                case "get-persistent-problems": HandleGetPersistentProblems(server); break;
                case "restart-computer":        HandleRestartComputer(server); break;
                case "clear-all-blocked":       HandleClearAllBlocked(server); break;                                                                                           
                default:                        IpcResponse.Error(server, "unknown command"); break;
            }
        }

        /// <summary>
        /// Décode un payload base64 → JSON, et retourne un JsonDocument prêt à l'emploi.
        /// Retourne null en cas d'erreur (base64 invalide ou JSON mal formé).
        /// </summary>
        private static JsonDocument? TryDecodeB64Json(string base64Payload)
        {
            try
            {
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Payload));
                return JsonDocument.Parse(json);
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"[IPC-B64] Décodage échoué : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extrait une string d'un JsonElement (null si absent ou null JSON).
        /// </summary>
        private static string? GetJsonString(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Null) return null;
            return el.GetString();
        }

        /// <summary>
        /// Extrait un bool d'un JsonElement (défaut = false si absent).
        /// </summary>
        private static bool GetJsonBool(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var el)) return false;
            return el.ValueKind == JsonValueKind.True;
        }

        /// <summary>
        /// Extrait un int d'un JsonElement (0 si absent ou invalide).
        /// </summary>
        private static int GetJsonInt(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var el)) return 0;
            return el.TryGetInt32(out var val) ? val : 0;
        }

        private void HandleClearAllBlocked(NamedPipeServerStream server)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                bool ok = _engine.ClearAllBlockedIps();
                sw.Stop();
                CoreLog.Info($"[CLEAR-ALL] Terminé en {sw.ElapsedMilliseconds} ms");
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", cleared = ok, elapsedMs = sw.ElapsedMilliseconds });
            }
            catch (Exception ex)
            {
                sw.Stop();
                CoreLog.Error($"HandleClearAllBlocked ({sw.ElapsedMilliseconds} ms) : " + ex.Message);
                try { IpcResponse.Error(server, ex.Message); } catch { }
            }
        }
        
        private void HandleGetStatus(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, new
            {
                running = _engine.IsRunning,
                lastUpdate = _engine.LastUpdateTime,
                worstSeverity = _engine.WorstSeverity,
                alertCount = _engine.GetAlerts().Count,
                connectionCount = _engine.GetRecentConnections(1000).Count
            });
        }

        private void HandleGetProblems(NamedPipeServerStream server)
        {
            try
            {
                var problems = _engine.GetProblems(20);
                IpcResponse.Json(server, problems);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleGetAlerts(NamedPipeServerStream server)
        {
            try
            {
                var alerts = _engine.GetAlerts();
                var projected = alerts.Select(a => new
                {
                    timestamp      = a.Timestamp,
                    source         = a.Source,
                    severity       = a.Severity.ToString(),
                    message        = a.Message,
                    remoteIp       = a.RemoteIp,
                    remotePort     = a.RemotePort,
                    localPort      = a.LocalPort,
                    pid            = a.Pid,
                    processName    = a.ProcessName,
                    processPath    = a.ProcessPath,
                    trustReason    = a.TrustReason,
                    details        = a.Details,
                    isKnownScanner = a.IsKnownScanner,
                    country        = a.Country,
                    countryCode    = a.CountryCode,
                    isp            = a.Isp,
                    isOutbound     = a.IsOutbound,           
                }).ToList();

                IpcResponse.Json(server, projected);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(server, ex.Message);
            }
        }
        
        private void HandleGetConnections(NamedPipeServerStream server)
        {
            try
            {
                var conns = _engine.GetRecentConnections(300);

                var projected = conns.Select(c => new
                {
                    pid = c.Pid,
                    processName = c.ProcessName,
                    localIp = c.LocalIp,
                    localPort = c.LocalPort,
                    remoteIp = c.RemoteIp,
                    remotePort = c.RemotePort,
                    protocol = c.Protocol.ToString(),   // <-- enum ? string explicite
                    timestamp = c.Timestamp
                }).ToList();

                IpcResponse.Json(server, projected);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleGetConfig(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, new
            {
                portScanWindowSeconds    = _settings.PortScanWindowSeconds,
                portScanThreshold        = _settings.PortScanThreshold,
                beaconWindowSeconds      = _settings.BeaconWindowSeconds,
                beaconMinHits            = _settings.BeaconMinHits,
                beaconJitterTolerancePct = _settings.BeaconJitterTolerancePct,
                autoBlock                = _settings.AutoBlock,
                autoBlockCritical        = _settings.AutoBlockCritical,
                autoBlockWarning         = _settings.AutoBlockWarning,
                autoBlockSubnet          = _settings.AutoBlockSubnet,
                ignoredNetworks          = _settings.IgnoredNetworks,
                disableFilters           = _settings.DisableFilters,
                blacklistLevel           = _settings.BlacklistLevel,
                blacklistLastUpdate      = _settings.BlacklistLastUpdate,

                // Robots d'exploration
                googleIpRangeEnabled            = _settings.GoogleIpRangeEnabled,
                googleIpRangeRefreshHours       = _settings.GoogleIpRangeRefreshHours,
                microsoftIpRangeEnabled         = _settings.MicrosoftIpRangeEnabled,
                microsoftIpRangeRefreshHours    = _settings.MicrosoftIpRangeRefreshHours,                
                blockBots                       = _settings.BlockBots,
                blockCrawlers                   = _settings.BlockCrawlers,
                crawlerPatterns                 = _settings.CrawlerPatterns,

                // Scanners de recherche (Trusted)
                ignoreSearchScanners     = _settings.IgnoreSearchScanners,
                scannerPatterns          = _settings.ScannerPatterns,

                // Patterns de classification (manquants avant → data loss au Save)
                safePatterns             = _settings.SafePatterns,
                criticalPatterns         = _settings.CriticalPatterns,
                whitelistPatterns        = _settings.WhitelistPatterns,
            });
        }

        private void HandleClearAlerts(NamedPipeServerStream server)
        {
            try
            {
                _engine.ClearAlerts();
                CoreLog.Write("IPC : historique des alertes vidé");
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleClearAlerts : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleReloadConfig(NamedPipeServerStream server)
        {
            try
            {
                _engine.ReloadConfig();
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleReloadConfig : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleForceScan(NamedPipeServerStream server)
        {
            try
            {
                _engine.ForceScan();
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleForceScan : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleRestartComputer(NamedPipeServerStream server)
        {
            try
            {
                CoreLog.Info("IPC : redémarrage de l'ordinateur demandé par l'UI");

                var psi = new System.Diagnostics.ProcessStartInfo(
                    "shutdown.exe",
                    "/r /t 10 /c \"NetMonitor : redémarrage pour débloquer le pilote WinDivert\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                System.Diagnostics.Process.Start(psi);

                // On répond OK tout de suite : le shutdown est planifié avec un délai de 10 s.
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Error("Erreur HandleRestartComputer : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleShutdown(NamedPipeServerStream server)
        {
            IpcResponse.Ok(server);
            CoreLog.Write("IPC : shutdown demandé");

            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => Environment.Exit(0));
        }

        private void HandleGetWhitelist(NamedPipeServerStream server)
        {
            try
            {
                var list = _engine.GetWhitelistEntries();
                IpcResponse.Json(server, list);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }
        
        private void HandleGetWebStatus(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, new
            {
                enabled = _settings.WebEnabled,
                port = _settings.WebPort,
                username = _settings.WebUsername,
                password = _settings.WebPassword
            });
        }

        private void HandleSetWebConfig(string command, NamedPipeServerStream server)
        {
            try
            {
                string payload = command.Substring("set-web-config ".Length).Trim();
                var parts = payload.Split('&');

                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length != 2) continue;

                    string key = kv[0].Trim().ToLower();
                    string val = kv[1].Trim();

                    switch (key)
                    {
                        case "enabled":
                            if (bool.TryParse(val, out bool e)) _settings.WebEnabled = e;
                            break;
                        case "port":
                            if (int.TryParse(val, out int p)) _settings.WebPort = p;
                            break;
                        case "username":
                            _settings.WebUsername = val;
                            break;
                        case "password":
                            _settings.WebPassword = val;
                            break;
                    }
                }

                _settings.SaveWeb();
                Program.RestartWebServer();

                CoreLog.Write($"IPC : config WebServer mise à jour (enabled={_settings.WebEnabled}, port={_settings.WebPort})");
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleSetWebConfig : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleGetActiveIntrusions(NamedPipeServerStream server)
        {
            try
            {
                var list = _engine.GetActiveIntrusions();
                var projected = list.Select(a => new
                {
                    timestamp       = a.Timestamp,
                    source          = a.Source,
                    severity        = a.Severity.ToString(),
                    message         = a.Message,
                    remoteIp        = a.RemoteIp,
                    remotePort      = a.RemotePort,
                    localPort       = a.LocalPort,
                    pid             = a.Pid,
                    processName     = a.ProcessName,
                    processPath     = a.ProcessPath,
                    trustReason     = a.TrustReason,                     
                    details         = a.Details,
                    isKnownScanner  = a.IsKnownScanner,
                    country         = a.Country,
                    countryCode     = a.CountryCode,
                    isp             = a.Isp,
                    isOutbound      = a.IsOutbound,
                }).ToList();

                IpcResponse.Json(server, projected);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleClearActiveIntrusions(NamedPipeServerStream server)
        {
            try
            {
                _engine.ClearActiveIntrusions();
                IpcResponse.Ok(server);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleGetBlockedIps(NamedPipeServerStream server)
        {
            try
            {
                var list = _engine.GetBlockedIps();
                IpcResponse.Json(server, list);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleGetBlockedIpRetries(NamedPipeServerStream server)
        {
            try
            {
                var retries = _engine.GetBlockedIpRetries();

                // On joint avec les infos GeoIP persistées dans BlockedIpInfo
                var blockedDict = _engine.GetBlockedIps()
                    .ToDictionary(b => b.Ip, b => b, StringComparer.OrdinalIgnoreCase);

                var projected = retries.Select(r =>
                {
                    blockedDict.TryGetValue(r.Ip, out var info);
                    return new
                    {
                        ip                  = r.Ip,
                        originalDetector    = r.OriginalDetector,
                        blockedAt           = r.BlockedAt,
                        firstRetryAt        = r.FirstRetryAt,
                        lastRetryAt         = r.LastRetryAt,
                        retryCount          = r.RetryCount,
                        severity            = info?.Severity ?? "-",
                        country             = info?.Country,
                        countryCode         = info?.CountryCode,
                        isp                 = info?.Isp,
                        isOutbound          = info?.IsOutbound ?? false,
                    };
                }).ToList();

                IpcResponse.Json(server, projected);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleClearBlockedIpRetries(NamedPipeServerStream server)
        {
            try
            {
                _engine.ClearBlockedIpRetries();
                IpcResponse.Ok(server);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleBlockIp(string command, NamedPipeServerStream server)
        {
            try
            {
                string payload = command.Substring("block-ip ".Length).Trim();
                var parts = payload.Split('|', 5);   // ? 5 au lieu de 4

                string ip = parts[0].Trim();
                string? source   = parts.Length > 1 ? parts[1] : null;
                string? message  = parts.Length > 2 ? parts[2] : null;
                string? details  = parts.Length > 3 ? parts[3] : null;
                string? severity = parts.Length > 4 ? parts[4] : null;

                // L'IPC est déclenché uniquement par des actions humaines (UI WPF ou page web)
                // → tout blocage via IPC est par définition manuel, donc forceManual=true.
                bool ok = _engine.BlockIp(ip, out string error, source, message, details, severity,
                                          isAutomatic: false,
                                          forceManual: true);
                
                if (ok)
                    IpcResponse.Json(server, new { status = "ok", blocked = true, ip });
                else
                    IpcResponse.Json(server, new { status = "error", blocked = false, ip, message = error });
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Handlers *-b64 — payload JSON base64
        //  Chaque méthode décode le payload et délègue à la même logique que
        //  la version legacy, garantissant un comportement identique.
        // ═══════════════════════════════════════════════════════════════════════

        private void HandleBlockIpB64(string command, NamedPipeServerStream server)
        {
            try
            {
                string base64 = command.Substring("block-ip-b64 ".Length).Trim();
                using var doc = TryDecodeB64Json(base64);
                if (doc == null) { IpcResponse.Error(server, "payload b64 invalide"); return; }

                var root = doc.RootElement;
                string ip        = GetJsonString(root, "ip") ?? "";
                string? source   = GetJsonString(root, "source");
                string? message  = GetJsonString(root, "message");
                string? details  = GetJsonString(root, "details");
                string? severity = GetJsonString(root, "severity");

                // IPC = toujours action humaine → forceManual=true (comme la version legacy)
                bool ok = _engine.BlockIp(ip, out string error, source, message, details, severity,
                                          isAutomatic: false,
                                          forceManual: true);

                if (ok)
                    IpcResponse.Json(server, new { status = "ok", blocked = true, ip });
                else
                    IpcResponse.Json(server, new { status = "error", blocked = false, ip, message = error });
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleSetWebConfigB64(string command, NamedPipeServerStream server)
        {
            try
            {
                string base64 = command.Substring("set-web-config-b64 ".Length).Trim();
                using var doc = TryDecodeB64Json(base64);
                if (doc == null) { IpcResponse.Error(server, "payload b64 invalide"); return; }

                var root = doc.RootElement;
                bool enabled      = GetJsonBool(root, "enabled");
                int port          = GetJsonInt(root, "port");
                string username   = GetJsonString(root, "username") ?? "";
                string password   = GetJsonString(root, "password") ?? "";

                _settings.WebEnabled  = enabled;
                _settings.WebPort     = port;
                _settings.WebUsername = username;
                _settings.WebPassword = password;

                _settings.SaveWeb();
                Program.RestartWebServer();

                CoreLog.Write($"IPC : config WebServer mise à jour via b64 (enabled={enabled}, port={port})");
                IpcResponse.Ok(server);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleAddPatternB64(string command, NamedPipeServerStream server)
        {
            try
            {
                string base64 = command.Substring("add-pattern-b64 ".Length).Trim();
                using var doc = TryDecodeB64Json(base64);
                if (doc == null) { IpcResponse.Error(server, "payload b64 invalide"); return; }

                var root = doc.RootElement;
                string list  = GetJsonString(root, "list") ?? "";
                string value = GetJsonString(root, "value") ?? "";

                bool ok = _settings.AddPattern(list, value);

                if (!ok)
                {
                    IpcResponse.Json(server, new { status = "error", list, value, message = "liste inconnue ou valeur vide" });
                    return;
                }

                _engine.ReloadConfig();
                int reclassified = _engine.ReclassifyBlockedIpsByPattern(value);

                IpcResponse.Json(server, new
                {
                    status = "ok",
                    list,
                    value,
                    reclassified,
                    message = reclassified > 0
                        ? $"{reclassified} IP(s) bloquée(s) mise(s) à jour"
                        : "pattern ajouté (aucune IP existante affectée)"
                });
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleSetManualSeverityB64(string command, NamedPipeServerStream server)
        {
            try
            {
                string base64 = command.Substring("set-manual-severity-b64 ".Length).Trim();
                using var doc = TryDecodeB64Json(base64);
                if (doc == null) { IpcResponse.Error(server, "payload b64 invalide"); return; }

                var root = doc.RootElement;
                string ip = GetJsonString(root, "ip") ?? "";
                string? sevStr = GetJsonString(root, "severity");

                if (string.IsNullOrWhiteSpace(ip)) { IpcResponse.Error(server, "ip manquante"); return; }

                // "auto" ou null → effacer l'override
                if (string.IsNullOrWhiteSpace(sevStr) || sevStr.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = _engine.SetManualSeverity(ip, null);
                    IpcResponse.Json(server, new { status = ok ? "ok" : "error", ip, manualSeverity = (string?)null });
                    return;
                }

                if (!Enum.TryParse<Severity>(sevStr, ignoreCase: true, out var sev))
                {
                    IpcResponse.Error(server, "sévérité invalide");
                    return;
                }

                bool ok2 = _engine.SetManualSeverity(ip, sev);
                IpcResponse.Json(server, new { status = ok2 ? "ok" : "error", ip, manualSeverity = sev.ToString() });
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleToggleWhitelistEntryB64(string command, NamedPipeServerStream server)
        {
            try
            {
                string base64 = command.Substring("toggle-whitelist-entry-b64 ".Length).Trim();
                using var doc = TryDecodeB64Json(base64);
                if (doc == null) { IpcResponse.Error(server, "payload b64 invalide"); return; }

                var root = doc.RootElement;
                string path      = GetJsonString(root, "path") ?? "";
                bool enabled     = GetJsonBool(root, "enabled");

                bool ok = _engine.SetWhitelistEntryEnabled(path, enabled);
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", toggled = ok, path, enabled });
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleUnblockIp(string command, NamedPipeServerStream server)
        {
            try
            {
                string ip = command.Substring("unblock-ip ".Length).Trim();
                bool ok = _engine.UnblockIp(ip);
                IpcResponse.Json(server, new { status = ok ? "ok" : "error", unblocked = ok, ip });
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }
        
        private void HandleGetAudit(NamedPipeServerStream server)
        {
            try
            {
                var list = _engine.GetAuditHistory();
                var projected = list.Select(a => new
                {
                    timestamp       = a.Timestamp,
                    source          = a.Source,
                    severity        = a.Severity.ToString(),
                    message         = a.Message,
                    remoteIp        = a.RemoteIp,
                    remotePort      = a.RemotePort,
                    localPort       = a.LocalPort,
                    pid             = a.Pid,
                    processName     = a.ProcessName,
                    processPath     = a.ProcessPath,
                    trustReason     = a.TrustReason,                     
                    details         = a.Details,
                    isKnownScanner  = a.IsKnownScanner,
                    isOutbound      = a.IsOutbound,
                }).ToList();
                IpcResponse.Json(server, projected);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleGetAuditGrouped(NamedPipeServerStream server)
        {
            try
            {
                var list = _engine.GetAuditHistoryGrouped();

                // ⚠ Projection obligatoire : AuditGroupedEntry.Severity est un enum.
                // Sans cette projection, System.Text.Json le sérialise en entier (0..3),
                // ce qui casse la désérialisation côté UI (AuditGroupedDto.Severity est string).
                var projected = list.Select(a => new
                {
                    firstSeen      = a.FirstSeen,
                    lastSeen       = a.LastSeen,
                    count          = a.Count,
                    severity       = a.Severity.ToString(),
                    source         = a.Source,
                    remoteIp       = a.RemoteIp,
                    remotePort     = a.RemotePort,
                    localPort      = a.LocalPort,
                    pid            = a.Pid,
                    processName    = a.ProcessName,
                    processPath    = a.ProcessPath,
                    trustReason    = a.TrustReason,
                    details        = a.Details,
                    isKnownScanner = a.IsKnownScanner,
                    country        = a.Country,          
                    countryCode    = a.CountryCode,      
                    isp            = a.Isp,              
                    groupKey       = a.GroupKey,
                    isOutbound     = a.IsOutbound,          
                }).ToList();

                IpcResponse.Json(server, projected);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }

        private void HandleClearAudit(NamedPipeServerStream server)
        {
            try
            {
                _engine.ClearAudit();
                IpcResponse.Ok(server);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }
        
        private void HandleExportPdf(NamedPipeServerStream server)
        {
            try
            {
                var history = _engine.GetAlerts();
                var blocked = _engine.GetBlockedIps();
                var audit = _engine.GetAuditHistory();

                byte[] pdfBytes = PdfExporter.GenerateReport(
                    Environment.MachineName, history, blocked, audit);

                // On renvoie le PDF encodé en Base64 pour passer dans le pipe
                string base64 = Convert.ToBase64String(pdfBytes);
                IpcResponse.Raw(server, base64);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleExportPdf : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }
        
        private void HandleGetStats(NamedPipeServerStream server)
        {
            try
            {
                var stats = Task.Run(() => _engine.GetStatsAsync()).Result;
                IpcResponse.Json(server, stats);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }
        
        private void HandleGetPendingProcesses(NamedPipeServerStream server)
        {
            try
            {
                var list = _engine.GetPendingProcesses();
                var projected = list.Select(p => new
                {
                    processPath = p.ProcessPath,
                    processName = p.ProcessName,
                    trustReason = p.TrustReason,
                    remoteIp    = p.RemoteIp,
                    remotePort  = p.RemotePort,
                    pid         = p.Pid,
                    firstSeen   = p.FirstSeen,
                    lastSeen    = p.LastSeen,
                    count       = p.Count,
                    severity    = p.Severity.ToString(),
                    detector    = p.Detector
                }).ToList();
                IpcResponse.Json(server, projected);
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }
        
        private void HandleGetPersistentProblems(NamedPipeServerStream server)
        {
            try
            {
                IpcResponse.Json(server, _engine.GetPersistentProblems());
            }
            catch (Exception ex) { IpcResponse.Error(server, ex.Message); }
        }
        
        private void HandleSetManualSeverity(NamedPipeServerStream server, string command)
        {
            try
            {
                string payload = command.Substring("set-manual-severity ".Length).Trim();

                // Format attendu : "<ip>|<severity|auto>"
                int sep = payload.LastIndexOf('|');
                if (sep <= 0 || sep >= payload.Length - 1)
                {
                    IpcResponse.Error(server, "format invalide");
                    return;
                }

                string ip = payload.Substring(0, sep).Trim();
                string sevStr = payload.Substring(sep + 1).Trim();

                // Auto → efface l'override
                if (sevStr.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = _engine.SetManualSeverity(ip, null);
                    IpcResponse.Json(server, new
                    {
                        status = ok ? "ok" : "error",
                        ip,
                        manualSeverity = (string?)null
                    });
                    return;
                }

                // Sinon, on parse l'enum
                if (!Enum.TryParse<Severity>(sevStr, ignoreCase: true, out var sev))
                {
                    IpcResponse.Error(server, "sévérité invalide");
                    return;
                }

                bool ok2 = _engine.SetManualSeverity(ip, sev);
                IpcResponse.Json(server, new
                {
                    status = ok2 ? "ok" : "error",
                    ip,
                    manualSeverity = sev.ToString()
                });
            }
            catch (Exception ex)
            {
                CoreLog.Error("Erreur HandleSetManualSeverity : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }
        
        private void HandleAddPattern(string command, NamedPipeServerStream server)
        {
            try
            {
                string payload = command.Substring("add-pattern ".Length).Trim();
                int sep = payload.IndexOf('|');
                if (sep <= 0)
                {
                    IpcResponse.Error(server, "format invalide (attendu: liste|valeur)");
                    return;
                }

                string list = payload.Substring(0, sep).Trim();
                string value = payload.Substring(sep + 1).Trim();

                bool ok = _settings.AddPattern(list, value);

                if (!ok)
                {
                    IpcResponse.Json(server, new
                    {
                        status = "error",
                        list,
                        value,
                        message = "liste inconnue ou valeur vide"
                    });
                    return;
                }

                // 1 seul reload + 1 seule reclassification
                _engine.ReloadConfig();
                int reclassified = _engine.ReclassifyBlockedIpsByPattern(value);

                IpcResponse.Json(server, new
                {
                    status = "ok",
                    list,
                    value,
                    reclassified,
                    message = reclassified > 0
                        ? $"{reclassified} IP(s) bloquée(s) mise(s) à jour"
                        : "pattern ajouté (aucune IP existante affectée)"
                });
            }
            catch (Exception ex)
            {
                CoreLog.Error("Erreur HandleAddPattern : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }                       
    }
}