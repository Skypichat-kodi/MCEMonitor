using System;
using System.IO.Pipes;
using System.Linq;

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
            // NOUVEAU : Interception de la commande Web
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
                default:                        IpcResponse.Error(server, "unknown command"); break;
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
                    timestamp = a.Timestamp,
                    source = a.Source,
                    severity = a.Severity.ToString(),
                    message = a.Message,
                    remoteIp = a.RemoteIp,
                    remotePort = a.RemotePort,
                    localPort = a.LocalPort,
                    pid = a.Pid,
                    processName = a.ProcessName,
                    processPath = a.ProcessPath,
                    trustReason = a.TrustReason,                   
                    details = a.Details
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
                portScanWindowSeconds = _settings.PortScanWindowSeconds,
                portScanThreshold = _settings.PortScanThreshold,
                beaconWindowSeconds = _settings.BeaconWindowSeconds,
                beaconMinHits = _settings.BeaconMinHits,
                beaconJitterTolerancePct = _settings.BeaconJitterTolerancePct,
                autoBlock = _settings.AutoBlock,
                ignoredNetworks = _settings.IgnoredNetworks,
                disableFilters = _settings.DisableFilters,
                blacklistLevel = _settings.BlacklistLevel,              // NOUVEAU
                blacklistLastUpdate = _settings.BlacklistLastUpdate     // NOUVEAU
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

        private void HandleShutdown(NamedPipeServerStream server)
        {
            IpcResponse.Ok(server);
            CoreLog.Write("IPC : shutdown demandé");

            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => Environment.Exit(0));
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
                    timestamp = a.Timestamp,
                    source = a.Source,
                    severity = a.Severity.ToString(),
                    message = a.Message,
                    remoteIp = a.RemoteIp,
                    remotePort = a.RemotePort,
                    localPort = a.LocalPort,
                    pid = a.Pid,
                    processName = a.ProcessName,
                    processPath = a.ProcessPath,
                    trustReason = a.TrustReason,                     
                    details = a.Details
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

                bool ok = _engine.BlockIp(ip, out string error, source, message, details, severity);
                
                if (ok)
                    IpcResponse.Json(server, new { status = "ok", blocked = true, ip });
                else
                    IpcResponse.Json(server, new { status = "error", blocked = false, ip, message = error });
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
                    timestamp = a.Timestamp,
                    source = a.Source,
                    severity = a.Severity.ToString(),
                    message = a.Message,
                    remoteIp = a.RemoteIp,
                    remotePort = a.RemotePort,
                    localPort = a.LocalPort,
                    pid = a.Pid,
                    processName = a.ProcessName,
                    processPath = a.ProcessPath,
                    trustReason = a.TrustReason,                     
                    details = a.Details
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
    }
}