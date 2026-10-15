using System;
using System.Globalization;
using System.Linq;
using System.Net;

namespace NetMonitor.Service.Web
{
    public static class WebHandler
    {
        public static string BuildNetPage(NetMonitorEngine engine, NetMonitorSettings settings)
        {
            try
            {
                var alerts = engine.GetAlerts();
                var conns = engine.GetRecentConnections(100);

                var alertRows = new System.Text.StringBuilder();
                if (alerts.Count > 0)
                {
                    foreach (var a in alerts.OrderByDescending(x => x.Timestamp).Take(50))
                    {
                        string sevClass = a.Severity.ToString() == "Critical" ? "danger"
                                        : a.Severity.ToString() == "High" ? "danger"
                                        : a.Severity.ToString() == "Warning" ? "warning"
                                        : "ok";

                        alertRows.Append($@"
                            <tr>
                                <td>{a.Timestamp:dd/MM HH:mm:ss}</td>
                                <td><span class='badge {sevClass}'>{WebUtility.HtmlEncode(a.Severity.ToString())}</span></td>
                                <td>{WebUtility.HtmlEncode(a.Source)}</td>
                                <td>{WebUtility.HtmlEncode(a.Message)}</td>
                            </tr>");
                    }
                }
                else
                {
                    alertRows.Append("<tr><td colspan='4' style='text-align:center;color:#888;padding:20px'>Aucune intrusion détectée</td></tr>");
                }

                var connRows = new System.Text.StringBuilder();
                if (conns.Count > 0)
                {
                    foreach (var c in conns.Take(50))
                    {
                        connRows.Append($@"
                            <tr>
                                <td>{c.Timestamp:HH:mm:ss}</td>
                                <td>{WebUtility.HtmlEncode(c.Protocol.ToString())}</td>
                                <td>{WebUtility.HtmlEncode(c.LocalIp)}:{c.LocalPort}</td>
                                <td>{WebUtility.HtmlEncode(c.RemoteIp)}:{c.RemotePort}</td>
                                <td>{c.Pid}</td>
                                <td>{WebUtility.HtmlEncode(c.ProcessName ?? "")}</td>
                            </tr>");
                    }
                }
                else
                {
                    connRows.Append("<tr><td colspan='6' style='text-align:center;color:#888;padding:20px'>Aucune connexion récente</td></tr>");
                }

                string statusColor = engine.IsRunning ? "#6CCB5F" : "#FF6347";
                string statusText = engine.IsRunning ? "Surveillance active" : "Surveillance arrêtée";

                return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>NetMonitor - {Environment.MachineName}</title>
    <meta http-equiv='refresh' content='10'>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background:#1E1E1E; color:#E5E5E5; margin:0; padding:20px; }}
        h1 {{ color:#4CC2FF; margin:0 0 5px 0; }}
        .subtitle {{ color:#888; margin-bottom:20px; font-size:13px; }}
        .status {{ display:inline-block; padding:4px 12px; border-radius:12px; background:{statusColor}; color:#1E1E1E; font-weight:bold; font-size:12px; margin-bottom:20px; }}
        h2 {{ color:#4CC2FF; font-size:16px; margin-top:30px; border-bottom:1px solid #3C3C3C; padding-bottom:5px; }}
        table {{ width:100%; border-collapse:collapse; font-size:12px; margin-top:10px; }}
        th {{ background:#2D2D30; color:#FFF; padding:8px; text-align:left; border:1px solid #3C3C3C; }}
        td {{ padding:6px 8px; border:1px solid #2D2D30; color:#CCC; }}
        tr:nth-child(even) {{ background:#252526; }}
        .badge {{ padding:2px 8px; border-radius:8px; font-size:11px; font-weight:bold; color:#1E1E1E; }}
        .badge.danger {{ background:#FF6347; }}
        .badge.warning {{ background:#FFB900; }}
        .badge.ok {{ background:#6CCB5F; }}
    </style>
</head>
<body>
    <h1>NetMonitor</h1>
    <div class='subtitle'>Machine : {Environment.MachineName} - Mise à jour : {DateTime.Now:dd/MM/yyyy HH:mm:ss}</div>
    <div class='status'>{statusText}</div>

    <h2>Intrusions récentes</h2>
    <table>
        <thead>
            <tr><th>Date</th><th>Sévérité</th><th>Détecteur</th><th>Message</th></tr>
        </thead>
        <tbody>{alertRows}</tbody>
    </table>

    <h2>Connexions récentes</h2>
    <table>
        <thead>
            <tr><th>Heure</th><th>Proto</th><th>Local</th><th>Distant</th><th>PID</th><th>Process</th></tr>
        </thead>
        <tbody>{connRows}</tbody>
    </table>
</body>
</html>";
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebHandler ERROR : " + ex.Message);
                return $"<html><body><h2>Erreur : {WebUtility.HtmlEncode(ex.Message)}</h2></body></html>";
            }
        }
    }
}