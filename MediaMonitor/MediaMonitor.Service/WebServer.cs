using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MediaMonitor.Core.Services;
using PdfSharp.Pdf;
using PdfSharp.Drawing;

namespace MediaMonitor.Service
{
    public class WebServer
    {
        private readonly HttpListener _listener = new();
        private readonly MediaMonitorEngine _engine;
        private readonly int _port;
        private Thread _thread;
        private bool _running = false;
        private long _requestCount = 0;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private string _lastRequestIp = "N/A";
        private string _lastReportStatus = "Aucun rapport envoyé";                

        private WebServerSettings _settings;

        public WebServer(int port, MediaMonitorEngine engine)
        {
            _port = port;
            _engine = engine;

            _settings = WebServerSettings.Load();

            _listener.Prefixes.Add($"http://+:{port}/");
        }

        public void ReloadSettings()
        {
            _settings = WebServerSettings.Load();
            CoreLog.Write("WebServer : paramètres rechargés.");
        }

        public void Start()
        {
            if (_running)
                return;

            _running = true;
            _listener.Start();

            _thread = new Thread(ServerLoop)
            {
                IsBackground = true
            };
            _thread.Start();

            CoreLog.Write($"WebServer démarré sur http://localhost:{_port}/");
        }

        public void Stop()
        {
            try
            {
                _running = false;
                _listener.Stop();
                CoreLog.Write("WebServer arrêté.");
            }
            catch { }
        }

        private void ServerLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch
                {
                    if (_running)
                        CoreLog.Write("WebServer : erreur dans ServerLoop.");
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                _requestCount++;
                _lastRequestTime = DateTime.Now;
                _lastRequestIp = ctx.Request.RemoteEndPoint?.ToString() ?? "N/A";           
                // ?? AUTHENTIFICATION BASIC
                if (!CheckAuth(ctx))
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"MediaMonitor\"");
                    ctx.Response.Close();
                    return;
                }

                string path = ctx.Request.Url.AbsolutePath.ToLower();

                switch (path)
                {
                    case "/":
                        SendHtml(ctx, BuildHomePage());
                        break;

                    case "/history":
                        SendJson(ctx, _engine.GetHistory());
                        break;

                    case "/live":
                        SendJson(ctx, _engine.GetCurrentOpenFiles());
                        break;

                    case "/lastimage":
                        SendJson(ctx, new { lastImage = _engine.GetLastImage() });
                        break;

                    case "/status":
                        SendJson(ctx, new
                        {
                            server = Environment.MachineName,
                            time = DateTime.Now,
                            open = _engine.GetCurrentOpenFiles().Count,
                            history = _engine.GetHistory().Count
                        });
                        break;

                    case "/report":
                        SendHtml(ctx, _engine.GenerateReportFromHistory());
                        break;

                    case "/clear":
                        _engine.ClearHistory();
                        SendHtml(ctx, "<html><body><h2>Historique effacé.</h2></body></html>");
                        break;

                    case "/backup":
                        SendHtml(ctx, BuildBackupPage(ctx.Request));
                        break;
                        
                    case "/download":
                        DownloadBackup(ctx);
                        break;

                    case "/purge":
                        PurgeBackups(ctx);
                        break;

                    case "/back":
                        SendHtml(ctx, BuildHomePage());
                        break;                       

                    default:
                        SendHtml(ctx, "<html><body><h2>404 - Not Found</h2></body></html>", 404);
                        break;
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebServer ERROR: " + ex.Message);
            }
            
            // ------------------------------------------------------------
            // Favicon
            // ------------------------------------------------------------
            // Alias standard
            if (ctx.Request.Url.AbsolutePath == "/favicon.ico")
            {
                string icoPath = @"C:\ProgramData\MCEMonitor\MediaMonitor.ico";

                if (File.Exists(icoPath))
                {
                    byte[] ico = File.ReadAllBytes(icoPath);
                    ctx.Response.ContentType = "image/x-icon";
                    ctx.Response.OutputStream.Write(ico, 0, ico.Length);
                    ctx.Response.Close();
                    return;
                }

                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
            
        }

        // ?? Vérification Basic Auth
        private bool CheckAuth(HttpListenerContext ctx)
        {
            string auth = ctx.Request.Headers["Authorization"];

            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Basic "))
                return false;

            try
            {
                string encoded = auth.Substring("Basic ".Length).Trim();
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

                // Format attendu : "username:password"
                return decoded == $"{_settings.Username}:{_settings.Password}";
            }
            catch
            {
                return false;
            }
        }

