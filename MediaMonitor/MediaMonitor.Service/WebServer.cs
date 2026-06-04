using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MediaMonitor.Core.Services;

namespace MediaMonitor.Service
{
    public class WebServer
    {
        private readonly HttpListener _listener = new();
        private readonly MediaMonitorEngine _engine;
        private readonly int _port;
        private Thread _thread;
        private bool _running = false;

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
                        if (ctx.Request.QueryString["save"] == "1")
                        {
                            _engine.SaveHistoryBackup();
                        }
                        SendHtml(ctx, BuildBackupPage(ctx.Request));
                        break;

                    case "/download-backup":
                        SendBackupFile(ctx);
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

        private void SendBackupFile(HttpListenerContext ctx)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";
            if (!Directory.Exists(folder))
            {
                SendHtml(ctx, "<html><body><h2>Aucune sauvegarde disponible.</h2></body></html>");
                return;
            }

            var files = Directory.GetFiles(folder, "history_*.json");
            if (files.Length == 0)
            {
                SendHtml(ctx, "<html><body><h2>Aucune sauvegarde disponible.</h2></body></html>");
                return;
            }

            string lastFile = files.OrderByDescending(f => f).First();
            byte[] data = File.ReadAllBytes(lastFile);

            ctx.Response.ContentType = "application/json";
            ctx.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(lastFile)}\"");
            ctx.Response.ContentLength64 = data.Length;

            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
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

        private string BuildHomePage()
        {
            var live = _engine.GetCurrentOpenFiles();
            var history = _engine.GetHistory();

            var sb = new StringBuilder();

            sb.Append(@"
            <html>
            <head>
            <meta charset='UTF-8'>
            <title>MediaMonitor Web</title>
            <style>
            body { background:#111; color:#eee; font-family:Arial; }
            h1 { color:#6cf; }
            table { width:100%; border-collapse:collapse; margin-top:20px; }
            th, td { border:1px solid #444; padding:6px; }
            th { background:#222; }
            tr:nth-child(even) { background:#1a1a1a; }
            a { color:#6cf; }
            .button {
                display:inline-block;
                padding:10px 20px;
                background:#4CAF50;
                color:white !important;
                text-decoration:none;
                border-radius:6px;
                font-size:16px;
                font-weight:bold;
                margin-top:15px;
            }
            </style>
            <meta http-equiv='refresh' content='5'>
            </head>
            <body>
            <h1>MediaMonitor – Tableau de bord</h1>
            ");

            sb.Append($"<p>Serveur : <b>{Environment.MachineName}</b></p>");
            sb.Append($"<p>Heure : <b>{DateTime.Now:HH:mm:ss}</b></p>");
            sb.Append($"<p>Fichiers en cours : <b>{live.Count}</b></p>");
            sb.Append($"<p>Historique total : <b>{history.Count}</b></p>");

            // ?? BOUTON "CONSULTER LA SAUVEGARDE" ??
            sb.Append("<div style='margin-top:20px; text-align:center;'>");
            sb.Append("<a class='button' href='/backup'>Consulter la sauvegarde</a>");
            sb.Append("</div>");

            sb.Append("<h2>Lecture en cours</h2>");
            sb.Append("<table><tr><th>Client</th><th>Type</th><th>Nom</th><th>Fichier</th></tr>");

            foreach (var item in live)
            {
                sb.Append("<tr>");
                sb.Append($"<td>{item.ClientName}</td>");
                sb.Append($"<td>{item.MediaType}</td>");
                sb.Append($"<td>{item.Nom}</td>");
                sb.Append($"<td>{item.FileName}</td>");
                sb.Append("</tr>");
            }

            sb.Append("</table>");

            sb.Append("<h2>Historique</h2>");
            sb.Append("<table><tr><th>Heure</th><th>Client</th><th>Type</th><th>Nom</th><th>Fichier</th></tr>");

            foreach (var item in history)
            {
                sb.Append("<tr>");
                sb.Append($"<td>{item.Timestamp:HH:mm:ss}</td>");
                sb.Append($"<td>{item.ClientName}</td>");
                sb.Append($"<td>{item.MediaType}</td>");
                sb.Append($"<td>{item.Nom}</td>");
                sb.Append($"<td>{item.FileName}</td>");
                sb.Append("</tr>");
            }

            sb.Append("</table>");

            sb.Append("</body></html>");

            return sb.ToString();
        }

        // ==========================
        //  PAGE /backup
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
<style>
body { background:#111; color:#eee; font-family:Arial; text-align:center; padding-top:50px; }
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
                return @"
<html>
<head>
<meta charset='UTF-8'>
<title>MediaMonitor – Sauvegarde</title>
<style>
body { background:#111; color:#eee; font-family:Arial; text-align:center; padding-top:50px; }
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
            var allItems = JsonSerializer.Deserialize<List<BackupItem>>(json) ?? new List<BackupItem>();
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
    <td>{item.Timestamp:yyyy-MM-dd HH:mm}</td>
</tr>");
            }
string buttons = @"
<div style='text-align:center; margin:20px 0;'>

    <a href='/backup?save=1' 
       style='display:inline-block; padding:10px 20px; background:#4CAF50; 
              color:white; text-decoration:none; border-radius:6px; 
              font-weight:bold; margin-right:15px;'>
        Mettre a jour la sauvegarde
    </a>

    <a href='/download-backup' 
       style='display:inline-block; padding:10px 20px; background:#9C27B0; 
              color:white; text-decoration:none; border-radius:6px; 
              font-weight:bold; margin-right:15px;'>
        Télécharger la sauvegarde
    </a>

    <a href='/' 
       style='display:inline-block; padding:10px 20px; background:#0078D4; 
              color:white; text-decoration:none; border-radius:6px; 
              font-weight:bold;'>
        Retour
    </a>

</div>
";

            string html = buttons + BackupHtmlTemplate
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
                .Replace("{{SEL_DATE_30}}", date == "30" ? "selected" : "");

            return html;
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

        private const string BackupHtmlTemplate = @"
<!DOCTYPE html>
<html lang='fr'>
<head>
<meta charset='UTF-8'>
<title>Historique sauvegardé - MediaMonitor</title>
<style>
body { margin:0; padding:20px; font-family:Segoe UI,Arial; background:#1e1e1e; color:#e5e5e5; }
h1 { margin:0 0 20px 0; font-size:20px; color:#fff; }
.container { display:flex; gap:20px; }
.groupbox { flex:1; border:1px solid #3c3c3c; border-radius:6px; background:#252526; padding:12px; }
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
</style>
</head>
<body>

<h1>Historique sauvegardé</h1>

<div style='margin-bottom:15px; display:flex; gap:20px; flex-wrap:wrap;'>

    <div>
        <label>Filtrer par type :</label>
        <select onchange='location.href=""?type="" + this.value + ""&client={{FILTER_CLIENT}}&date={{DATE}}&sort={{SORT}}"";'>
            <option value='all' {{SEL_ALL}}>Tous</option>
            <option value='audio' {{SEL_AUDIO}}>Audio</option>
            <option value='serie' {{SEL_SERIE}}>Séries</option>
            <option value='video' {{SEL_VIDEO}}>Vidéos</option>
        </select>
    </div>

    <div>
        <label>Filtrer par client :</label>
        <select onchange='location.href=""?type={{FILTER_TYPE}}&client="" + this.value + ""&date={{DATE}}&sort={{SORT}}"";'>
            {{CLIENT_OPTIONS}}
        </select>
    </div>

    <div>
        <label>Filtrer par date :</label>
        <select onchange='location.href=""?type={{FILTER_TYPE}}&client={{FILTER_CLIENT}}&date="" + this.value + ""&sort={{SORT}}"";'>
            <option value='all' {{SEL_DATE_ALL}}>Tout</option>
            <option value='today' {{SEL_DATE_TODAY}}>Aujourd’hui</option>
            <option value='yesterday' {{SEL_DATE_YESTERDAY}}>Hier</option>
            <option value='7' {{SEL_DATE_7}}>7 jours</option>
            <option value='30' {{SEL_DATE_30}}>30 jours</option>
        </select>
    </div>

    <div>
        <label>Trier :</label>
        <select onchange='location.href=""?type={{FILTER_TYPE}}&client={{FILTER_CLIENT}}&date={{DATE}}&sort="" + this.value;'>
            <option value='date_desc' {{SEL_DATEDESC}}>Date ?</option>
            <option value='date_asc' {{SEL_DATEASC}}>Date ?</option>
            <option value='name_asc' {{SEL_NAMEASC}}>Nom A?Z</option>
            <option value='name_desc' {{SEL_NAMEDESC}}>Nom Z?A</option>
        </select>
    </div>

</div>

<div class='container'>

<div class='groupbox'>
    <div class='groupbox-title'>Statistiques</div>
    <div class='stats-grid'>
        <div class='label'>Titres lus :</div><div class='value'>{{TOTAL}}</div>
        <div class='label'>Audio :</div><div class='value'>{{AUDIO}}</div>
        <div class='label'>Séries :</div><div class='value'>{{SERIES}}</div>
        <div class='label'>Vidéos :</div><div class='value'>{{VIDEOS}}</div>
    </div>
</div>

<div class='groupbox'>
    <div class='groupbox-title'>Listing des titres</div>
    <div class='listing-header'>
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

</body>
</html>";
    }
}

