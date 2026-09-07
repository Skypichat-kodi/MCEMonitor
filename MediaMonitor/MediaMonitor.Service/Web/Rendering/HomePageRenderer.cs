using System;
using System.Linq;
using System.Net;
using System.Text;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Templates;
using MediaMonitor.Service.Web.Helpers;

namespace MediaMonitor.Service.Web.Rendering
{
    public static class HomePageRenderer
    {
        public static string Render(
            MediaMonitorEngine engine,
            int port,
            long requestCount,
            DateTime lastRequestTime,
            string lastRequestIp)
        {
            var live = engine.GetCurrentOpenFiles();
            var history = engine.GetHistory();

            int liveCount = live.Count;
            int historyCount = history.Count;
            int history24h = history.Count(h => h.Timestamp >= DateTime.Now.AddHours(-24));

            var sb = new StringBuilder();

            // ======================================================================
            //  HTML + CSS
            // ======================================================================

            sb.Append(@"
        <!DOCTYPE html>
        <html lang='fr'>
        <head>
        <meta charset='UTF-8'>
        <title>{{tr:MediaMonitor – Tableau de bord}}</title>
        <link rel='icon' type='image/x-icon' href='/favicon.ico'>
        <style>
        body { margin:0; padding:20px; font-family:Segoe UI,Arial; background:#1e1e1e; color:#e5e5e5; }
        h1 { margin:0 0 20px 0; font-size:20px; color:#fff; }
        .container { display:flex; gap:20px; flex-wrap:wrap; }
        .groupbox { flex:1; min-width:260px; border:1px solid #3c3c3c; border-radius:6px; background:#252526; padding:12px; }
        .groupbox-title { font-weight:bold; margin-bottom:10px; color:#fff; }
        .stats-grid { display:grid; grid-template-columns:auto auto; row-gap:6px; column-gap:12px; font-size:13px; }
        .label { color:#ccc; }
        .value { font-weight:bold; color:#fff; }
        table { width:100%; border-collapse:collapse; font-size:13px; margin-top:10px; }
        th, td { padding:4px 6px; border-bottom:1px solid #3c3c3c; }
        th { background:#2d2d30; color:#fff; }
        tr:nth-child(even) td { background:#262626; }
        tr:nth-child(odd) td { background:#1f1f1f; }
        .type-badge { padding:1px 6px; border-radius:10px; font-size:11px; color:#fff; }
        .type-audio { background:#007acc; }
        .type-serie { background:#c586c0; }
        .type-video { background:#d19a66; }
        .type-rec   { background:#ff4d4d; color:white; }
        .type-tv    { background:#ffe066; color:black; }

        .button-bar { margin-bottom:20px; display:flex; gap:10px; flex-wrap:wrap; }
        .button {
            padding:6px 12px;
            background:#007acc;
            color:white;
            text-decoration:none;
            border-radius:4px;
            font-size:13px;
        }
        .button-secondary { background:#444; }
        .button-danger { background:#cc3300; }
        .small { font-size:12px; color:#ccc; }

        td, th { border-right: 1px solid #3c3c3c; }
        td:last-child, th:last-child { border-right: none; }

        .rec-row td {
            background-color: rgba(255, 0, 0, 0.35) !important;
            color: white !important;
        }

        .tv-row td {
          background-color: rgba(255, 255, 0, 0.35) !important;
          color: black !important;
        }

        .info-btn {
            display: inline-flex;
            justify-content: center;
            align-items: center;
            width: 28px;
            height: 28px;
            background: #3498db;
            color: white;
            border-radius: 50%;
            text-decoration: none;
            font-weight: bold;
            font-family: Arial, sans-serif;
            transition: background 0.2s;
        }

        .info-btn:hover {
            background: #217dbb;
        }
        </style>
        ");

            // ======================================================================
            //  AUTO-REFRESH + OVERLAY
            // ======================================================================

            sb.Append("<div id='infoOverlayContainer'></div>");

            sb.Append(@"
        <script>
        let refreshEnabled = true;

        function autoRefresh() {
            if (refreshEnabled) {
                window.location.reload();
            }
        }
        setInterval(autoRefresh, 5000);
        </script>
        ");

            sb.Append("<h1>MediaMonitor – Tableau de bord</h1>");

            // ======================================================================
            //  BARRE D’ACTIONS
            // ======================================================================

            sb.Append(@"
                <div class='button-bar'>
                    <a href='/' class='button button-secondary'>{{tr:Rafraîchir}}</a>
                    <a href='/backup' class='button'>{{tr:Voir le backup}}</a>
                    <a href='/download' class='button'>{{tr:Télécharger le backup}}</a>
                    <a href='/purge' class='button button-danger' onclick='return confirm(""{{tr:Voulez-vous vraiment supprimer TOUTES les sauvegardes}} ?"");'>{{tr:Purger les sauvegardes}}</a>
                </div>
                ");

            sb.Append("<div class='container'>");

            // ======================================================================
            //  STATUT DU SERVICE
            // ======================================================================

            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>{{tr:Statut du service}}</div>");
            sb.Append("<div class='stats-grid'>");
            sb.Append("<div class='label'>{{tr:Serveur}} :</div><div class='value'>" + WebUtility.HtmlEncode(Environment.MachineName) + "</div>");
            sb.Append("<div class='label'>{{tr:Heure actuelle}} :</div><div class='value'>" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");
            sb.Append("</div>");
            sb.Append("</div>");

            // ======================================================================
            //  RAPPORTS (corrigé)
            // ======================================================================

            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>{{tr:Rapports}}</div>");
            sb.Append("<div class='stats-grid'>");

            var lastReport = ReportScheduler.LastReportTime;
            sb.Append("<div class='label'>{{tr:Dernier rapport :}}</div><div class='value'>" +
                      (lastReport == DateTime.MinValue ? "{{tr:Aucun rapport envoyé}}" : lastReport.ToString("yyyy-MM-dd HH:mm:ss")) +
                      "</div>");

            var next = ReportScheduler.NextReportTime;
            sb.Append("<div class='label'>{{tr:Prochain envoi :}}</div><div class='value'>" + next.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");

            sb.Append("</div>");
            sb.Append("</div>");

            // ======================================================================
            //  LECTURE EN COURS
            // ======================================================================

            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>{{tr:Lecture en cours}}</div>");
            sb.Append("<div class='stats-grid'>");
            sb.Append("<div class='label'>{{tr:Fichiers ouverts}} :</div><div class='value'>" + liveCount + "</div>");
            sb.Append("<div class='label'>{{tr:Utilisateurs actifs}} :</div><div class='value'>" +
                      live.Select(x => x.ClientDisplay).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count() +
                      "</div>");
            sb.Append("</div>");
            sb.Append("</div>");

            // ======================================================================
            //  HISTORIQUE
            // ======================================================================

            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>{{tr:Historique}}</div>");
            sb.Append("<div class='stats-grid'>");
            sb.Append("<div class='label'>{{tr:Événements totaux}} :</div><div class='value'>" + historyCount + "</div>");
            sb.Append("<div class='label'>{{tr:Sur 24h}} :</div><div class='value'>" + history24h + "</div>");

            if (historyCount > 0)
            {
                var last = history.Last();
                sb.Append("<div class='label'>{{tr:Dernier événement}} :</div>");
                sb.Append("<div class='value'>" + last.Timestamp.ToString("HH:mm:ss") + " – " +
                          WebUtility.HtmlEncode(last.MediaType) + " (" +
                          WebUtility.HtmlEncode(last.ClientDisplay) + ")</div>");
            }

            sb.Append("</div>");
            sb.Append("</div>");

            // ======================================================================
            //  WEBSERVER
            // ======================================================================

            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>{{tr:WebServer}}</div>");
            sb.Append("<div class='stats-grid'>");
            sb.Append("<div class='label'>{{tr:Port}} :</div><div class='value'>" + port + "</div>");
            sb.Append("<div class='label'>{{tr:Requêtes}} :</div><div class='value'>" + requestCount + "</div>");
            sb.Append("<div class='label'>{{tr:Dernière requête}} :</div><div class='value'>" +
                      (lastRequestTime == DateTime.MinValue ? "N/A" : lastRequestTime.ToString("HH:mm:ss")) +
                      "</div>");
            sb.Append("<div class='label'>{{tr:Dernier client}} :</div><div class='value'>" +
                      WebUtility.HtmlEncode(lastRequestIp) + "</div>");
            sb.Append("</div>");
            sb.Append("</div>");

            sb.Append("</div>"); // .container

            // ======================================================================
            //  TABLEAU LECTURE EN COURS
            // ======================================================================

            sb.Append("<h2 style='margin-top:25px; font-size:16px; color:#fff;'>{{tr:Lecture en cours}}</h2>");
            sb.Append("<table>");
            sb.Append("<thead><tr>");
            sb.Append("<th>{{tr:Client}}</th>");
            sb.Append("<th>{{tr:Type}}</th>");
            sb.Append("<th>{{tr:Canal}}</th>");
            sb.Append("<th>{{tr:Saison}}</th>");
            sb.Append("<th>{{tr:Épisode}}</th>");
            sb.Append("<th>{{tr:Nom}}</th>");
            sb.Append("<th>{{tr:Fichier}}</th>");
            sb.Append("<th>{{tr:Chemin}}</th>");
            sb.Append("<th>{{tr:Info}}</th>");
            sb.Append("</tr></thead><tbody>");

            foreach (var item in live)
            {
                string mediaType = item.MediaType ?? "";
                string badgeClass = TypeBadgeHelper.GetTypeBadgeClass(mediaType);

                sb.Append("<tr>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.ClientDisplay ?? "")}</td>");

                // *** LIGNE CORRIGÉE ***
                sb.Append(
                    $"<td><span class='type-badge {badgeClass}'>" +
                    $"{WebUtility.HtmlEncode(mediaType)}</span></td>");

                sb.Append($"<td>{WebUtility.HtmlEncode(item.Channel ?? "")}</td>");
                sb.Append($"<td>{(item.Saison > 0 ? item.Saison.ToString() : "")}</td>");
                sb.Append($"<td>{(item.Episode > 0 ? item.Episode.ToString() : "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.Nom ?? "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.FileName ?? "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.Path ?? "")}</td>");
                sb.Append($"<td><a class='info-btn' href='#' data-path='{WebUtility.HtmlEncode(item.Path)}' onclick='openInfo(this.dataset.path)'>I</a></td>");
                sb.Append("</tr>");
            }

            if (liveCount == 0)
                sb.Append("<tr><td colspan='9' class='small'>{{tr:Aucune lecture en cours.}}</td></tr>");

            sb.Append("</tbody></table>");

            // ======================================================================
            //  TABLEAU HISTORIQUE
            // ======================================================================

            sb.Append("<h2 style='margin-top:25px; font-size:16px; color:#fff;'>{{tr:Historique}}</h2>");
            sb.Append("<table>");
            sb.Append("<thead><tr>");
            sb.Append("<th>{{tr:Heure}}</th>");
            sb.Append("<th>{{tr:Client}}</th>");
            sb.Append("<th>{{tr:Type}}</th>");
            sb.Append("<th>{{tr:Canal}}</th>");
            sb.Append("<th>{{tr:Saison}}</th>");
            sb.Append("<th>{{tr:Épisode}}</th>");
            sb.Append("<th>{{tr:Nom}}</th>");
            sb.Append("<th>{{tr:Fichier}}</th>");
            sb.Append("<th>{{tr:Chemin}}</th>");
            sb.Append("<th>{{tr:Info}}</th>");
            sb.Append("</tr></thead><tbody>");

            foreach (var item in history.OrderByDescending(h => h.Timestamp).Take(200))
            {
                string mediaType = item.MediaType ?? "";
                string badgeClass = TypeBadgeHelper.GetTypeBadgeClass(mediaType);

                sb.Append("<tr>");
                sb.Append($"<td>{item.Timestamp:HH:mm:ss}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.ClientDisplay ?? "")}</td>");

                // *** LIGNE CORRIGÉE ***
                sb.Append(
                    $"<td><span class='type-badge {badgeClass}'>" +
                    $"{WebUtility.HtmlEncode(mediaType)}</span></td>");

                sb.Append($"<td>{WebUtility.HtmlEncode(item.Channel ?? "")}</td>");
                sb.Append($"<td>{(item.Saison > 0 ? item.Saison.ToString() : "")}</td>");
                sb.Append($"<td>{(item.Episode > 0 ? item.Episode.ToString() : "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.Nom ?? "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.FileName ?? "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.Path ?? "")}</td>");
                sb.Append($"<td><a class='info-btn' href='#' data-path='{WebUtility.HtmlEncode(item.Path)}' onclick='openInfo(this.dataset.path)'>I</a></td>");
                sb.Append("</tr>");
            }

            if (historyCount == 0)
                sb.Append("<tr><td colspan='10' class='small'>{{tr:Aucun événement.}}</td></tr>");

            sb.Append("</tbody></table>");

            // ======================================================================
            //  SCRIPT POPUP INFO
            // ======================================================================

            sb.Append(@"
                    <script>
                        function openInfo(path) {
                            refreshEnabled = false;

                            fetch('/info?path=' + encodeURIComponent(path))
                                .then(r => r.text())
                                .then(html => {

                                    const container = document.getElementById('infoOverlayContainer');
                                    container.innerHTML = html;

                                    const scripts = container.querySelectorAll('script');
                                    scripts.forEach(oldScript => {
                                        const newScript = document.createElement('script');

                                        if (oldScript.src) {
                                            newScript.src = oldScript.src;
                                        } else {
                                            newScript.textContent = oldScript.textContent;
                                        }

                                        document.body.appendChild(newScript);
                                    });
                                });
                        }

                        function closeOverlay() {
                            window.location.reload();
                        }
                    </script>
                    </body></html>
                    ");

            // Traduction
            var html = sb.ToString();
            html = TemplateEngine.Translate(html);

            return html;
        }
    }
}
