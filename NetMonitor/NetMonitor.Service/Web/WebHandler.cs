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
                        string sevClass = SeverityToCssClass(item.Severity.ToString());

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
                        string sevClass = SeverityToCssClass(a.Severity.ToString());

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
                        string sevClass = SeverityToCssClass(p.Severity.ToString());

                        string pathEnc = WebUtility.HtmlEncode(p.ProcessPath);
                        string remote = $"{p.RemoteIp}:{p.RemotePort}";

                        pendingRows.Append($@"<tr class='selectable' onclick=""rowClick(this, 'pending-sel')"">
                            <td style='width:30px;text-align:center'><input type='radio' name='pending-sel' value='{pathEnc}'></td>
                            <td>{p.LastSeen.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td><span class='badge {sevClass}'>{p.Severity}</span></td>
                            <td>{WebUtility.HtmlEncode(p.Detector ?? "-")}</td>
                            <td>{p.Pid}</td>
                            <td>{WebUtility.HtmlEncode(p.ProcessName)}</td>
                            <td class='info-cell'>{BuildProcessInfoLink(p.ProcessName)}</td>
                            <td title='{pathEnc}'>{WebUtility.HtmlEncode(Truncate(p.ProcessPath, 60))}</td>
                            <td title='{WebUtility.HtmlEncode(p.TrustReason ?? "")}'>{WebUtility.HtmlEncode(Truncate(p.TrustReason, 40))}</td>
                            <td>{WebUtility.HtmlEncode(remote)}</td>
                            <td class='count-cell'><span class='count-badge {(p.Count >= 10 ? "c10" : p.Count >= 5 ? "c5" : p.Count >= 2 ? "c2" : "c1")}'>{p.Count}</span></td>
                        </tr>");
                    }
                }
                else
                {
                    pendingRows.Append("<tr><td colspan='11' style='text-align:center;color:#888;padding:20px'>Aucun processus en attente</td></tr>");
                }

                // --- Tableau 2 : IP Bloquées ---
                var blockedRows = new System.Text.StringBuilder();
                if (blocked.Count > 0)
                {
                    foreach (var b in blocked.OrderByDescending(x => x.BlockedAt).Take(50))
                    {
                        string ipEnc = WebUtility.HtmlEncode(b.Ip);

                        string sevClass = SeverityToCssClass(b.Severity);

                        string typeLabel = b.IsProcess ? "Processus" : "IP";
                        string display = b.IsProcess ? (b.ProcessPath ?? b.Ip) : b.Ip;
                        string displayEnc = WebUtility.HtmlEncode(display);
                        string infoCell = b.IsProcess ? "" : BuildIpInfoLink(b.Ip, lookupUrl);

                        // ⚠ 1. Déclarations AVANT le Append
                        string manualLock = b.ManualSeverity.HasValue
                            ? "<span class='manual-lock' title='Sévérité forcée manuellement" +
                              (b.ManualSeveritySetAt.HasValue
                                  ? $" le {b.ManualSeveritySetAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}"
                                  : "") +
                              "'>🔒</span>"
                            : "";

                        string sevButton = b.IsProcess
                            ? ""
                            : $"<button type='button' class='severity-btn' " +
                              $"onclick=\"event.stopPropagation(); changeSeverity('{ipEnc}', " +
                              $"'{WebUtility.HtmlEncode(b.Severity)}', " +
                              $"{(b.ManualSeverity.HasValue ? "true" : "false")})\" " +
                              $"title='Changer la sévérité'>⚙</button>";

                        // ⚠ 2. Append APRÈS les déclarations
                        blockedRows.Append($@"<tr class='selectable' onclick=""rowClick(this, 'blocked-sel')"">
                            <td style='width:30px;text-align:center'><input type='radio' name='blocked-sel' value='{ipEnc}'></td>
                            <td>{b.BlockedAt.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td>{typeLabel}</td>
                            <td title='{displayEnc}'><b>{WebUtility.HtmlEncode(Truncate(display, 60))}</b></td>
                            <td class='info-cell'>{infoCell}</td>
                            <td><span class='badge {sevClass}'>{WebUtility.HtmlEncode(b.Severity ?? "-")}</span>{manualLock}{sevButton}</td>
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
                        string sevClass = SeverityToCssClass(h.Severity.ToString());

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
                            <td>{WebUtility.HtmlEncode(h.ProcessName ?? "-")}</td>
                            <td title='{detailsTitle}'>{WebUtility.HtmlEncode(detailsText)}</td>
                        </tr>");
                    }
                }
                else
                {
                    historyRows.Append("<tr><td colspan='9' style='text-align:center;color:#888;padding:20px'>Historique vide</td></tr>");
                }

                // --- Tableau 4 : Audit (groupé) ---
                var auditRows = new System.Text.StringBuilder();
                var auditGrouped = engine.GetAuditHistoryGrouped()
                    .OrderByDescending(a => a.Count)
                    .ThenByDescending(a => a.LastSeen)
                    .ToList();

                if (auditGrouped.Count > 0)
                {
                    foreach (var a in auditGrouped.Take(200))
                    {
                        string sevClass = SeverityToCssClass(a.Severity.ToString());

                        string countClass = a.Count >= 10 ? "c10" :
                                            a.Count >= 5  ? "c5"  :
                                            a.Count >= 2  ? "c2"  : "c1";

                        string detailsText = string.IsNullOrWhiteSpace(a.Details)
                            ? "-"
                            : (a.Details.Length > 120 ? a.Details.Substring(0, 117) + "…" : a.Details);
                        string detailsTitle = WebUtility.HtmlEncode(a.Details ?? "");

                        auditRows.Append($@"<tr>
                            <td>{a.FirstSeen.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td>{a.LastSeen.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td class='count-cell'><span class='count-badge {countClass}' title='{a.Count} occurrence(s)'>{a.Count}</span></td>
                            <td><span class='badge {sevClass}'>{a.Severity}</span></td>
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
                    auditRows.Append("<tr><td colspan='14' style='text-align:center;color:#888;padding:20px'>Aucune alerte ignorée</td></tr>");
                }

                // --- Tableau 5 : tentatives sur IP bloquée ---
                var blockedRetriesRows = new System.Text.StringBuilder();
                var blockedRetries = engine.GetBlockedIpRetries();
                if (blockedRetries.Count > 0)
                {
                    foreach (var r in blockedRetries.Take(100))
                    {
                        string ipEnc = WebUtility.HtmlEncode(r.Ip);
                        string countClass = r.RetryCount >= 10 ? "c10" :
                                            r.RetryCount >= 5  ? "c5"  :
                                            r.RetryCount >= 2  ? "c2"  : "c1";

                        blockedRetriesRows.Append($@"<tr>
                            <td><b>{ipEnc}</b></td>
                            <td class='info-cell'>{BuildIpInfoLink(r.Ip, lookupUrl)}</td>
                            <td>{WebUtility.HtmlEncode(r.OriginalDetector ?? "-")}</td>
                            <td>{r.BlockedAt.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td>{r.FirstRetryAt.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td>{r.LastRetryAt.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td class='count-cell'><span class='count-badge {countClass}' title='{r.RetryCount} tentatives'>{r.RetryCount}</span></td>
                        </tr>");
                    }
                }
                else
                {
                    blockedRetriesRows.Append(
                        "<tr><td colspan='7' style='text-align:center;color:#888;padding:20px'>" +
                        "Aucune tentative détectée sur les IP bloquées</td></tr>");
                }

                // --- Modèle ---
                var model = new Dictionary<string, object?>
                {
                    ["MachineName"]         = Environment.MachineName,
                    ["LastUpdate"]          = engine.LastUpdateTime == DateTime.MinValue ? "Jamais" : engine.LastUpdateTime.ToString("dd/MM/yyyy HH:mm:ss"),
                    ["ActiveRows"]          = activeRows.ToString(),
                    ["BlockedRows"]         = blockedRows.ToString(),
                    ["HistoryRows"]         = historyRows.ToString(),
                    ["AuditCount"]          = audit.Count,
                    ["AuditRows"]           = auditRows.ToString(),
                    ["ActiveCount"]         = active.Count,
                    ["BlockedCount"]        = blocked.Count,
                    ["PendingRows"]         = pendingRows.ToString(),
                    ["PendingCount"]        = pending.Count,
                    ["BlockedRetriesRows"]  = blockedRetriesRows.ToString(),
                    ["BlockedRetriesCount"] = blockedRetries.Count                                        
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

        /// <summary>
        /// Retourne le badge "i" cliquable qui ouvre la recherche du processus.
        /// L'URL par défaut est Google ; si tu as une propriété settings.ProcessLookupUrl,
        /// remplace la valeur par défaut ci-dessous.
        /// </summary>
        private static string BuildProcessInfoLink(
            string? processName,
            string lookupUrlTemplate = "https://www.google.com/search?q={name}")
        {
            if (string.IsNullOrWhiteSpace(processName)) return "";

            string url = lookupUrlTemplate.Replace("{name}", Uri.EscapeDataString(processName));
            string urlEnc = WebUtility.HtmlEncode(url);

            return $"<a href='{urlEnc}' target='_blank' rel='noopener noreferrer' " +
                   $"class='ip-info' title='Rechercher ce processus sur le web'>i</a>";
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
                string sevClass = SeverityToCssClass(item.MaxSeverity);

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

        public static async Task<string> BuildMapPageAsync(
            NetMonitorEngine engine,
            NetMonitorSettings settings,
            bool showBlockedOnly = false)
        {
            try
            {
                var stats = await engine.GetStatsAsync();

                var model = new Dictionary<string, object?>
                {
                    ["MachineName"]     = Environment.MachineName,
                    ["MapMode"]         = showBlockedOnly ? "blocked" : "active",
                    ["ActiveMapData"]   = System.Text.Json.JsonSerializer.Serialize(stats.Active.AlertsByCountryCode),
                    ["BlockedMapData"]  = System.Text.Json.JsonSerializer.Serialize(stats.Blocked.AlertsByCountryCode)
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

        public static string BuildWhitelistPage(NetMonitorEngine engine, NetMonitorSettings settings, string publicHost = "localhost")
        {
            try
            {
                var entries = engine.GetWhitelistEntries()
                    .OrderBy(e => e.Name)
                    .ThenBy(e => e.Path)
                    .ToList();

                var rows = new System.Text.StringBuilder();
                if (entries.Count > 0)
                {
                    foreach (var e in entries)
                    {
                        string pathEnc = WebUtility.HtmlEncode(e.Path);
                        string nameEnc = WebUtility.HtmlEncode(e.Name);
                        string publisherEnc = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(e.Publisher) ? "-" : e.Publisher);
                        string shaDisplay = string.IsNullOrWhiteSpace(e.Sha256)
                            ? "-"
                            : (e.Sha256.Length > 12 ? e.Sha256.Substring(0, 12) + "…" : e.Sha256);
                        string shaEnc = WebUtility.HtmlEncode(shaDisplay);
                        string shaTitle = WebUtility.HtmlEncode(e.Sha256 ?? "");
                        string enabledAttr = e.Enabled ? "checked" : "";

                        rows.Append($@"<tr>
                            <td>{nameEnc}</td>
                            <td title='{pathEnc}'>{WebUtility.HtmlEncode(Truncate(e.Path, 60))}</td>
                            <td style='text-align:center'>
                                <input type='checkbox' {enabledAttr}
                                       onchange=""toggleWhitelist(this, '{WebUtility.HtmlEncode(e.Path.Replace("'", "\\'"))}')"">
                            </td>
                            <td title='{publisherEnc}'>{publisherEnc}</td>
                            <td title='{shaTitle}' style='font-family:Consolas,monospace;font-size:11px'>{shaEnc}</td>
                            <td style='text-align:center'>
                                <button type='button' class='action-btn danger' style='padding:2px 8px'
                                        onclick=""removeWhitelist('{WebUtility.HtmlEncode(e.Path.Replace("'", "\\'"))}')"">🗑</button>
                            </td>
                        </tr>");
                    }
                }
                else
                {
                    rows.Append("<tr><td colspan='6' style='text-align:center;color:#888;padding:20px'>Aucune entrée dans la liste blanche</td></tr>");
                }

                var model = new Dictionary<string, object?>
                {
                    ["MachineName"]  = Environment.MachineName,
                    ["LastUpdate"]   = engine.LastUpdateTime == DateTime.MinValue ? "Jamais" : engine.LastUpdateTime.ToString("dd/MM/yyyy HH:mm:ss"),
                    ["WhitelistRows"] = rows.ToString(),
                    ["WhitelistCount"] = entries.Count,
                    ["ActiveCount"]   = entries.Count(e => e.Enabled)
                };

                string html = ViewRenderer.Render("WhitelistPage.html", model);
                html = html.Replace("<!--PEERBAR-->", PeerStatusService.BuildPeerBarHtml(publicHost));
                return html;
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebHandler.BuildWhitelistPage ERROR : " + ex.Message);
                return $"<html><body><h2>Erreur : {WebUtility.HtmlEncode(ex.Message)}</h2></body></html>";
            }
        }

        private static string Truncate(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "-";
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }
        
        /// <summary>
        /// Mappe une sévérité (enum stringifiée) vers sa classe CSS.
        /// Accepte les 5 nouveaux niveaux + les valeurs legacy (High, Info, "-").
        /// </summary>
        private static string SeverityToCssClass(string? severity) => severity switch
        {
            "Critical"    => "critical",
            "High"        => "critical",    // legacy : même couleur que Critical
            "Warning"     => "warning",
            "Info"        => "info",
            "Safe"        => "safe",
            "Trusted"     => "trusted",
            "Whitelisted" => "whitelisted",
            _             => "unknown"       // null, "", "-"
        };                        
    }
}