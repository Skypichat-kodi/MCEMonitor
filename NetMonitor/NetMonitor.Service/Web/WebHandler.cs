using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

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

                // --- Tableau 1 : Intrusions Actives ---
                var activeRows = new System.Text.StringBuilder();
                if (active.Count > 0)
                {
                    foreach (var a in active.OrderByDescending(x => x.Timestamp).Take(50))
                    {
                        string sevClass = a.Severity.ToString() == "Critical" ? "danger" :
                                          a.Severity.ToString() == "High" ? "danger" :
                                          a.Severity.ToString() == "Warning" ? "warning" : "ok";

                        bool hasIp = !string.IsNullOrWhiteSpace(a.RemoteIp);
                        string ipEnc = WebUtility.HtmlEncode(a.RemoteIp ?? "");

                        string radioCell = hasIp
                            ? $"<td style='width:30px;text-align:center'><input type='radio' name='active-sel' value='{ipEnc}'></td>"
                            : "<td></td>";

                        string trAttrs = hasIp
                            ? "class='selectable' onclick=\"rowClick(this, 'active-sel')\""
                            : "";

                        activeRows.Append($@"<tr {trAttrs}>
                            {radioCell}
                            <td>{a.Timestamp:dd/MM HH:mm:ss}</td>
                            <td><span class='badge {sevClass}'>{a.Severity}</span></td>
                            <td>{WebUtility.HtmlEncode(a.Source)}</td>
                            <td>{ipEnc}</td>
                            <td>{WebUtility.HtmlEncode(a.Message)}</td>
                        </tr>");
                    }
                }
                else activeRows.Append("<tr><td colspan='6' style='text-align:center;color:#888;padding:20px'>Aucune intrusion active</td></tr>");

                // --- Tableau 2 : IP Bloquées ---
                var blockedRows = new System.Text.StringBuilder();
                if (blocked.Count > 0)
                {
                    foreach (var b in blocked.OrderByDescending(x => x.BlockedAt).Take(50))
                    {
                        string ipEnc = WebUtility.HtmlEncode(b.Ip);

                        blockedRows.Append($@"<tr class='selectable' onclick=""rowClick(this, 'blocked-sel')"">
                            <td style='width:30px;text-align:center'><input type='radio' name='blocked-sel' value='{ipEnc}'></td>
                            <td>{b.BlockedAt.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td><b>{ipEnc}</b></td>
                            <td>{WebUtility.HtmlEncode(b.Source)}</td>
                            <td>{WebUtility.HtmlEncode(b.Message)}</td>
                        </tr>");
                    }
                }
                else blockedRows.Append("<tr><td colspan='5' style='text-align:center;color:#888;padding:20px'>Aucune IP bloquée</td></tr>");

                // --- Tableau 3 : Historique ---
                var historyRows = new System.Text.StringBuilder();
                if (history.Count > 0)
                {
                    foreach (var h in history.OrderByDescending(x => x.Timestamp).Take(100))
                    {
                        string sevClass = h.Severity.ToString() == "Critical" ? "danger" :
                                          h.Severity.ToString() == "High" ? "danger" :
                                          h.Severity.ToString() == "Warning" ? "warning" : "ok";
                        historyRows.Append($@"<tr>
                            <td>{h.Timestamp:dd/MM HH:mm:ss}</td>
                            <td><span class='badge {sevClass}'>{h.Severity}</span></td>
                            <td>{WebUtility.HtmlEncode(h.Source)}</td>
                            <td>{WebUtility.HtmlEncode(h.RemoteIp ?? "")}</td>
                            <td>{WebUtility.HtmlEncode(h.Message)}</td>
                        </tr>");
                    }
                }
                else
                {
                    historyRows.Append("<tr><td colspan='5' style='text-align:center;color:#888;padding:20px'>Historique vide</td></tr>");
                }

                // --- Tableau 4 : Audit Whitelist ---
                var auditRows = new System.Text.StringBuilder();
                if (audit.Count > 0)
                {
                    foreach (var a in audit.OrderByDescending(x => x.Timestamp).Take(100))
                    {
                        auditRows.Append($@"<tr>
                            <td>{a.Timestamp:dd/MM HH:mm:ss}</td>
                            <td>{WebUtility.HtmlEncode(a.Source)}</td>
                            <td>{WebUtility.HtmlEncode(a.RemoteIp ?? "")}</td>
                            <td style='color:#888;'>{WebUtility.HtmlEncode(a.Message)}</td>
                        </tr>");
                    }
                }
                else
                {
                    auditRows.Append("<tr><td colspan='4' style='text-align:center;color:#888;padding:20px'>Aucune alerte ignorée</td></tr>");
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
                    ["BlockedCount"] = blocked.Count
                };

                string html = ViewRenderer.Render("NetPage.html", model);

                // Injection de la PeerBar
                html = html.Replace("<!--PEERBAR-->", PeerStatusService.BuildPeerBarHtml(publicHost));

                return html;
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebHandler ERROR : " + ex.Message);
                return $"<html><body><h2>Erreur : {WebUtility.HtmlEncode(ex.Message)}</h2></body></html>";
            }
        }
    }
}