        private void SendJson(HttpListenerContext ctx, object data)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            byte[] buffer = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
        }

        private void SendHtml(HttpListenerContext ctx, string html, int status = 200)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
        }
        private string GetTypeBadgeClass(string? mediaType)
        {
            return (mediaType ?? "").ToLower() switch
            {
                "audio" => "type-audio",
                "serie" => "type-serie",
                "video" => "type-video",
                _ => ""
            };
        }

        private DateTime GetReportSendTime()
        {
            string path = @"C:\ProgramData\MCEMonitor\Logs\MediaMonitor.Schedule.log";

            if (!File.Exists(path))
                return DateTime.MinValue;

            string lastLine = File.ReadLines(path).LastOrDefault(l => l.Contains("Prochain envoi"));

            if (lastLine == null)
                return DateTime.MinValue;

            // Exemple de ligne :
            // [2026-06-14 20:19:56] [CODE01] Prochain envoi du rapport prévu à 11:50 (dans 15h 30min)

            int idx = lastLine.IndexOf("prévu à ");
            if (idx < 0)
                return DateTime.MinValue;

            string timePart = lastLine.Substring(idx + "prévu à ".Length, 5); // "11:50"

            if (TimeSpan.TryParse(timePart, out var ts))
            {
                DateTime next = DateTime.Today.Add(ts);

                // Si l'heure est déjà passée ? demain
                if (next <= DateTime.Now)
                    next = next.AddDays(1);

                return next;
            }

            return DateTime.MinValue;
        }
        
        // ==========================
        //  PAGE PRINCIPALE
        // ==========================        
        private string BuildHomePage()
        {
            var live = _engine.GetCurrentOpenFiles();
            var history = _engine.GetHistory();

            int liveCount = live.Count;
            int historyCount = history.Count;
            int history24h = history.Count(h => h.Timestamp >= DateTime.Now.AddHours(-24));

            var sb = new StringBuilder();

            sb.Append(@"
        <!DOCTYPE html>
        <html lang='fr'>
        <head>
        <meta charset='UTF-8'>
        <title>MediaMonitor – Tableau de bord</title>
        <link rel='icon' type='image/x-icon' href='/MediaMonitor.ico'>
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

        /* Type = colonne 2 dans le premier tableau */
        table:nth-of-type(1) td:nth-child(2),
        table:nth-of-type(1) th:nth-child(2) {
            text-align: center;
        }

        /* Type = colonne 3 dans le second tableau */
        table:nth-of-type(2) td:nth-child(3),
        table:nth-of-type(2) th:nth-child(3) {
            text-align: center;
        }

        td, th {
            border-right: 1px solid #3c3c3c;
        }

        td:last-child, th:last-child {
            border-right: none;
        }
        </style>
        <meta http-equiv='refresh' content='5'>
        </head>
        <body>
        ");

            sb.Append("<h1>MediaMonitor – Tableau de bord</h1>");

            sb.Append(@"
        <div class='button-bar'>
            <a href='/' class='button button-secondary'>Rafraîchir</a>
            <a href='/backup' class='button'>Voir le backup</a>
            <a href='/download' class='button'>Télécharger le backup</a>
            <a href='/purge' class='button button-danger' onclick='return confirm(""Voulez-vous vraiment supprimer TOUTES les sauvegardes ?"");'>Purger les sauvegardes</a>
        </div>
        ");

            sb.Append("<div class='container'>");

            // Statut du service
            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>Statut du service</div>");
            sb.Append("<div class='stats-grid'>");
            sb.Append($"<div class='label'>Serveur :</div><div class='value'>{WebUtility.HtmlEncode(Environment.MachineName)}</div>");
            sb.Append($"<div class='label'>Heure actuelle :</div><div class='value'>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>");
            sb.Append("</div>");
            sb.Append("</div>");

            // Rapports
            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>Rapports</div>");
            sb.Append("<div class='stats-grid'>");
            sb.Append("<div class='label'>Dernier rapport :</div>");
            sb.Append($"<div class='value'>{WebUtility.HtmlEncode(_lastReportStatus)}</div>");
            var next = GetReportSendTime();
            sb.Append($"<div class='label'>Prochain envoi :</div><div class='value'>{next:yyyy-MM-dd HH:mm:ss}</div>");
            sb.Append("</div>");
            sb.Append("</div>");

            // Lecture en cours
            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>Lecture en cours</div>");
            sb.Append("<div class='stats-grid'>");
            sb.Append($"<div class='label'>Fichiers ouverts :</div><div class='value'>{liveCount}</div>");
            sb.Append($"<div class='label'>Utilisateurs actifs :</div><div class='value'>{live.Select(x => x.ClientName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count()}</div>");
            sb.Append("</div>");
            sb.Append("</div>");

            // Historique
            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>Historique</div>");
            sb.Append("<div class='stats-grid'>");
            sb.Append($"<div class='label'>Événements totaux :</div><div class='value'>{historyCount}</div>");
            sb.Append($"<div class='label'>Sur 24h :</div><div class='value'>{history24h}</div>");
            if (historyCount > 0)
            {
                var last = history.Last();
                sb.Append("<div class='label'>Dernier événement :</div>");
                sb.Append($"<div class='value'>{last.Timestamp:HH:mm:ss} – {WebUtility.HtmlEncode(last.MediaType)} ({WebUtility.HtmlEncode(last.ClientName)})</div>");
            }
            sb.Append("</div>");
            sb.Append("</div>");

            // WebServer
            sb.Append("<div class='groupbox'>");
            sb.Append("<div class='groupbox-title'>WebServer</div>");
            sb.Append("<div class='stats-grid'>");
            sb.Append($"<div class='label'>Port :</div><div class='value'>{_port}</div>");
            sb.Append($"<div class='label'>Requetes :</div><div class='value'>{_requestCount}</div>");
            sb.Append($"<div class='label'>Derniere requete :</div><div class='value'>{(_lastRequestTime == DateTime.MinValue ? "N/A" : _lastRequestTime.ToString("HH:mm:ss"))}</div>");
            sb.Append($"<div class='label'>Dernier client :</div><div class='value'>{WebUtility.HtmlEncode(_lastRequestIp)}</div>");
            sb.Append("</div>");
            sb.Append("</div>");

            sb.Append("</div>"); // .container

            // === TABLEAU LECTURE EN COURS ===
            sb.Append("<h2 style='margin-top:25px; font-size:16px; color:#fff;'>Lecture en cours</h2>");
            sb.Append("<table>");
            sb.Append("<thead><tr><th>Client</th><th>Type</th><th>Saison</th><th>Épisode</th><th>Nom</th><th>Fichier</th><th>Chemin</th></tr></thead><tbody>");

            foreach (var item in live)
            {
                string type = item.MediaType ?? "";
                string badgeClass = GetTypeBadgeClass(type);

                sb.Append("<tr>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.ClientName ?? "")}</td>");
                sb.Append($"<td><span class='type-badge {badgeClass}'>{WebUtility.HtmlEncode(type)}</span></td>");
                sb.Append($"<td>{item.Saison}</td>");
                sb.Append($"<td>{item.Episode}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.Nom ?? "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.FileName ?? "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.Path ?? "")}</td>");
                sb.Append("</tr>");
            }

            if (liveCount == 0)
                sb.Append("<tr><td colspan='7' class='small'>Aucune lecture en cours.</td></tr>");

            sb.Append("</tbody></table>");

            // === TABLEAU HISTORIQUE ===
            sb.Append("<h2 style='margin-top:25px; font-size:16px; color:#fff;'>Historique</h2>");
            sb.Append("<table>");
            sb.Append("<thead><tr><th>Heure</th><th>Client</th><th>Type</th><th>Saison</th><th>Épisode</th><th>Nom</th><th>Fichier</th><th>Chemin</th></tr></thead><tbody>");

            foreach (var item in history.OrderByDescending(h => h.Timestamp).Take(200))
            {
                string type = item.MediaType ?? "";
                string badgeClass = GetTypeBadgeClass(type);

                sb.Append("<tr>");
                sb.Append($"<td>{item.Timestamp:HH:mm:ss}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.ClientName ?? "")}</td>");
                sb.Append($"<td><span class='type-badge {badgeClass}'>{WebUtility.HtmlEncode(type)}</span></td>");
                sb.Append($"<td>{item.Saison}</td>");
                sb.Append($"<td>{item.Episode}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.Nom ?? "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.FileName ?? "")}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.Path ?? "")}</td>");
                sb.Append("</tr>");
            }

            if (historyCount == 0)
                sb.Append("<tr><td colspan='8' class='small'>Aucun historique disponible.</td></tr>");

            sb.Append("</tbody></table>");

            sb.Append("</body></html>");

            return sb.ToString();
        }

        // ==========================
        //  PAGE BACKUP SANS FICHIER
        // ==========================
        private string BuildBackupPage(HttpListenerRequest req)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";
            if (!Directory.Exists(folder))
        return @"
        <html>
        <head>
        <meta charset='UTF-8'>
        <title>MediaMonitor – Sauvegarde</title>
        <link rel='icon' type='image/x-icon' href='/MediaMonitor.ico'>
        <style>
        body { background:#111; color:#eee; font-family:Segoe UI,Arial; text-align:center; padding-top:50px; }
        h2 { color:#f55; }
        .button {
            display:inline-block;
            padding:10px 20px;
            background:#0078D4;
            color:white;
            text-decoration:none;
            border-radius:6px;
            font-weight:bold;
            margin-top:20px;
        }
        
        </style>
        </head>
        <body>
        <h2>Aucune sauvegarde trouvée.</h2>
        <a class='button' href='/'>Retour</a>
        </body>
        </html>";

                    var files = Directory.GetFiles(folder, "history_*.json");
                    if (files.Length == 0)
                    
        
        // ==========================
        //  PAGE BACKUP AVEC FICHIER
        // ==========================                    
        return @"
        <html>
        <head>
        <meta charset='UTF-8'>
        <title>MediaMonitor – Sauvegarde</title>
        <link rel='icon' type='image/x-icon' href='/MediaMonitor.ico'>
        <style>
        body { background:#111; color:#eee; font-family:Segoe UI,Arial; text-align:center; padding-top:50px; }
        h2 { color:#f55; }
        .button {
            display:inline-block;
            padding:10px 20px;
            background:#0078D4;
            color:white;
            text-decoration:none;
            border-radius:6px;
            font-weight:bold;
            margin-top:20px;
        }
        </style>
        </head>
        <body>
        <h2>Aucune sauvegarde disponible.</h2>
        <a class='button' href='/'>Retour</a>
        </body>
        </html>";

            string lastFile = files.OrderByDescending(f => f).First();

            var json = File.ReadAllText(lastFile);
            BackupFileModel? backup = JsonSerializer.Deserialize<BackupFileModel>(json);

            if (backup == null || backup.Reports == null)
                return "<html><body><h2>Sauvegarde invalide.</h2></body></html>";

            // Aplatir tous les items de tous les jours
            var allItems = backup.Reports
                .Where(r => r.Items != null)
                .SelectMany(r => r.Items)
                .ToList();

            var items = allItems.ToList();

            // Paramètres GET
            string type = req.QueryString["type"]?.ToLower() ?? "all";
            string client = req.QueryString["client"]?.ToLower() ?? "all";
            string date = req.QueryString["date"]?.ToLower() ?? "all";
            string sort = req.QueryString["sort"]?.ToLower() ?? "date_desc";

            // Filtre type
            items = type switch
            {
                "audio" => items.Where(i => i.MediaType.Equals("Audio", StringComparison.OrdinalIgnoreCase)).ToList(),
                "serie" => items.Where(i => i.MediaType.Equals("Serie", StringComparison.OrdinalIgnoreCase)).ToList(),
                "video" => items.Where(i => i.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase)).ToList(),
                _ => items
            };

            // Filtre client
            if (client != "all")
            {
                items = items.Where(i => i.ClientName != null &&
                                         i.ClientName.Equals(client, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Filtre date
            DateTime now = DateTime.Now;
            items = date switch
            {
                "today" => items.Where(i => i.Timestamp.Date == now.Date).ToList(),
                "yesterday" => items.Where(i => i.Timestamp.Date == now.AddDays(-1).Date).ToList(),
                "7" => items.Where(i => i.Timestamp >= now.AddDays(-7)).ToList(),
                "30" => items.Where(i => i.Timestamp >= now.AddDays(-30)).ToList(),
                _ => items
            };

            // Tri
            items = sort switch
            {
                "name_asc" => items.OrderBy(i => i.Nom).ToList(),
                "name_desc" => items.OrderByDescending(i => i.Nom).ToList(),
                "date_asc" => items.OrderBy(i => i.Timestamp).ToList(),
                _ => items.OrderByDescending(i => i.Timestamp).ToList()
            };

            // Stats
            int total = items.Count;
            int audio = items.Count(i => i.MediaType.Equals("Audio", StringComparison.OrdinalIgnoreCase));
            int series = items.Count(i => i.MediaType.Equals("Serie", StringComparison.OrdinalIgnoreCase));
            int videos = items.Count(i => i.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase));

            // Liste complète des clients (pour le menu)
            var allClients = allItems
                .Where(i => !string.IsNullOrWhiteSpace(i.ClientName))
                .Select(i => i.ClientName!)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            var clientOptions = new StringBuilder();
            clientOptions.Append($"<option value='all' {(client == "all" ? "selected" : "")}>Tous</option>");
            foreach (var c in allClients)
            {
                string val = c.ToLower();
                string selected = (val == client) ? "selected" : "";
                clientOptions.Append($"<option value='{WebUtility.HtmlEncode(val)}' {selected}>{WebUtility.HtmlEncode(c)}</option>");
            }

            // Lignes du tableau
            var rows = new StringBuilder();
            foreach (var item in items)
            {
                string badgeClass = item.MediaType?.ToLower() switch
                {
                    "audio" => "type-audio",
                    "serie" => "type-serie",
                    "video" => "type-video",
                    _ => ""
                };

                rows.Append($@"
        <tr>
            <td>{WebUtility.HtmlEncode(item.Nom ?? "")}</td>
            <td><span class='type-badge {badgeClass}'>{WebUtility.HtmlEncode(item.MediaType ?? "")}</span></td>
            <td>{WebUtility.HtmlEncode(item.ClientName ?? "")}</td>
            <td>{item.Timestamp:dd/MM/yyyy HH:mm}</td>
        </tr>");
            }

            int[] hours = new int[24];
            foreach (var it in items)
                hours[it.Timestamp.Hour]++;

            string hoursJson = JsonSerializer.Serialize(hours);

            string html = BackupHtmlTemplate
                .Replace("{{TOTAL}}", total.ToString())
                .Replace("{{AUDIO}}", audio.ToString())
                .Replace("{{SERIES}}", series.ToString())
                .Replace("{{VIDEOS}}", videos.ToString())
                .Replace("{{COUNT}}", total.ToString())
                .Replace("{{PERIOD}}", WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(lastFile)))
                .Replace("{{ROWS}}", rows.ToString())
                .Replace("{{CLIENT_OPTIONS}}", clientOptions.ToString())
                .Replace("{{FILTER_TYPE}}", type)
                .Replace("{{FILTER_CLIENT}}", client)
                .Replace("{{SORT}}", sort)
                .Replace("{{SEL_ALL}}", type == "all" ? "selected" : "")
                .Replace("{{SEL_AUDIO}}", type == "audio" ? "selected" : "")
                .Replace("{{SEL_SERIE}}", type == "serie" ? "selected" : "")
                .Replace("{{SEL_VIDEO}}", type == "video" ? "selected" : "")
                .Replace("{{SEL_DATEDESC}}", sort == "date_desc" ? "selected" : "")
                .Replace("{{SEL_DATEASC}}", sort == "date_asc" ? "selected" : "")
                .Replace("{{SEL_NAMEASC}}", sort == "name_asc" ? "selected" : "")
                .Replace("{{SEL_NAMEDESC}}", sort == "name_desc" ? "selected" : "")
                .Replace("{{SEL_DATE_ALL}}", date == "all" ? "selected" : "")
                .Replace("{{SEL_DATE_TODAY}}", date == "today" ? "selected" : "")
                .Replace("{{SEL_DATE_YESTERDAY}}", date == "yesterday" ? "selected" : "")
                .Replace("{{SEL_DATE_7}}", date == "7" ? "selected" : "")
                .Replace("{{SEL_DATE_30}}", date == "30" ? "selected" : "")
                .Replace("{{HOURS_DATA}}", hoursJson);

            return html;
        }

        // Modele du fichier cumulatif généré par le service
        private class BackupFileModel
        {
            public int RetentionDays { get; set; }
            public List<DailyReport> Reports { get; set; } = new();
        }

        private class DailyReport
        {
            public DateTime Date { get; set; }
            public List<BackupItem> Items { get; set; } = new();
        }

        // Modèle des éléments de sauvegarde
        private class BackupItem
        {
            public string? ClientName { get; set; }
            public string? MediaType { get; set; }
            public string? Nom { get; set; }
            public string? FileName { get; set; }
            public DateTime Timestamp { get; set; }
        }

        private void DownloadBackup(HttpListenerContext ctx)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";
            var files = Directory.GetFiles(folder, "history_*.json");

            if (files.Length == 0)
            {
                SendHtml(ctx, "<html><body><h2>Aucune sauvegarde disponible.</h2></body></html>");
                return;
            }

            string lastFile = files.OrderByDescending(f => f).First();
            string json = File.ReadAllText(lastFile);

            BackupFileModel? backup = JsonSerializer.Deserialize<BackupFileModel>(json);
            if (backup == null || backup.Reports == null)
            {
                SendHtml(ctx, "<html><body><h2>Sauvegarde invalide.</h2></body></html>");
                return;
            }

            // Aplatir les items
            var items = backup.Reports
                .Where(r => r.Items != null)
                .SelectMany(r => r.Items)
                .OrderByDescending(i => i.Timestamp)
                .ToList();

            // --- Génération PDF ---
            using var doc = new PdfSharp.Pdf.PdfDocument();
            doc.Info.Title = "Backup MediaMonitor";

            var page = doc.AddPage();
            var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            var font = new PdfSharp.Drawing.XFont("Arial", 10);

            double y = 20;

            gfx.DrawString("Historique sauvegardé", 
                new PdfSharp.Drawing.XFont("Arial", 14, PdfSharp.Drawing.XFontStyle.Bold),
                PdfSharp.Drawing.XBrushes.Black, 
                new PdfSharp.Drawing.XPoint(20, y));

            y += 30;

            foreach (var item in items)
            {
                string line = $"{item.Timestamp:dd/MM/yyyy HH:mm}  |  {item.MediaType}  |  {item.ClientName}  |  {item.Nom}";
                gfx.DrawString(line, font, PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(20, y));
                y += 15;

                if (y > page.Height - 40)
                {
                    page = doc.AddPage();
                    gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
                    y = 20;
                }
            }

            using var ms = new MemoryStream();
            doc.Save(ms);
            byte[] pdfBytes = ms.ToArray();

            // Déterminer les dates du backup
            var dates = backup.Reports
                .Where(r => r.Items != null && r.Items.Count > 0)
                .Select(r => r.Date)
                .OrderBy(d => d)
                .ToList();

            string pdfName;

            if (dates.Count == 1)
            {
                // Un seul jour
                pdfName = $"backup_{dates[0]:yyyy-MM-dd}.pdf";
            }
            else if (dates.Count > 1)
            {
                // Plage de dates
                pdfName = $"backup_{dates.First():yyyy-MM-dd}_to_{dates.Last():yyyy-MM-dd}.pdf";
            }
            else
            {
                // Fallback
                pdfName = "backup.pdf";
            }

            // --- Envoi au navigateur ---
            ctx.Response.ContentType = "application/pdf";
            ctx.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{pdfName}\"");
            ctx.Response.OutputStream.Write(pdfBytes, 0, pdfBytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private void PurgeBackups(HttpListenerContext ctx)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";

            if (Directory.Exists(folder))
            {
                foreach (var f in Directory.GetFiles(folder, "history_*.json"))
                    File.Delete(f);
            }

            SendHtml(ctx, "<html><body><h2>Toutes les sauvegardes ont été supprimées.</h2><a href='/backup'>Retour</a></body></html>");
        }

        private const string BackupHtmlTemplate = @"
        <!DOCTYPE html>
        <html lang=""fr"">
        <head>
        <meta charset=""UTF-8"">
        <title>Historique sauvegardé - MediaMonitor</title>
        <link rel=""icon"" type=""image/x-icon"" href=""/MediaMonitor.ico"">

        <!-- Chart.js -->
        <script src=""https://cdn.jsdelivr.net/npm/chart.js""></script>

        <style>
            body { margin:0; padding:20px; font-family:Segoe UI,Arial; background:#1e1e1e; color:#e5e5e5; }
            h1 { margin:0 0 20px 0; font-size:20px; color:#fff; }
            .container { display:flex; gap:20px; }
            .groupbox {flex:1; border:1px solid #3c3c3c; border-radius:6px; background:#252526; padding:12px; position:relative; transition: all 0.8s ease; /* plus lent */}
            .left-content {transition: opacity 0.6s ease; /* plus lent */}
            .groupbox-title { font-weight:bold; margin-bottom:10px; color:#fff; }
            .stats-grid { display:grid; grid-template-columns:auto auto; row-gap:6px; column-gap:12px; font-size:13px; }
            .label { color:#ccc; }
            .value { font-weight:bold; color:#fff; }
            .listing-header { display:flex; justify-content:space-between; margin-bottom:8px; font-size:13px; }
            table { width:100%; border-collapse:collapse; font-size:13px; }
            th, td { padding:4px 6px; border-bottom:1px solid #3c3c3c; }
            th { background:#2d2d30; color:#fff; }
            tr:nth-child(even) td { background:#262626; }
            tr:nth-child(odd) td { background:#1f1f1f; }
            .type-badge { padding:1px 6px; border-radius:10px; font-size:11px; color:#fff; }
            .type-audio { background:#007acc; }
            .type-serie { background:#c586c0; }
            .type-video { background:#d19a66; }
            select { background:#2d2d30; color:#e5e5e5; border:1px solid #3c3c3c; border-radius:4px; padding:2px 4px; }
            label { margin-right:4px; }

            td, th { border-right: 1px solid #3c3c3c; }
            td:last-child, th:last-child { border-right: none; }
            td:nth-child(2), th:nth-child(2) { text-align:center; }

            /* --- COLLAPSIBLE --- */

            /* Colonne gauche normale */
            #leftColumn {
                overflow: visible;
                transition: all 0.6s ease;
            }

            /* Contenu interne */
            .left-content {
                transition: opacity 0.45s ease;
            }

            /* Colonne repliée */
            #leftColumn.collapsed {
                flex: 0 0 40px !important;
                max-width: 40px !important;
                min-width: 40px !important;
                padding: 12px 4px;
            }

            /* Contenu interne masqué SANS prendre de place */
            #leftColumn.collapsed .left-content {
                opacity: 0;
                height: 0;
                overflow: hidden;
                padding: 0;
                margin: 0;
            }

            .toggle-btn {
                position: absolute;
                top: 10px;
                right: -18px;
                width: 28px;
                height: 28px;
                border-radius: 50%;
                border: none;
                background: #444;
                color: #fff;
                cursor: pointer;
                font-size: 18px;
                line-height: 28px;
                text-align: center;
                z-index: 50;
            }

            .toggle-btn:hover { background:#666; }
        </style>

        </head>
        <body>

        <h1>Historique sauvegardé</h1>

        <div style=""margin-bottom:20px; display:flex; gap:10px;"">
            <a href=""/download"" style=""padding:6px 12px; background:#007acc; color:white; text-decoration:none; border-radius:4px;"">Télécharger</a>

            <a href=""/purge""
               onclick=""return confirm('Voulez-vous vraiment supprimer TOUTES les sauvegardes ?');""
               style=""padding:6px 12px; background:#cc3300; color:white; text-decoration:none; border-radius:4px;"">
               Purger
            </a>

            <a href=""/"" style=""padding:6px 12px; background:#444; color:white; text-decoration:none; border-radius:4px;"">Retour</a>
        </div>

        <div style=""margin-bottom:15px; display:flex; gap:20px; flex-wrap:wrap;"">

            <div>
                <label>Filtrer par type :</label>
                <select onchange=""location.href='?type=' + this.value + '&client={{FILTER_CLIENT}}&date={{DATE}}&sort={{SORT}}';"">
                    <option value='all' {{SEL_ALL}}>Tous</option>
                    <option value='audio' {{SEL_AUDIO}}>Audio</option>
                    <option value='serie' {{SEL_SERIE}}>Séries</option>
                    <option value='video' {{SEL_VIDEO}}>Vidéos</option>
                </select>
            </div>

            <div>
                <label>Filtrer par client :</label>
                <select onchange=""location.href='?type={{FILTER_TYPE}}&client=' + this.value + '&date={{DATE}}&sort={{SORT}}';"">
                    {{CLIENT_OPTIONS}}
                </select>
            </div>

            <div>
                <label>Filtrer par date :</label>
                <select onchange=""location.href='?type={{FILTER_TYPE}}&client={{FILTER_CLIENT}}&date=' + this.value + '&sort={{SORT}}';"">
                    <option value='all' {{SEL_DATE_ALL}}>Tout</option>
                    <option value='today' {{SEL_DATE_TODAY}}>Aujourd’hui</option>
                    <option value='yesterday' {{SEL_DATE_YESTERDAY}}>Hier</option>
                    <option value='7' {{SEL_DATE_7}}>7 jours</option>
                    <option value='30' {{SEL_DATE_30}}>30 jours</option>
                </select>
            </div>

            <div>
                <label>Trier :</label>
                <select onchange=""location.href='?type={{FILTER_TYPE}}&client={{FILTER_CLIENT}}&date={{DATE}}&sort=' + this.value;"">
                    <option value='date_desc' {{SEL_DATEDESC}}>Date +</option>
                    <option value='date_asc' {{SEL_DATEASC}}>Date -</option>
                    <option value='name_asc' {{SEL_NAMEASC}}>Nom (A–Z)</option>
                    <option value='name_desc' {{SEL_NAMEDESC}}>Nom (Z–A)</option>
                </select>
            </div>

        </div>

        <div class=""container"">

            <!-- COLONNE GAUCHE -->
            <div class=""groupbox"" id=""leftColumn"">
                <button class=""toggle-btn"" id=""toggleLeft"">&lt;</button>

                <div class=""left-content"">
                    <div class=""groupbox-title"">Statistiques</div>
                    <div class=""stats-grid"">
                        <div class=""label"">Titres lus :</div><div class=""value"">{{TOTAL}}</div>
                        <div class=""label"">Audio :</div><div class=""value"">{{AUDIO}}</div>
                        <div class=""label"">Séries :</div><div class=""value"">{{SERIES}}</div>
                        <div class=""label"">Vidéos :</div><div class=""value"">{{VIDEOS}}</div>
                    </div>

                    <br>

                    <div class=""groupbox-title"">Graphiques</div>

                    <div style=""display:flex; gap:15px; align-items:stretch;"">

                        <!-- Bloc Donut -->
                        <div style=""flex:1; background:#2b2b2b; border:1px solid #3c3c3c; border-radius:6px; padding:10px;"">
                            <div style=""text-align:center; font-weight:bold; margin-bottom:8px; color:#fff;"">
                                Répartition par type
                            </div>
                            <canvas id=""chartTypes"" height=""160""></canvas>
                        </div>

                        <!-- Séparateur vertical -->
                        <div style=""width:1px; background:#3c3c3c;""></div>

                        <!-- Bloc Horaire -->
                        <div style=""flex:1; background:#2b2b2b; border:1px solid #3c3c3c; border-radius:6px; padding:10px;"">
                            <div style=""text-align:center; font-weight:bold; margin-bottom:8px; color:#fff;"">
                                Activité par heure
                            </div>
                            <canvas id=""chartHours"" height=""160""></canvas>
                        </div>

                    </div>
                </div>
            </div>

            <!-- COLONNE DROITE -->
            <div class=""groupbox"">
                <div class=""groupbox-title"">Listing des titres</div>
                <div class=""listing-header"">
                    <span>Période : {{PERIOD}}</span>
                    <span>{{COUNT}} élément(s)</span>
                </div>
                <table>
                    <thead>
                        <tr><th>Titre</th><th>Type</th><th>Client</th><th>Date</th></tr>
                    </thead>
                    <tbody>
                        {{ROWS}}
                    </tbody>
                </table>
            </div>

        </div>

        <!-- SCRIPTS GRAPHIQUES -->
        <script>
            const audio = {{AUDIO}};
            const series = {{SERIES}};
            const videos = {{VIDEOS}};
            const hoursData = {{HOURS_DATA}};

            new Chart(document.getElementById('chartTypes'), {
                type: 'doughnut',
                data: {
                    labels: ['Audio', 'Séries', 'Vidéos'],
                    datasets: [{
                        data: [audio, series, videos],
                        backgroundColor: ['#007acc', '#c586c0', '#d19a66']
                    }]
                },
                options: {
                    plugins: { legend: { labels: { color:'#fff' } } }
                }
            });

            new Chart(document.getElementById('chartHours'), {
                type: 'line',
                data: {
                    labels: [...Array(24).keys()].map(h => (h<10?'0':'') + h + 'h'),
                    datasets: [{
                        label: 'Lectures par heure',
                        data: hoursData,
                        borderColor: '#4fc3f7',
                        backgroundColor: 'rgba(79,195,247,0.2)',
                        tension: 0.3
                    }]
                },
                options: {
                    scales: {
                        x: { ticks: { color:'#fff' } },
                        y: { ticks: { color:'#fff' } }
                    },
                    plugins: { legend: { labels: { color:'#fff' } } }
                }
            });
        </script>

        <!-- SCRIPT COLLAPSIBLE -->
        <script>
            (function () {
                const btn = document.getElementById('toggleLeft');
                const leftCol = document.getElementById('leftColumn');

                if (!btn || !leftCol) return;

                let collapsed = false;

                btn.addEventListener('click', () => {
                    collapsed = !collapsed;

                    if (collapsed) {
                        leftCol.classList.add('collapsed');
                        btn.textContent = '>';
                    } else {
                        leftCol.classList.remove('collapsed');
                        btn.textContent = '<';
                    }
                });
            })();
        </script>

        </body>
        </html>";

    }
}

