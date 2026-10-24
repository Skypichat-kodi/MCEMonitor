using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Web
{
    public static class WebHandler
    {
        public static string BuildNetPage(NetMonitorEngine engine, NetMonitorSettings settings, string publicHost = "localhost")
        {
            try
            {
                var active = engine.GetActiveIntrusions();
                var blocked = engine.GetBlockedIps();
                var history = engine.GetAlerts();
                var audit = engine.GetAuditHistory();

                // ⚠ lookupUrl doit être déclaré ici (utilisé partout en dessous)
                string lookupUrl = string.IsNullOrWhiteSpace(settings.IpLookupUrl)
                    ? "https://www.abuseipdb.com/check/{ip}"
                    : settings.IpLookupUrl;

                // --- Tableau 1 : Intrusions Actives (groupées par IP) ---
                var activeRows = new System.Text.StringBuilder();
                if (active.Count > 0)
                {
                    var grouped = active
                        .Where(a => !string.IsNullOrWhiteSpace(a.RemoteIp))
                        .GroupBy(a => a.RemoteIp!)
                        .Select(g =>
                        {
                            var mostSevere = g.OrderByDescending(x => (int)x.Severity).First();
                            var mostRecent = g.OrderByDescending(x => x.Timestamp).First();

                            string detectorList = string.Join(", ",
                                g.Select(x => x.Source).Distinct().OrderBy(x => x));

                            return new
                            {
                                Ip        = g.Key,
                                Count     = g.Count(),
                                Severity  = mostSevere.Severity,
                                Detectors = detectorList,
                                FirstSeen = g.Min(x => x.Timestamp),
                                LastSeen  = g.Max(x => x.Timestamp),
                                Message   = mostRecent.Message,
                                Pid         = mostRecent.Pid,
                                ProcessPath = mostRecent.ProcessPath,
                                TrustReason = mostRecent.TrustReason
                            };
                        })
                        .OrderByDescending(x => (int)x.Severity)
                        .ThenByDescending(x => x.Count)
                        .ThenByDescending(x => x.LastSeen)
                        .Take(50)
                        .ToList();

                    foreach (var item in grouped)
                    {
                        string sevClass = item.Severity.ToString() == "Critical" ? "danger" :
                                          item.Severity.ToString() == "High" ? "danger" :
                                          item.Severity.ToString() == "Warning" ? "warning" : "ok";

                        string ipEnc = WebUtility.HtmlEncode(item.Ip);

                        string countClass = item.Count >= 10 ? "c10" :
                                            item.Count >= 5  ? "c5"  :
                                            item.Count >= 2  ? "c2"  : "c1";

                        activeRows.Append($@"<tr class='selectable' onclick=""rowClick(this, 'active-sel')"">
                            <td style='width:30px;text-align:center'><input type='radio' name='active-sel' value='{ipEnc}'></td>
                            <td title='Premiere : {item.FirstSeen:dd/MM HH:mm:ss} | Derniere : {item.LastSeen:dd/MM HH:mm:ss}'>{item.LastSeen:dd/MM HH:mm:ss}</td>
                            <td><span class='badge {sevClass}'>{item.Severity}</span></td>
                            <td>{WebUtility.HtmlEncode(item.Detectors)}</td>
                            <td>{ipEnc}</td>
                            <td class='info-cell'>{BuildIpInfoLink(item.Ip, lookupUrl)}</td>
                            <td class='count-cell'><span class='count-badge {countClass}' title='{item.Count} alertes'>{item.Count}</span></td>
                            <td>{WebUtility.HtmlEncode(item.Message)}</td>
                        </tr>");
                    }

                    foreach (var a in active.Where(x => string.IsNullOrWhiteSpace(x.RemoteIp))
                                            .OrderByDescending(x => x.Timestamp).Take(20))
                    {
                        string sevClass = a.Severity.ToString() == "Critical" ? "danger" :
                                          a.Severity.ToString() == "High" ? "danger" :
                                          a.Severity.ToString() == "Warning" ? "warning" : "ok";

                        activeRows.Append($@"<tr>
                            <td></td>
                            <td>{a.Timestamp:dd/MM HH:mm:ss}</td>
                            <td><span class='badge {sevClass}'>{a.Severity}</span></td>
                            <td>{WebUtility.HtmlEncode(a.Source)}</td>
                            <td></td>
                            <td></td>
                            <td class='count-cell'><span class='count-badge c1'>-</span></td>
                            <td>{a.Pid}</td>
                            <td>{WebUtility.HtmlEncode(a.Message)}</td>
                        </tr>");
                    }
                }
                else
                {
                    activeRows.Append("<tr><td colspan='8' style='text-align:center;color:#888;padding:20px'>Aucune intrusion active</td></tr>");
                }

                // --- Tableau 1bis : Processus en attente d'examen ---
                var pendingRows = new System.Text.StringBuilder();
                var pending = engine.GetPendingProcesses();
                if (pending.Count > 0)
                {
                    foreach (var p in pending
                        .OrderByDescending(x => (int)x.Severity)
                        .ThenByDescending(x => x.Count)
                        .Take(100))
                    {
                        string sevClass = p.Severity.ToString() == "Critical" ? "danger" :
                                          p.Severity.ToString() == "High" ? "danger" :
                                          p.Severity.ToString() == "Warning" ? "warning" : "ok";

                        string pathEnc = WebUtility.HtmlEncode(p.ProcessPath);
                        string remote = $"{p.RemoteIp}:{p.RemotePort}";

                        pendingRows.Append($@"<tr class='selectable' onclick=""rowClick(this, 'pending-sel')"">
                            <td style='width:30px;text-align:center'><input type='radio' name='pending-sel' value='{pathEnc}'></td>
                            <td>{p.LastSeen.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td><span class='badge {sevClass}'>{p.Severity}</span></td>
                            <td>{WebUtility.HtmlEncode(p.Detector ?? "-")}</td>
                            <td>{p.Pid}</td>
                            <td>{WebUtility.HtmlEncode(p.ProcessName)}</td>
                            <td title='{pathEnc}'>{WebUtility.HtmlEncode(Truncate(p.ProcessPath, 60))}</td>
                            <td title='{WebUtility.HtmlEncode(p.TrustReason ?? "")}'>{WebUtility.HtmlEncode(Truncate(p.TrustReason, 40))}</td>
                            <td>{WebUtility.HtmlEncode(remote)}</td>
                            <td class='count-cell'><span class='count-badge {(p.Count >= 10 ? "c10" : p.Count >= 5 ? "c5" : p.Count >= 2 ? "c2" : "c1")}'>{p.Count}</span></td>
                        </tr>");
                    }
                }
                else
                {
                    pendingRows.Append("<tr><td colspan='10' style='text-align:center;color:#888;padding:20px'>Aucun processus en attente</td></tr>");
                }

                // --- Tableau 2 : IP Bloquées ---
                var blockedRows = new System.Text.StringBuilder();
                if (blocked.Count > 0)
                {
                    foreach (var b in blocked.OrderByDescending(x => x.BlockedAt).Take(50))
                    {
                        string ipEnc = WebUtility.HtmlEncode(b.Ip);

                        string sevClass = string.IsNullOrWhiteSpace(b.Severity) || b.Severity == "-"
                            ? "unknown"
                            : b.Severity == "Critical" ? "danger"
                            : b.Severity == "High"     ? "danger"
                            : b.Severity == "Warning"  ? "warning"
                            : "ok";

                        string typeLabel = b.IsProcess ? "Processus" : "IP";
                        string display = b.IsProcess ? (b.ProcessPath ?? b.Ip) : b.Ip;
                        string displayEnc = WebUtility.HtmlEncode(display);
                        string infoCell = b.IsProcess ? "" : BuildIpInfoLink(b.Ip, lookupUrl);

                        blockedRows.Append($@"<tr class='selectable' onclick=""rowClick(this, 'blocked-sel')"">
                            <td style='width:30px;text-align:center'><input type='radio' name='blocked-sel' value='{ipEnc}'></td>
                            <td>{b.BlockedAt.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td>{typeLabel}</td>
                            <td title='{displayEnc}'><b>{WebUtility.HtmlEncode(Truncate(display, 60))}</b></td>
                            <td class='info-cell'>{infoCell}</td>
                            <td><span class='badge {sevClass}'>{WebUtility.HtmlEncode(b.Severity ?? "-")}</span></td>
                            <td>{WebUtility.HtmlEncode(b.Source)}</td>
                            <td>{WebUtility.HtmlEncode(b.Message)}</td>
                        </tr>");
                    }
                }
                else blockedRows.Append("<tr><td colspan='7' style='text-align:center;color:#888;padding:20px'>Aucune IP bloquée</td></tr>");

                // --- Tableau 3 : Historique (enrichi) ---
                var historyRows = new System.Text.StringBuilder();
                if (history.Count > 0)
                {
                    foreach (var h in history.OrderByDescending(x => x.Timestamp).Take(100))
                    {
                        string sevClass = h.Severity.ToString() == "Critical" ? "danger" :
                                          h.Severity.ToString() == "High" ? "danger" :
                                          h.Severity.ToString() == "Warning" ? "warning" : "ok";

                        string detailsText = string.IsNullOrWhiteSpace(h.Details)
                            ? "-"
                            : (h.Details.Length > 120 ? h.Details.Substring(0, 117) + "…" : h.Details);

                        string detailsTitle = WebUtility.HtmlEncode(h.Details ?? "");

                        historyRows.Append($@"<tr>
                            <td>{h.Timestamp:dd/MM HH:mm:ss}</td>
                            <td><span class='badge {sevClass}'>{h.Severity}</span></td>
                            <td>{WebUtility.HtmlEncode(h.Source)}</td>
                            <td>{WebUtility.HtmlEncode(h.RemoteIp ?? "")}</td>
                            <td class='info-cell'>{BuildIpInfoLink(h.RemoteIp, lookupUrl)}</td>
                            <td>{h.RemotePort}</td>
                            <td>{h.LocalPort}</td>
                            <td>{h.Pid}</td>
                            <td>{WebUtility.HtmlEncode(h.ProcessName ?? "-")}</td>
                            <td title='{WebUtility.HtmlEncode(h.ProcessPath ?? "")}'>{WebUtility.HtmlEncode(Truncate(h.ProcessPath, 60))}</td>
                            <td title='{WebUtility.HtmlEncode(h.TrustReason ?? "")}'>{WebUtility.HtmlEncode(Truncate(h.TrustReason, 40))}</td>
                            <td title='{detailsTitle}'>{WebUtility.HtmlEncode(detailsText)}</td>
                        </tr>");
                    }
                }
                else
                {
                    historyRows.Append("<tr><td colspan='12' style='text-align:center;color:#888;padding:20px'>Historique vide</td></tr>");
                }

                // --- Tableau 4 : Audit (enrichi) ---
                var auditRows = new System.Text.StringBuilder();
                if (audit.Count > 0)
                {
                    foreach (var a in audit.OrderByDescending(x => x.Timestamp).Take(100))
                    {
                        string detailsText = string.IsNullOrWhiteSpace(a.Details)
                            ? "-"
                            : (a.Details.Length > 120 ? a.Details.Substring(0, 117) + "…" : a.Details);

                        string detailsTitle = WebUtility.HtmlEncode(a.Details ?? "");

                        auditRows.Append($@"<tr>
                            <td>{a.Timestamp:dd/MM HH:mm:ss}</td>
                            <td>{WebUtility.HtmlEncode(a.Source)}</td>
                            <td>{WebUtility.HtmlEncode(a.RemoteIp ?? "")}</td>
                            <td class='info-cell'>{BuildIpInfoLink(a.RemoteIp, lookupUrl)}</td>
                            <td>{a.RemotePort}</td>
                            <td>{a.LocalPort}</td>
                            <td>{a.Pid}</td>
                            <td>{WebUtility.HtmlEncode(a.ProcessName ?? "-")}</td>
                            <td title='{WebUtility.HtmlEncode(a.ProcessPath ?? "")}'>{WebUtility.HtmlEncode(Truncate(a.ProcessPath, 60))}</td>
                            <td title='{WebUtility.HtmlEncode(a.TrustReason ?? "")}'>{WebUtility.HtmlEncode(Truncate(a.TrustReason, 40))}</td>
                            <td title='{detailsTitle}'>{WebUtility.HtmlEncode(detailsText)}</td>
                        </tr>");
                    }
                }
                else
                {
                    auditRows.Append("<tr><td colspan='11' style='text-align:center;color:#888;padding:20px'>Aucune alerte ignorée</td></tr>");
                }

                // --- Modèle ---
                var model = new Dictionary<string, object?>
                {
                    ["MachineName"]  = Environment.MachineName,
                    ["LastUpdate"]   = engine.LastUpdateTime == DateTime.MinValue
                                        ? "Jamais"
                                        : engine.LastUpdateTime.ToString("dd/MM/yyyy HH:mm:ss"),
                    ["ActiveRows"]   = activeRows.ToString(),
                    ["BlockedRows"]  = blockedRows.ToString(),
                    ["HistoryRows"]  = historyRows.ToString(),
                    ["AuditCount"]   = audit.Count,
                    ["AuditRows"]    = auditRows.ToString(),
                    ["ActiveCount"]  = active.Count,
                    ["BlockedCount"] = blocked.Count,
                    ["PendingRows"]  = pendingRows.ToString(),
                    ["PendingCount"] = pending.Count                    
                };

                string html = ViewRenderer.Render("NetPage.html", model);
                html = html.Replace("<!--PEERBAR-->", PeerStatusService.BuildPeerBarHtml(publicHost));

                return html;
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebHandler ERROR : " + ex.Message);
                return $"<html><body><h2>Erreur : {WebUtility.HtmlEncode(ex.Message)}</h2></body></html>";
            }
        }

        /// <summary>
        /// Retourne le badge "i" cliquable qui ouvre la fiche AbuseIPDB de l'IP.
        /// Pure ASCII ("i" + CSS) pour éviter tout problème d'encodage.
        /// </summary>
        private static string BuildIpInfoLink(string? ip, string lookupUrlTemplate)
        {
            if (string.IsNullOrWhiteSpace(ip)) return "";

            string url = lookupUrlTemplate.Replace("{ip}", Uri.EscapeDataString(ip));
            string urlEnc = WebUtility.HtmlEncode(url);

            return $"<a href='{urlEnc}' target='_blank' rel='noopener noreferrer' " +
                   $"class='ip-info' title='Voir les informations sur cette IP (AbuseIPDB)'>i</a>";
        }

        // ============================================================
        //  Page Statistiques
        // ============================================================
        public static async System.Threading.Tasks.Task<string> BuildStatsPageAsync(
            NetMonitorEngine engine,
            NetMonitorSettings settings,
            string publicHost = "localhost")
        {
            try
            {
                var stats = await engine.GetStatsAsync();

                string worstClass = stats.WorstSeverity == "critical" ? "danger"
                                  : stats.WorstSeverity == "warning" ? "warning" : "ok";

                var model = new Dictionary<string, object?>
                {
                    ["MachineName"]       = Environment.MachineName,
                    ["GeneratedAt"]       = stats.GeneratedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                    ["TotalAlerts"]       = stats.TotalAlerts,
                    ["DistinctIps"]       = stats.DistinctIps,
                    ["DistinctCountries"] = stats.DistinctCountries,
                    ["WorstSeverity"]     = stats.WorstSeverity,
                    ["WorstClass"]        = worstClass,

                    ["ActiveTotal"]        = stats.Active.TotalAlerts,
                    ["ActiveIps"]          = stats.Active.DistinctIps,
                    ["ActiveCountries"]    = stats.Active.DistinctCountries,
                    ["ActiveIpRows"]       = BuildActiveIpRows(stats.Active.TopIps),
                    ["ActiveCountryRows"]  = BuildCountryRows(stats.Active.TopCountries),
                    ["ActiveDetectorRows"] = BuildDetectorRows(stats.Active.TopDetectors),
                    ["ActiveMapData"]      = System.Text.Json.JsonSerializer.Serialize(stats.Active.AlertsByCountryCode),

                    ["BlockedTotal"]        = stats.Blocked.TotalAlerts,
                    ["BlockedIps"]          = stats.Blocked.DistinctIps,
                    ["BlockedCountries"]    = stats.Blocked.DistinctCountries,
                    ["BlockedIpRows"]       = BuildBlockedIpRows(stats.Blocked.TopIps),
                    ["BlockedCountryRows"]  = BuildCountryRows(stats.Blocked.TopCountries),
                    ["BlockedDetectorRows"] = BuildDetectorRows(stats.Blocked.TopDetectors),
                    ["BlockedMapData"]      = System.Text.Json.JsonSerializer.Serialize(stats.Blocked.AlertsByCountryCode),
                    
                    // --- Analyses (graphiques) ---
                    ["TopLocalPortsJson"]        = System.Text.Json.JsonSerializer.Serialize(stats.TopLocalPorts),
                    ["TopRemotePortsJson"]       = System.Text.Json.JsonSerializer.Serialize(stats.TopRemotePorts),
                    ["ActivityByHourJson"]       = System.Text.Json.JsonSerializer.Serialize(stats.ActivityByHour),
                    ["ActivityByDayJson"]        = System.Text.Json.JsonSerializer.Serialize(stats.ActivityByDay),
                    ["TopProcessesJson"]         = System.Text.Json.JsonSerializer.Serialize(stats.TopProcesses),
                    ["TopDetectorsJson"]         = System.Text.Json.JsonSerializer.Serialize(stats.TopDetectors),
                    ["SeverityDistributionJson"] = System.Text.Json.JsonSerializer.Serialize(stats.SeverityDistribution)                    
                };

                string html = ViewRenderer.Render("Stats.html", model);
                html = html.Replace("<!--PEERBAR-->", PeerStatusService.BuildPeerBarHtml(publicHost));
                return html;
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebHandler.BuildStatsPageAsync ERROR : " + ex.Message);
                return $"<html><body><h2>Erreur : {WebUtility.HtmlEncode(ex.Message)}</h2></body></html>";
            }
        }

        private static string BuildActiveIpRows(IEnumerable<IpStats> list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var item in list)
            {
                string sevClass = item.MaxSeverity == "Critical" ? "danger" :
                                  item.MaxSeverity == "High" ? "danger" :
                                  item.MaxSeverity == "Warning" ? "warning" : "ok";

                string country = string.IsNullOrEmpty(item.CountryCode)
                    ? (item.Country ?? "-")
                    : $"{item.Country} ({item.CountryCode})";

                sb.Append($@"<tr>
                    <td><b>{WebUtility.HtmlEncode(item.Ip)}</b></td>
                    <td>{WebUtility.HtmlEncode(country)}</td>
                    <td><span class='badge {sevClass}'>{item.MaxSeverity}</span></td>
                    <td style='text-align:right'>{item.Count}</td>
                    <td>{item.LastSeen.ToLocalTime():dd/MM HH:mm}</td>
                </tr>");
            }
            if (!list.Any())
                sb.Append("<tr><td colspan='5' style='text-align:center;color:#888;padding:15px'>Aucune donnée</td></tr>");
            return sb.ToString();
        }

        private static string BuildBlockedIpRows(IEnumerable<IpStats> list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var item in list)
            {
                string country = string.IsNullOrEmpty(item.CountryCode)
                    ? (item.Country ?? "-")
                    : $"{item.Country} ({item.CountryCode})";

                string detector = item.Detectors != null && item.Detectors.Count > 0
                    ? string.Join(", ", item.Detectors)
                    : "-";

                sb.Append($@"<tr>
                    <td><b>{WebUtility.HtmlEncode(item.Ip)}</b></td>
                    <td>{WebUtility.HtmlEncode(country)}</td>
                    <td>{WebUtility.HtmlEncode(item.Isp ?? "-")}</td>
                    <td>{WebUtility.HtmlEncode(detector)}</td>
                    <td>{item.LastSeen.ToLocalTime():dd/MM HH:mm}</td>
                </tr>");
            }
            if (!list.Any())
                sb.Append("<tr><td colspan='5' style='text-align:center;color:#888;padding:15px'>Aucune donnée</td></tr>");
            return sb.ToString();
        }

        private static string BuildCountryRows(IEnumerable<CountryStats> list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in list)
            {
                string code = string.IsNullOrEmpty(c.CountryCode) ? "" : $" ({c.CountryCode})";
                sb.Append($@"<tr>
                    <td>{WebUtility.HtmlEncode(c.Country)}{code}</td>
                    <td style='text-align:right'>{c.Count}</td>
                    <td style='text-align:right'>{c.DistinctIps}</td>
                </tr>");
            }
            if (!list.Any())
                sb.Append("<tr><td colspan='3' style='text-align:center;color:#888;padding:15px'>Aucune donnée</td></tr>");
            return sb.ToString();
        }

        private static string BuildDetectorRows(IEnumerable<DetectorStats> list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var d in list)
            {
                sb.Append($@"<tr>
                    <td>{WebUtility.HtmlEncode(d.Detector)}</td>
                    <td style='text-align:right'>{d.Count}</td>
                </tr>");
            }
            if (!list.Any())
                sb.Append("<tr><td colspan='2' style='text-align:center;color:#888;padding:15px'>Aucune donnée</td></tr>");
            return sb.ToString();
        }

        public static async System.Threading.Tasks.Task<string> BuildMapPageAsync(
            NetMonitorEngine engine,
            NetMonitorSettings settings)
        {
            try
            {
                var stats = await engine.GetStatsAsync();

                var model = new Dictionary<string, object?>
                {
                    ["ActiveMapData"]  = System.Text.Json.JsonSerializer.Serialize(stats.Active.AlertsByCountryCode),
                    ["BlockedMapData"] = System.Text.Json.JsonSerializer.Serialize(stats.Blocked.AlertsByCountryCode)
                };

                return ViewRenderer.Render("Map.html", model);
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebHandler.BuildMapPageAsync ERROR : " + ex.Message);
                return $"<html><body><h2 style='color:#FF6347;font-family:sans-serif'>Erreur : {WebUtility.HtmlEncode(ex.Message)}</h2></body></html>";
            }
        }
        
        public static async System.Threading.Tasks.Task<string> BuildChartsPageAsync(
            NetMonitorEngine engine,
            NetMonitorSettings settings)
        {
            try
            {
                var stats = await engine.GetStatsAsync();

                var model = new Dictionary<string, object?>
                {
                    ["TopLocalPortsJson"]        = System.Text.Json.JsonSerializer.Serialize(stats.TopLocalPorts),
                    ["TopRemotePortsJson"]       = System.Text.Json.JsonSerializer.Serialize(stats.TopRemotePorts),
                    ["ActivityByHourJson"]       = System.Text.Json.JsonSerializer.Serialize(stats.ActivityByHour),
                    ["ActivityByDayJson"]        = System.Text.Json.JsonSerializer.Serialize(stats.ActivityByDay),
                    ["TopDetectorsJson"]         = System.Text.Json.JsonSerializer.Serialize(stats.TopDetectors),
                    ["SeverityDistributionJson"] = System.Text.Json.JsonSerializer.Serialize(stats.SeverityDistribution)
                };

                return ViewRenderer.Render("Charts.html", model);
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebHandler.BuildChartsPageAsync ERROR : " + ex.Message);
                return $"<html><body><h2 style='color:#FF6347;font-family:sans-serif'>Erreur : {WebUtility.HtmlEncode(ex.Message)}</h2></body></html>";
            }
        }

        private static string Truncate(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "-";
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }                
    }
}