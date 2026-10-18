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

                var activeRows = new System.Text.StringBuilder();
                if (active.Count > 0)
                {
                    foreach (var a in active.OrderByDescending(x => x.Timestamp).Take(50))
                    {
                        string sevClass = a.Severity.ToString() == "Critical" ? "danger" :
                                          a.Severity.ToString() == "High" ? "danger" :
                                          a.Severity.ToString() == "Warning" ? "warning" : "ok";
                        activeRows.Append($@"<tr>
                            <td>{a.Timestamp:dd/MM HH:mm:ss}</td>
                            <td><span class='badge {sevClass}'>{a.Severity}</span></td>
                            <td>{WebUtility.HtmlEncode(a.Source)}</td>
                            <td>{WebUtility.HtmlEncode(a.RemoteIp ?? "")}</td>
                            <td>{WebUtility.HtmlEncode(a.Message)}</td>
                        </tr>");
                    }
                }
                else activeRows.Append("<tr><td colspan='5' style='text-align:center;color:#888;padding:20px'>Aucune intrusion active</td></tr>");

                var blockedRows = new System.Text.StringBuilder();
                if (blocked.Count > 0)
                {
                    foreach (var b in blocked.OrderByDescending(x => x.BlockedAt).Take(50))
                    {
                        blockedRows.Append($@"<tr>
                            <td>{b.BlockedAt.ToLocalTime():dd/MM HH:mm:ss}</td>
                            <td><b>{WebUtility.HtmlEncode(b.Ip)}</b></td>
                            <td>{WebUtility.HtmlEncode(b.Source)}</td>
                            <td>{WebUtility.HtmlEncode(b.Message)}</td>
                        </tr>");
                    }
                }
                else blockedRows.Append("<tr><td colspan='4' style='text-align:center;color:#888;padding:20px'>Aucune IP bloquée</td></tr>");

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
                else historyRows.Append("<tr><td colspan='5' style='text-align:center;color:#888;padding:20px'>Historique vide</td></tr>");

                var model = new Dictionary<string, object?>
                {
                    ["MachineName"] = Environment.MachineName,
                    ["LastUpdate"] = engine.LastUpdateTime == DateTime.MinValue ? "Jamais" : engine.LastUpdateTime.ToString("dd/MM/yyyy HH:mm:ss"),
                    ["ActiveRows"] = activeRows.ToString(),
                    ["BlockedRows"] = blockedRows.ToString(),
                    ["HistoryRows"] = historyRows.ToString()
                };

                string html = ViewRenderer.Render("NetPage.html", model);
                
                // Injection de la PeerBar (doit correspondre au commentaire HTML exact)
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