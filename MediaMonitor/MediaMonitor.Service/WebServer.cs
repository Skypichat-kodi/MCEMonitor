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
using System.Text.RegularExpressions;
using MediaMonitor.Core.Language;

namespace MediaMonitor.Service
{
    public class WebServer
    {
        // ======================================================================
        //  TRADUCTION HTML VIA REGEX {tr:...}
        // ======================================================================
                
        // Regex compilée pour détecter {tr}
        private static readonly Regex TrHtmlRegex =
            new(@"\{\{tr:(.+?)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Fonction de traduction HTML
        private string TranslateHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            return TrHtmlRegex.Replace(html, match =>
            {
                string key = match.Groups[1].Value.Trim();

                // Appel à ton système de traduction existant
                string translated = LanguageManager.Get(key) ?? key;

                // Si la clé n'existe pas ? on garde la clé brute
                return string.IsNullOrEmpty(translated) ? key : translated;
            });
        }

        // ======================================================================
        //  CHAMPS PRIVÉS DU SERVEUR WEB
        // ======================================================================

        private readonly HttpListener _listener = new();
        private readonly MediaMonitorEngine _engine;
        private readonly int _port;
        private Thread _thread;
        private bool _running = false;
        private long _requestCount = 0;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private string _lastRequestIp = "N/A";

        // ======================================================================
        //  CONSTRUCTEUR + GESTION DU DÉMARRAGE DU SERVEUR
        // ======================================================================

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

        // ======================================================================
        //  ARRÊT DU SERVEUR + BOUCLE PRINCIPALE + ROUTAGE DES REQUÊTES
        // ======================================================================

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

                // Authentification Basic
                if (!CheckAuth(ctx))
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"MediaMonitor\"");
                    ctx.Response.Close();
                    return;
                }

                // ============================================================
                // ?? Gestion de la langue 
                // ============================================================
                string lang = ctx.Request.QueryString["lang"];
                if (!string.IsNullOrEmpty(lang))
                {
                    LanguageManager.Load(lang);
                }

                string path = ctx.Request.Url.AbsolutePath.ToLower();

                // Favicon
                if (path == "/favicon.ico")
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

                    case "/info":
                    {
                        ctx.Response.ContentEncoding = Encoding.UTF8;
                        ctx.Response.ContentType = "text/html; charset=utf-8";

                        // 1. Lire le paramètre "path"
                        string filePath = ctx.Request.QueryString["path"];
                        filePath = WebUtility.UrlDecode(filePath);

                        // 2. Analyser le fichier
                        var info = FileAnalyzer.Analyze(filePath);

                        // ============================================================
                        //    Détermination de l'icône selon le type de média (Base64)
                        // ============================================================

                        string iconBasePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons");
                        string iconFile = info.MediaType switch
                        {
                            "Audio" => "webicon_audio.png",
                            "Video" => "webicon_video.png",
                            "Image" => "webicon_image.png",
                            _ => "icon_file.png"
                        };

                        string iconFullPath = Path.Combine(iconBasePath, iconFile);

                        // Charger l'icône et la convertir en Base64
                        byte[] iconBytes = File.ReadAllBytes(iconFullPath);
                        string iconBase64 = "data:image/png;base64," + Convert.ToBase64String(iconBytes);

                        // 3. Charger le template UTF-8
                        string templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "InfoPage.html");
                        string html = File.ReadAllText(templatePath, Encoding.UTF8);

                        // 4. Champs simples
                        html = html.Replace("{{IconPath}}", iconBase64);
                        html = html.Replace("{{FileName}}", WebUtility.HtmlEncode(info.FileName));
                        html = html.Replace("{{Title}}", WebUtility.HtmlEncode(info.Title ?? info.FileName));
                        html = html.Replace("{{Path}}", WebUtility.HtmlEncode(info.Path));
                        html = html.Replace("{{MediaType}}", WebUtility.HtmlEncode(info.MediaType));

                        // Taille
                        var fi = new FileInfo(info.Path);
                        html = html.Replace("{{SizeMB}}", (fi.Length / 1024.0 / 1024.0).ToString("F2"));

                        // Durée
                        string durationText = info.Duration > 0
                            ? TimeSpan.FromSeconds(info.Duration).ToString(@"hh\:mm\:ss")
                            : "—";
                        html = html.Replace("{{DurationText}}", durationText);

                        // Miniature (image par défaut si rien)
                        string coverBase64;

                        if (info.AlbumArt != null && info.AlbumArt.Length > 0)
                        {
                            coverBase64 = "data:image/jpeg;base64," + Convert.ToBase64String(info.AlbumArt);
                        }
                        else
                        {
                            string defaultCoverPath = Path.Combine(AppContext.BaseDirectory, "Templates", "default-cover.png");
                            byte[] defaultBytes = File.ReadAllBytes(defaultCoverPath);
                            coverBase64 = "data:image/png;base64," + Convert.ToBase64String(defaultBytes);
                        }

                        html = html.Replace("{{AlbumArtBase64}}", coverBase64);

                        // Vidéo
                        html = html.Replace("{{SeriesName}}", WebUtility.HtmlEncode(info.SeriesName ?? ""));
                        html = html.Replace("{{EpisodeName}}", WebUtility.HtmlEncode(info.EpisodeName ?? ""));
                        html = html.Replace("{{Saison}}", info.Saison.ToString());
                        html = html.Replace("{{Episode}}", info.Episode.ToString());
                        html = html.Replace("{{VideoCodec}}", WebUtility.HtmlEncode(info.VideoCodec ?? ""));
                        html = html.Replace("{{AudioCodec}}", WebUtility.HtmlEncode(info.AudioCodec ?? ""));

                        // Audio
                        html = html.Replace("{{TitleTag}}", WebUtility.HtmlEncode(info.Title ?? ""));
                        html = html.Replace("{{Artist}}", WebUtility.HtmlEncode(info.Artist ?? ""));
                        html = html.Replace("{{Album}}", WebUtility.HtmlEncode(info.Album ?? ""));
                        html = html.Replace("{{Year}}", info.Year > 0 ? info.Year.ToString() : "—");
                        html = html.Replace("{{Track}}", info.Track > 0 ? info.Track.ToString() : "—");
                        html = html.Replace("{{Genre}}", WebUtility.HtmlEncode(info.Genre ?? ""));

                        // 5. Blocs conditionnels Mustache
                        html = ApplyConditional(html, "IfDuration", info.Duration > 0);
                        html = ApplyConditional(html, "IfVideo", info.MediaType == "Video");
                        html = ApplyConditional(html, "IfAudio", info.MediaType == "Audio");
                        html = ApplyConditional(html, "IfSeries", !string.IsNullOrEmpty(info.SeriesName));
                        html = ApplyConditional(html, "IfMovie", info.MediaType == "Video" && string.IsNullOrEmpty(info.SeriesName));
                        html = ApplyConditional(html, "IfSeasonEpisode", info.Saison > 0 || info.Episode > 0);
                        html = ApplyConditional(html, "IfEpisodeName", !string.IsNullOrEmpty(info.EpisodeName));
                        html = ApplyConditional(html, "IfVideoCodec", !string.IsNullOrEmpty(info.VideoCodec));
                        html = ApplyConditional(html, "IfAudioCodec", !string.IsNullOrEmpty(info.AudioCodec));

                        // Traduction
                        html = HTMLTranslator.Translate(html);

                        // 6. Envoyer la page remplie
                        SendHtml(ctx, html);
                        break;
                    }

                    if (path.StartsWith("/resources/icons/"))
                    {
                        string fileName = Path.GetFileName(path);

                        string fullPath = Path.Combine(@"C:\ProgramData\MCEMonitor\Resources\Icons", fileName);

                        if (File.Exists(fullPath))
                        {
                            byte[] bytes = File.ReadAllBytes(fullPath);
                            ctx.Response.ContentType = "image/png";
                            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                            ctx.Response.Close();
                            return;
                        }

                        ctx.Response.StatusCode = 404;
                        ctx.Response.Close();
                        return;
                    }

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

        // ==========================
        //  AUTHENTIFICATION BASIC
        // ==========================
        private bool CheckAuth(HttpListenerContext ctx)
        {
            string auth = ctx.Request.Headers["Authorization"];

            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Basic "))
                return false;

            try
            {
                string encoded = auth.Substring("Basic ".Length).Trim();
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

                return decoded == $"{_settings.Username}:{_settings.Password}";
            }
            catch
            {
                return false;
            }
        }

        // ==========================
        //  ENVOI JSON / HTML
        // ==========================
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

        // ==========================
        //  BADGE TYPE
        // ==========================
        private string GetTypeBadgeClass(string? mediaType)
        {
            return (mediaType ?? "").ToLower() switch
            {
                "audio" => "type-audio",
                "serie" => "type-serie",
                "video" => "type-video",
                "rec"   => "type-rec",
                "tv"    => "type-tv",
                _       => ""
            };
        }

        // ==========================
        //  EXTRACTION / PARSING
        // ==========================
        private (string Serie, string Episode) ExtractSerie(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom))
                return ("", "");

            var parts = nom.Split(" - ", 2, StringSplitOptions.TrimEntries);

            if (parts.Length == 2)
                return (parts[0], parts[1]);

            return (nom, "");
        }
        private (string Track, string Artiste, string Titre) ExtractAudio(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom))
                return ("", "", "");

            var parts = nom.Split(" - ", StringSplitOptions.TrimEntries);

            // Cas 1 : Track - Artiste - Titre
            if (parts.Length >= 3 && int.TryParse(parts[0], out _))
            {
                return (parts[0], parts[1], string.Join(" - ", parts.Skip(2)));
            }

            // Cas 2 : Artiste - Titre
            if (parts.Length >= 2)
            {
                return ("", parts[0], string.Join(" - ", parts.Skip(1)));
            }

            // Cas 3 : Titre seul
            return ("", "", nom);
        }

        // ==========================
        //  STATS AVANCÉES (pour /backup)
        // ==========================
        private List<(string Serie, int Count)> GetTopSeries(List<BackupItem> items)
        {
            return items
                .Where(i => (i.MediaType ?? "").Equals("serie", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(i.Nom))
                .Select(i => ExtractSerie(i.Nom!).Serie)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .GroupBy(s => s)
                .Select(g => (Serie: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();
        }

        private List<(string Artiste, int Count)> GetTopArtistes(List<BackupItem> items)
        {
            return items
                .Where(i => (i.MediaType ?? "").Equals("audio", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(i.Nom))
                .Select(i => ExtractAudio(i.Nom!).Artiste)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .GroupBy(a => a)
                .Select(g => (Artiste: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();
        }

        private List<(string Client, int Count)> GetTopClientsStats(List<BackupItem> items)
        {
            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.ClientDisplay))
                .GroupBy(i => i.ClientDisplay!)
                .Select(g => (Client: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();
        }

        // =================================
        //  RECHERCHE DERNIER RAPPORT ENVOYÉ
        // =================================
        private DateTime GetLastReportTime()
        {
            string path = @"C:\ProgramData\MCEMonitor\Logs\MediaMonitor.Schedule.log";

            if (!File.Exists(path))
                return DateTime.MinValue;

            // On cherche la DERNIÈRE ligne contenant "Rapport envoyé"
            string lastLine = File.ReadLines(path)
                                  .LastOrDefault(l => l.Contains("Rapport envoyé"));

            if (lastLine == null)
                return DateTime.MinValue;

            // Exemple :
            // [2026-06-21 11:47:01] [CODE02] Rapport envoyé à 2026-06-21 11:47:01

            int idx = lastLine.IndexOf("à ");
            if (idx < 0)
                return DateTime.MinValue;

            string datePart = lastLine.Substring(idx + 2).Trim();

            if (DateTime.TryParse(datePart, out var dt))
                return dt;

            return DateTime.MinValue;
        }

        // =================================
        //  RECHERCHE PROCHAIN RAPPORT PRÉVU
        // =================================
        private DateTime GetReportSendTime()
        {
            string path = @"C:\ProgramData\MCEMonitor\Logs\MediaMonitor.Schedule.log";

            if (!File.Exists(path))
                return DateTime.MinValue;

            string lastLine = File.ReadLines(path)
                                  .LastOrDefault(l => l.Contains("Prochain envoi"));

            if (lastLine == null)
                return DateTime.MinValue;

            int idx = lastLine.IndexOf("prévu à ");
            if (idx < 0)
                return DateTime.MinValue;

            string timePart = lastLine.Substring(idx + "prévu à ".Length, 5);

            if (TimeSpan.TryParse(timePart, out var ts))
            {
                DateTime next = DateTime.Today.Add(ts);
                if (next <= DateTime.Now)
                    next = next.AddDays(1);
                return next;
            }

            return DateTime.MinValue;
        }       
        
        // ======================================================================
        //  PAGE PRINCIPALE – TEMPLATE HTML + CSS
        // ======================================================================

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
        <title>{{tr:MediaMonitor – Tableau de bord}}</title>
        <link rel=""icon"" type=""image/x-icon"" href=""/favicon.ico"">
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

        table:nth-of-type(1) td:nth-child(2),
        table:nth-of-type(1) th:nth-child(2) {
            text-align: center;
        }

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
        //  PAGE PRINCIPALE – OVERLAY + AUTO-REFRESH + BARRE D’ACTIONS + STATISTIQUES
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

        sb.Append(@"
                <div class='button-bar'>
                    <a href='/' class='button button-secondary'>{{tr:Rafraîchir}}</a>
                    <a href='/backup' class='button'>{{tr:Voir le backup}}</a>
                    <a href='/download' class='button'>{{tr:Télécharger le backup}}</a>
                    <a href='/purge' class='button button-danger' onclick='return confirm(""{{tr:Voulez-vous vraiment supprimer TOUTES les sauvegardes}} ?"");'>{{tr:Purger les sauvegardes}}</a>
                </div>
                ");

        sb.Append("<div class='container'>");

        // Statut du service
        sb.Append("<div class='groupbox'>");
        sb.Append("<div class='groupbox-title'>{{tr:Statut du service}}</div>");
        sb.Append("<div class='stats-grid'>");
        sb.Append("<div class='label'>{{tr:Serveur}} :</div><div class='value'>" + WebUtility.HtmlEncode(Environment.MachineName) + "</div>");
        sb.Append("<div class='label'>{{tr:Heure actuelle}} :</div><div class='value'>" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");
        sb.Append("</div>");
        sb.Append("</div>");

        // Rapports
        sb.Append("<div class='groupbox'>");
        sb.Append("<div class='groupbox-title'>{{tr:Rapports}}</div>");
        sb.Append("<div class='stats-grid'>");
        var lastReport = GetLastReportTime();
        sb.Append("<div class='label'>{{tr:Dernier rapport :}}</div><div class='value'>" + (lastReport == DateTime.MinValue ? "{{tr:Aucun rapport envoyé}}" : lastReport.ToString("yyyy-MM-dd HH:mm:ss")) + "</div>");
        var next = GetReportSendTime();
        sb.Append("<div class='label'>{{tr:Prochain envoi :}}</div><div class='value'>" + next.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");
        sb.Append("</div>");
        sb.Append("</div>");

        // Lecture en cours
        sb.Append("<div class='groupbox'>");
        sb.Append("<div class='groupbox-title'>{{tr:Lecture en cours}}</div>");
        sb.Append("<div class='stats-grid'>");
        sb.Append("<div class='label'>{{tr:Fichiers ouverts}} :</div><div class='value'>" + liveCount + "</div>");
        sb.Append("<div class='label'>{{tr:Utilisateurs actifs}} :</div><div class='value'>" + live.Select(x => x.ClientDisplay).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count() + "</div>");
        sb.Append("</div>");
        sb.Append("</div>");

        // Historique
        sb.Append("<div class='groupbox'>");
        sb.Append("<div class='groupbox-title'>{{tr:Historique}}</div>");
        sb.Append("<div class='stats-grid'>");
        sb.Append("<div class='label'>{{tr:Événements totaux}} :</div><div class='value'>" + historyCount + "</div>");
        sb.Append("<div class='label'>{{tr:Sur 24h}} :</div><div class='value'>" + history24h + "</div>");
        if (historyCount > 0)
        {
            var last = history.Last();
            sb.Append("<div class='label'>{{tr:Dernier événement}} :</div>");
            sb.Append("<div class='value'>" + last.Timestamp.ToString("HH:mm:ss") + " – " + WebUtility.HtmlEncode(last.MediaType) + " (" + WebUtility.HtmlEncode(last.ClientDisplay) + ")</div>");
        }
        sb.Append("</div>");
        sb.Append("</div>");

        // WebServer
        sb.Append("<div class='groupbox'>");
        sb.Append("<div class='groupbox-title'>{{tr:WebServer}}</div>");
        sb.Append("<div class='stats-grid'>");
        sb.Append("<div class='label'>{{tr:Port}} :</div><div class='value'>" + _port + "</div>");
        sb.Append("<div class='label'>{{tr:Requêtes}} :</div><div class='value'>" + _requestCount + "</div>");
        sb.Append("<div class='label'>{{tr:Dernière requête}} :</div><div class='value'>" + (_lastRequestTime == DateTime.MinValue ? "N/A" : _lastRequestTime.ToString("HH:mm:ss")) + "</div>");
        sb.Append("<div class='label'>{{tr:Dernier client}} :</div><div class='value'>" + WebUtility.HtmlEncode(_lastRequestIp) + "</div>");
        sb.Append("</div>");
        sb.Append("</div>");

        sb.Append("</div>"); // .container

        // === TABLEAU LECTURE EN COURS ===
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

        // ======================================================================
        //  TABLEAU LECTURE EN COURS + TABLEAU HISTORIQUE
        // ======================================================================

        foreach (var item in live)
        {
            string mediaType = item.MediaType ?? "";
            string badgeClass = GetTypeBadgeClass(mediaType);

            string canal = item.Channel ?? "";
            string titreAffiche = item.Nom ?? "";
            int saisonAffiche = item.Saison;
            int episodeAffiche = item.Episode;

            sb.Append("<tr>");
            sb.Append($"<td>{WebUtility.HtmlEncode(item.ClientDisplay ?? "")}</td>");
            sb.Append($"<td><span class=\"type-badge {badgeClass}\">{WebUtility.HtmlEncode(mediaType)}</span></td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(canal)}</td>");
            sb.Append($"<td>{(saisonAffiche > 0 ? saisonAffiche.ToString() : "")}</td>");
            sb.Append($"<td>{(episodeAffiche > 0 ? episodeAffiche.ToString() : "")}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(titreAffiche)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(item.FileName ?? "")}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(item.Path ?? "")}</td>");
            sb.Append($"<td><a class=\"info-btn\" href=\"#\" data-path=\"{WebUtility.HtmlEncode(item.Path)}\" onclick=\"openInfo(this.dataset.path)\">I</a></td>");
            sb.Append("</tr>");
        }

        if (liveCount == 0)
            sb.Append("<tr><td colspan='9' class='small'>{{tr:Aucune lecture en cours.}}</td></tr>");

        sb.Append("</tbody></table>");

        // === TABLEAU HISTORIQUE ===
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
            string badgeClass = GetTypeBadgeClass(mediaType);

            string canal = item.Channel ?? "";
            string titreAffiche = item.Nom ?? "";
            int saisonAffiche = item.Saison;
            int episodeAffiche = item.Episode;

            sb.Append("<tr>");
            sb.Append($"<td>{item.Timestamp:HH:mm:ss}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(item.ClientDisplay ?? "")}</td>");
            sb.Append($"<td><span class=\"type-badge {badgeClass}\">{WebUtility.HtmlEncode(mediaType)}</span></td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(canal)}</td>");
            sb.Append($"<td>{(saisonAffiche > 0 ? saisonAffiche.ToString() : "")}</td>");
            sb.Append($"<td>{(episodeAffiche > 0 ? episodeAffiche.ToString() : "")}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(titreAffiche)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(item.FileName ?? "")}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(item.Path ?? "")}</td>");
            sb.Append($"<td><a class=\"info-btn\" href=\"#\" data-path=\"{WebUtility.HtmlEncode(item.Path)}\" onclick=\"openInfo(this.dataset.path)\">I</a></td>");
            sb.Append("</tr>");
        }

        if (historyCount == 0)
            sb.Append("<tr><td colspan='10' class='small'>{{tr:Aucun événement.}}</td></tr>");

        sb.Append("</tbody></table>");

        // === SCRIPT FINAL UNIQUE ===
        sb.Append("</tbody></table>");

        // ======================================================================
        //  SCRIPT POPUP INFO + FERMETURE + TRADUCTION + RETOUR HTML
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

                                    // Exécuter les scripts du popup
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

        sb.Append("</body></html>");

        var html = sb.ToString();
        html = HTMLTranslator.Translate(html);
        return html;
        }

        // ======================================================================
        //  PAGE BACKUP (MODERNE) – CHARGEMENT, FILTRES, TRI, STATISTIQUES
        // ======================================================================

        private string BuildBackupPage(HttpListenerRequest req)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";

            if (!Directory.Exists(folder))
            {
                string translated =
                    "<html><body style='background:#111; color:#eee; font-family:Segoe UI; padding:40px;'>" +
                    "<h2>{{tr:Aucune sauvegarde trouvée}}</h2>" +
                    "<a href='/' style='color:#4fc3f7;'>{{tr:Retour}}</a></body></html>";

                return HTMLTranslator.Translate(translated);
            }

            var files = Directory.GetFiles(folder, "history_*.json");

            if (files.Length == 0)
            {
                return HTMLTranslator.Translate(@"
                <html>
                <head>
                    <meta charset='utf-8'>
                    <title>{{tr:Aucune sauvegarde}}</title>
                    <style>
                        body {
                            background-color: #1e1e1e;
                            color: #ffffff;
                            font-family: Segoe UI, Arial, sans-serif;
                            margin: 0;
                            padding: 40px;
                        }
                        .container {
                            max-width: 700px;
                            margin: auto;
                            background: #2b2b2b;
                            padding: 25px;
                            border-radius: 8px;
                            box-shadow: 0 0 10px #000;
                            text-align: center;
                        }
                        h2 {
                            color: #f55;
                        }
                        a.btn {
                            display: inline-block;
                            margin-top: 20px;
                            padding: 10px 18px;
                            background: #444;
                            color: white;
                            text-decoration: none;
                            border-radius: 5px;
                            font-weight: bold;
                        }
                        a.btn:hover {
                            background: #666;
                        }
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <h2>{{tr:Aucune sauvegarde disponible}}</h2>
                        <a href='/' class='btn'>{{tr:Retour}}</a>
                    </div>
                </body>
                </html>");
            }

            string lastFile = files.OrderByDescending(f => f).First();
            string json = File.ReadAllText(lastFile);

            BackupFileModel? backup = JsonSerializer.Deserialize<BackupFileModel>(json);

            if (backup == null || backup.Reports == null)
                return "<html><body><h2>Sauvegarde invalide.</h2></body></html>";

            // Fusionner les rapports du même jour
            backup.Reports = backup.Reports
                .GroupBy(r => r.Date.Date)
                .Select(g => new DailyReport
                {
                    Date = g.Key,
                    Items = g.SelectMany(r => r.Items ?? new List<BackupItem>()).ToList()
                })
                .ToList();

            // Aplatir
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
                "rec"   => items.Where(i => i.MediaType.Equals("REC",   StringComparison.OrdinalIgnoreCase)).ToList(),
                "tv"    => items.Where(i => i.MediaType.Equals("TV",    StringComparison.OrdinalIgnoreCase)).ToList(),
                _ => items
            };

            // Filtre client
            if (client != "all")
            {
                items = items.Where(i => i.ClientDisplay != null &&
                                         i.ClientDisplay.Equals(client, StringComparison.OrdinalIgnoreCase)).ToList();
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

            // Stats simples
            int total = items.Count;
            int audio = items.Count(i => i.MediaType.Equals("Audio", StringComparison.OrdinalIgnoreCase));
            int series = items.Count(i => i.MediaType.Equals("Serie", StringComparison.OrdinalIgnoreCase));
            int videos = items.Count(i => i.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase));
            int recCount = items.Count(i => i.MediaType.Equals("REC", StringComparison.OrdinalIgnoreCase));
            int tvCount  = items.Count(i => i.MediaType.Equals("TV",  StringComparison.OrdinalIgnoreCase));

            // ======================================================================
            //  PAGE BACKUP (MODERNE) – LISTE CLIENTS + TABLEAU + STATISTIQUES
            // ======================================================================

            // Liste des clients
            var allClients = allItems
                .Where(i => !string.IsNullOrWhiteSpace(i.ClientDisplay))
                .Select(i => i.ClientDisplay!)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            var clientOptions = new StringBuilder();

            string allLabel = LanguageManager.Get("Tous") ?? "Tous";

            clientOptions.Append(
                $"<option value='all' {(client == "all" ? "selected" : "")}>{allLabel}</option>"
            );

            foreach (var c in allClients)
            {
                string val = c.ToLower();
                string selected = (val == client) ? "selected" : "";
                clientOptions.Append(
                    $"<option value='{WebUtility.HtmlEncode(val)}' {selected}>{WebUtility.HtmlEncode(c)}</option>"
                );
            }

            // Lignes du tableau principal
            var rows = new StringBuilder();

            foreach (var item in items)
            {
                string mediaType = item.MediaType ?? "";

                string badgeClass = mediaType.ToLower() switch
                {
                    "audio" => "type-audio",
                    "serie" => "type-serie",
                    "video" => "type-video",
                    "rec"   => "type-rec",
                    "tv"    => "type-tv",
                    _       => ""
                };

                string canal = item.Channel ?? "";
                string titreAffiche = item.Nom ?? "";
                int saisonAffiche = item.Saison;
                int episodeAffiche = item.Episode;

                rows.Append($@"
                    <tr>
                        <td style=""text-align:center;"">
                            <span class=""type-badge {badgeClass}"">{WebUtility.HtmlEncode(mediaType)}</span>
                        </td>

                        <td style=""text-align:left;"">
                            {WebUtility.HtmlEncode(titreAffiche)}
                        </td>

                        <td>{(saisonAffiche > 0 ? saisonAffiche.ToString() : "")}</td>
                        <td>{(episodeAffiche > 0 ? episodeAffiche.ToString() : "")}</td>

                        <td>{WebUtility.HtmlEncode(canal)}</td>
                        <td>{WebUtility.HtmlEncode(item.ClientDisplay ?? "")}</td>

                        <td>{item.Timestamp:dd/MM/yyyy HH:mm}</td>

                        <td style=""text-align:right;"">
                            <a class=""info-btn""
                               href=""#""
                               data-path=""{WebUtility.HtmlEncode(item.Path)}""
                               onclick=""openInfo(this.dataset.path)"">I</a>
                        </td>
                    </tr>");
            }

            // Activité par heure
            int[] hours = new int[24];
            foreach (var it in items)
                hours[it.Timestamp.Hour]++;

            // Activité par heure — séparée par type
            int[] hoursAudio = new int[24];
            int[] hoursSeries = new int[24];
            int[] hoursVideo = new int[24];
            int[] hoursRec = new int[24];
            int[] hoursTv = new int[24];

            foreach (var it in items)
            {
                int h = it.Timestamp.Hour;

                switch ((it.MediaType ?? "").ToLowerInvariant())
                {
                    case "audio": hoursAudio[h]++; break;
                    case "serie": hoursSeries[h]++; break;
                    case "video": hoursVideo[h]++; break;
                    case "rec":   hoursRec[h]++; break;
                    case "tv":    hoursTv[h]++; break;
                }
            }

            // Construction du tableau structuré pour le graphique
            var hoursData = new object[24];

            for (int h = 0; h < 24; h++)
            {
                hoursData[h] = new {
                    audio = hoursAudio[h],
                    serie = hoursSeries[h],
                    video = hoursVideo[h],
                    rec   = hoursRec[h],
                    tv    = hoursTv[h]
                };
            }

            string hoursJson = JsonSerializer.Serialize(hoursData);

// --- STATISTIQUES PAR CLIENT POUR LE GRAPHIQUE ---

// On exclut les tuners DVB-T qui polluent les graphiques
var filteredClients = allClients
    .Where(c => !c.StartsWith("DVB-T", StringComparison.OrdinalIgnoreCase))
    .ToList();

var clientMediaData = new List<object>();

foreach (var clientName in filteredClients)
{
    // IMPORTANT : utiliser "items" (médias filtrés par date/type/client)
    var clientItems = items.Where(i =>
        i.ClientDisplay != null &&
        i.ClientDisplay.Equals(clientName, StringComparison.OrdinalIgnoreCase)
    );

    clientMediaData.Add(new {
        client = clientName,
        audio = clientItems.Count(i => i.MediaType.Equals("Audio", StringComparison.OrdinalIgnoreCase)),
        serie = clientItems.Count(i => i.MediaType.Equals("Serie", StringComparison.OrdinalIgnoreCase)),
        video = clientItems.Count(i => i.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase)),
        rec   = clientItems.Count(i => i.MediaType.Equals("REC",   StringComparison.OrdinalIgnoreCase)),
        tv    = clientItems.Count(i => i.MediaType.Equals("TV",    StringComparison.OrdinalIgnoreCase))
    });
}

string clientMediaJson = JsonSerializer.Serialize(clientMediaData);

            // STATISTIQUES AVANCÉES
            var topSeries = GetTopSeries(allItems);
            var topArtistes = GetTopArtistes(allItems);
            var topClients = GetTopClientsStats(allItems);
            var mediaStats = GetMediaStatsPerClient(allItems);
            var mediaStatsHtml = BuildMediaStatsPerClientHtml(mediaStats);

            // TEMPLATE FINAL
            string html = BackupHtmlTemplate
                .Replace("{{TOTAL}}", total.ToString())
                .Replace("{{AUDIO}}", audio.ToString())
                .Replace("{{SERIES}}", series.ToString())
                .Replace("{{VIDEOS}}", videos.ToString())
                .Replace("{{REC}}", recCount.ToString())
                .Replace("{{TV}}", tvCount.ToString())
                .Replace("{{COUNT}}", total.ToString())
                .Replace("{{PERIOD}}", WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(lastFile)))
                .Replace("{{ROWS}}", rows.ToString())
                .Replace("{{CLIENT_OPTIONS}}", clientOptions.ToString())
                .Replace("{{FILTER_TYPE}}", type)
                .Replace("{{FILTER_CLIENT}}", client)
                .Replace("{{DATE}}", date)
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
                .Replace("{{HOURS_DATA}}", hoursJson)
                .Replace("{{CLIENT_MEDIA_DATA}}", clientMediaJson)
                .Replace("{{TOP_SERIES_ROWS}}", BuildTopSeriesRows(topSeries))
                .Replace("{{TOP_ARTISTES_ROWS}}", BuildTopArtistesRows(topArtistes))
                .Replace("{{TOP_CLIENTS_ROWS}}", BuildTopClientsRows(topClients))
                .Replace("{{TOP_MEDIA_PER_CLIENT}}", mediaStatsHtml);

            // Traduction
            html = HTMLTranslator.Translate(html);

            return html;
        }

        // ==========================
        //  TEMPLATE HTML BACKUP
        // ==========================
        private const string BackupHtmlTemplate = @"
        <!DOCTYPE html>
        <html lang=""fr"">
        <head>
        <meta charset=""UTF-8"">
        <title>{{tr:Historique sauvegardé}} - MediaMonitor</title>
        <link rel=""icon"" type=""image/x-icon"" href=""/favicon.ico"">

        <!-- Chart.js -->
        <script src=""https://cdn.jsdelivr.net/npm/chart.js""></script>

        <style>
            body { margin:0; padding:20px; font-family:Segoe UI,Arial; background:#1e1e1e; color:#e5e5e5; }
            h1 { margin:0 0 20px 0; font-size:20px; color:#fff; }
            .container { display:flex; gap:20px; }

            .groupbox {
                flex:1;
                border:1px solid #3c3c3c;
                border-radius:6px;
                background:#252526;
                padding:12px;
                position:relative;
                transition: all 0.8s ease;
            }

            .left-content { transition: opacity 0.6s ease; }
            .groupbox-title { font-weight:bold; margin-bottom:10px; color:#fff; }

            .stats-grid {
                display:grid;
                grid-template-columns:auto auto;
                row-gap:6px;
                column-gap:12px;
                font-size:13px;
            }

            .label { color:#ccc; }
            .value { font-weight:bold; color:#fff; }

            .listing-header {
                display:flex;
                justify-content:space-between;
                margin-bottom:8px;
                font-size:13px;
            }

            table { width:100%; border-collapse:collapse; font-size:13px; }
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

            select {
                background:#2d2d30;
                color:#e5e5e5;
                border:1px solid #3c3c3c;
                border-radius:4px;
                padding:2px 4px;
            }

            label { margin-right:4px; }

            td, th { border-right: 1px solid #3c3c3c; }
            td:last-child, th:last-child { border-right: none; }
            td:nth-child(2), th:nth-child(2) { text-align:center; }

            const clientData = {{CLIENT_MEDIA_DATA}};

            const labels = clientData.map(c => c.client);

            new Chart(document.getElementById('chartClients'), {
                type: 'bar',
                data: {
                    labels: labels,
                    datasets: [
                        {
                            label: '{{tr:Audio}}',
                            data: clientData.map(c => c.audio),
                            backgroundColor: colors.audio
                        },
                        {
                            label: '{{tr:Séries}}',
                            data: clientData.map(c => c.serie),
                            backgroundColor: colors.series
                        },
                        {
                            label: '{{tr:Vidéos}}',
                            data: clientData.map(c => c.video),
                            backgroundColor: colors.video
                        },
                        {
                            label: 'REC',
                            data: clientData.map(c => c.rec),
                            backgroundColor: colors.rec
                        },
                        {
                            label: 'TV',
                            data: clientData.map(c => c.tv),
                            backgroundColor: colors.tv
                        }
                    ]
                },
                options: {
                    responsive: true,
                    scales: {
                        x: { ticks: { color:'#fff' } },
                        y: { ticks: { color:'#fff' } }
                    },
                    plugins: {
                        legend: { labels: { color:'#fff' } }
                    }
                }
            });

            /* --- COLLAPSIBLE --- */
            #leftColumn {
                overflow: visible;
                transition: all 0.6s ease;
            }

            #leftColumn.collapsed {
                flex: 0 0 40px !important;
                max-width: 40px !important;
                min-width: 40px !important;
                padding: 12px 4px;
            }

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

        </head>
        <body>

        <!-- ====================================================================== -->
        <!--  BACKUP – BARRE D’ACTIONS + FILTRES -->
        <!-- ====================================================================== -->

        <h1>{{tr:Historique sauvegardé}}</h1>

        <div style=""margin-bottom:20px; display:flex; gap:10px;"">
            <a href=""/download"" style=""padding:6px 12px; background:#007acc; color:white; text-decoration:none; border-radius:4px;"">
                {{tr:Télécharger}}
            </a>

            <a href=""/purge""
               onclick=""return confirm('{{tr:Voulez-vous vraiment supprimer TOUTES les sauvegardes}} ?');""
               style=""padding:6px 12px; background:#cc3300; color:white; text-decoration:none; border-radius:4px;"">
               {{tr:Purger}}
            </a>

            <a href=""/"" style=""padding:6px 12px; background:#444; color:white; text-decoration:none; border-radius:4px;"">
                {{tr:Retour}}
            </a>
        </div>

        <!-- FILTRES -->
        <div style=""margin-bottom:15px; display:flex; gap:20px; flex-wrap:wrap;"">

            <div>
                <label>{{tr:Filtrer par type}} :</label>
                <select onchange=""location.href='?type=' + this.value + '&client={{FILTER_CLIENT}}&date={{DATE}}&sort={{SORT}}';"">
                    <option value='all' {{SEL_ALL}}>{{tr:Tous}}</option>
                    <option value='audio' {{SEL_AUDIO}}>{{tr:Audio}}</option>
                    <option value='serie' {{SEL_SERIE}}>{{tr:Séries}}</option>
                    <option value='video' {{SEL_VIDEO}}>{{tr:Vidéos}}</option>
                </select>
            </div>

            <div>
                <label>{{tr:Filtrer par client}} :</label>
                <select onchange=""location.href='?type={{FILTER_TYPE}}&client=' + this.value + '&date={{DATE}}&sort={{SORT}}';"">
                    {{CLIENT_OPTIONS}}
                </select>
            </div>

            <div>
                <label>{{tr:Filtrer par date}} :</label>
                <select onchange=""location.href='?type={{FILTER_TYPE}}&client={{FILTER_CLIENT}}&date=' + this.value + '&sort={{SORT}}';"">
                    <option value='all' {{SEL_DATE_ALL}}>{{tr:Tout}}</option>
                    <option value='today' {{SEL_DATE_TODAY}}>{{tr:Aujourd’hui}}</option>
                    <option value='yesterday' {{SEL_DATE_YESTERDAY}}>{{tr:Hier}}</option>
                    <option value='7' {{SEL_DATE_7}}>{{tr:7 jours}}</option>
                    <option value='30' {{SEL_DATE_30}}>{{tr:30 jours}}</option>
                </select>
            </div>

            <div>
                <label>{{tr:Trier}} :</label>
                <select onchange=""location.href='?type={{FILTER_TYPE}}&client={{FILTER_CLIENT}}&date={{DATE}}&sort=' + this.value;"">
                    <option value='date_desc' {{SEL_DATEDESC}}>{{tr:Date +}}</option>
                    <option value='date_asc' {{SEL_DATEASC}}>{{tr:Date -}}</option>
                    <option value='name_asc' {{SEL_NAMEASC}}>{{tr:Nom (A–Z)}}</option>
                    <option value='name_desc' {{SEL_NAMEDESC}}>{{tr:Nom (Z–A)}}</option>
                </select>
            </div>

        </div>

        <!-- ====================================================================== -->
        <!--  BACKUP – BARRE D’ACTIONS + FILTRES -->
        <!-- ====================================================================== -->

        <div class=""container"">

            <!-- COLONNE GAUCHE -->
            <div class=""groupbox"" id=""leftColumn"">
                <button class=""toggle-btn"" id=""toggleLeft"">&lt;</button>

                <div class=""left-content"">

                    <!-- STATISTIQUES -->
                    <div class=""groupbox-title"">{{tr:Statistiques}}</div>
                    <div class=""stats-grid"">
                        <div class=""label"">{{tr:Titres lus}} :</div><div class=""value"">{{TOTAL}}</div>
                        <div class=""label"">{{tr:Audio}} :</div><div class=""value"">{{AUDIO}}</div>
                        <div class=""label"">{{tr:Séries}} :</div><div class=""value"">{{SERIES}}</div>
                        <div class=""label"">{{tr:Vidéos}} :</div><div class=""value"">{{VIDEOS}}</div>
                    </div>

                    <br>

                    <!-- GRAPHIQUES -->
                    <div class=""groupbox-title"">{{tr:Graphiques}}</div>

                    <div style=""display:flex; gap:15px; align-items:stretch;"">

                        <!-- Donut -->
                        <div style=""flex:1; background:#2b2b2b; border:1px solid #3c3c3c; border-radius:6px; padding:10px;"">
                            <div style=""text-align:center; font-weight:bold; margin-bottom:8px; color:#fff;"">
                                {{tr:Répartition par type}}
                            </div>
                            <canvas id=""chartTypes"" height=""160""></canvas>
                        </div>

                        <div style=""width:1px; background:#3c3c3c;""></div>

                        <!-- Colonne droite : Horaire + Jauges -->
                        <div style=""flex:1; display:flex; flex-direction:column; gap:15px;"">

                            <!-- Horaire -->
                            <div style=""background:#2b2b2b; border:1px solid #3c3c3c; border-radius:6px; padding:10px;"">
                                <div style=""text-align:center; font-weight:bold; margin-bottom:8px; color:#fff;"">
                                    {{tr:Activité par heure}}
                                </div>
                                <canvas id=""chartHours"" height=""160""></canvas>
                            </div>

                            <!-- Jauges par client -->
                            <div style=""background:#2b2b2b; border:1px solid #3c3c3c; border-radius:6px; padding:10px;"">
                                <div style=""text-align:center; font-weight:bold; margin-bottom:8px; color:#fff;"">
                                    {{tr:Médias par client}}
                                </div>
                                <canvas id=""chartClients"" height=""160""></canvas>
                            </div>

                        </div>

                    </div>

                    <br>

                    <!-- STATISTIQUES AVANCÉES -->
                    <div class=""groupbox-title"">{{tr:Statistiques avancées}}</div>

                    <!-- TOP SERIES -->
                    <div class=""groupbox"">
                        <div class=""groupbox-title"">{{tr:Top séries}}</div>
                        <table>
                            <thead><tr><th>{{tr:Série}}</th><th>{{tr:Lectures}}</th></tr></thead>
                            <tbody>{{TOP_SERIES_ROWS}}</tbody>
                        </table>
                    </div>

                    <!-- TOP ARTISTES -->
                    <div class=""groupbox"">
                        <div class=""groupbox-title"">{{tr:Top artistes}}</div>
                        <table>
                            <thead><tr><th>{{tr:Artiste}}</th><th>{{tr:Lectures}}</th></tr></thead>
                            <tbody>{{TOP_ARTISTES_ROWS}}</tbody>
                        </table>
                    </div>

                    <!-- TOP CLIENTS -->
                    <div class=""groupbox"">
                        <div class=""groupbox-title"">{{tr:Top clients}}</div>
                        <table>
                            <thead><tr><th>{{tr:Client}}</th><th>{{tr:Lectures}}</th></tr></thead>
                            <tbody>{{TOP_CLIENTS_ROWS}}</tbody>
                        </table>
                    </div>

                    <!-- TOP MEDIAS PAR CLIENT -->
                    <div class=""groupbox"">
                        <div class=""groupbox-title"">{{tr:Top médias par client}}</div>
                        {{TOP_MEDIA_PER_CLIENT}}
                    </div>

                </div>
            </div>

            <!-- COLONNE DROITE -->
            <div class=""groupbox"">
                <div class=""groupbox-title"">{{tr:Listing des titres}}</div>
                <div class=""listing-header"">
                    <span>{{tr:Période}} : {{PERIOD}}</span>
                    <span>{{COUNT}} {{tr:élément(s)}}</span>
                </div>
                <table>
                    <thead>
                        <tr>
                            <th>{{tr:Type}}</th>
                            <th>{{tr:Titre}}</th>
                            <th>{{tr:Saison}}</th>
                            <th>{{tr:Épisode}}</th>
                            <th>{{tr:Canal}}</th>
                            <th>{{tr:Client}}</th>
                            <th>{{tr:Date}}</th>
                            <th>{{tr:Info}}</th>
                        </tr>
                    </thead>
                    <tbody>{{ROWS}}</tbody>
                </table>
            </div>
        </div>

        <!-- ====================================================================== -->
        <!--  BACKUP – SCRIPTS (DONUT + ACTIVITÉ PAR HEURE + COLLAPSIBLE) -->
        <!-- ====================================================================== -->

        <!-- SCRIPTS DU TABLEAU GAUCHE-->
        <script>
        const audio = {{AUDIO}};
        const series = {{SERIES}};
        const videos = {{VIDEOS}};
        const rec = {{REC}};
        const tv = {{TV}};
        const hoursData = {{HOURS_DATA}};

        // --- Couleurs globales réutilisables ---
        const colors = {
            audio: '#007acc',
            series: '#c586c0',
            video: '#d19a66',
            rec:   '#ff4d4d',
            tv:    '#ffe066'
        };

        // --- DONUT ---
        new Chart(document.getElementById('chartTypes'), {
            type: 'doughnut',
            data: {
                labels: [
                    '{{tr:Audio}}',
                    '{{tr:Séries}}',
                    '{{tr:Vidéos}}',
                    '{{tr:REC}}',
                    '{{tr:TV}}'
                ],
                datasets: [{
                    data: [audio, series, videos, rec, tv],
                    backgroundColor: [
                        colors.audio,
                        colors.series,
                        colors.video,
                        colors.rec,
                        colors.tv
                    ]
                }]
            },
            options: {
                plugins: { legend: { labels: { color:'#fff' } } }
            }
        });

        // --- ACTIVITÉ PAR HEURE ---
        new Chart(document.getElementById('chartHours'), {
            type: 'line',
            data: {
                labels: [...Array(24).keys()].map(h => (h<10?'0':'') + h + 'h'),
                datasets: [
                    {
                        label: '{{tr:Audio}}',
                        data: hoursData.map(h => h.audio ?? 0),
                        borderColor: colors.audio,
                        backgroundColor: 'rgba(0,122,204,0.25)',
                        tension: 0.3
                    },
                    {
                        label: '{{tr:Séries}}',
                        data: hoursData.map(h => h.serie ?? 0),
                        borderColor: colors.series,
                        backgroundColor: 'rgba(197,134,192,0.25)',
                        tension: 0.3
                    },
                    {
                        label: '{{tr:Vidéos}}',
                        data: hoursData.map(h => h.video ?? 0),
                        borderColor: colors.video,
                        backgroundColor: 'rgba(209,154,102,0.25)',
                        tension: 0.3
                    },
                    {
                        label: 'REC',
                        data: hoursData.map(h => h.rec ?? 0),
                        borderColor: colors.rec,
                        backgroundColor: 'rgba(255,77,77,0.25)',
                        tension: 0.3
                    },
                    {
                        label: 'TV',
                        data: hoursData.map(h => h.tv ?? 0),
                        borderColor: colors.tv,
                        backgroundColor: 'rgba(255,224,102,0.25)',
                        tension: 0.3
                    }
                ]
            },
            options: {
                scales: {
                    x: { ticks: { color:'#fff' } },
                    y: { ticks: { color:'#fff' } }
                },
                plugins: { legend: { labels: { color:'#fff' } } }
            }
        });

// --- MÉDIAS PAR CLIENT (barres fines groupées) ---
const clientData = {{CLIENT_MEDIA_DATA}};
console.log(""CLIENT DATA:"", clientData);

const clientLabels = clientData.map(c => c.client);

new Chart(document.getElementById(""chartClients""), {
    type: ""bar"",
    data: {
        labels: clientLabels,
        datasets: [
            {
                label: ""{{tr:Audio}}"",
                data: clientData.map(c => c.audio),
                backgroundColor: colors.audio,
                barThickness: 12,
                maxBarThickness: 12
            },
            {
                label: ""{{tr:Séries}}"",
                data: clientData.map(c => c.serie),
                backgroundColor: colors.series,
                barThickness: 12,
                maxBarThickness: 12
            },
            {
                label: ""{{tr:Vidéos}}"",
                data: clientData.map(c => c.video),
                backgroundColor: colors.video,
                barThickness: 12,
                maxBarThickness: 12
            },
            {
                label: ""REC"",
                data: clientData.map(c => c.rec),
                backgroundColor: colors.rec,
                barThickness: 12,
                maxBarThickness: 12
            },
            {
                label: ""TV"",
                data: clientData.map(c => c.tv),
                backgroundColor: colors.tv,
                barThickness: 12,
                maxBarThickness: 12
            }
        ]
    },
    options: {
        responsive: true,
        scales: {
            x: {
                ticks: { color: ""#fff"" },
                categoryPercentage: 0.55,
                barPercentage: 0.55
            },
            y: {
                ticks: { color: ""#fff"" }
            }
        },
        plugins: {
            legend: { labels: { color: ""#fff"" } }
        }
    }
});

        // COLLAPSIBLE
        (function () {
            const btn = document.getElementById('toggleLeft');
            const leftCol = document.getElementById('leftColumn');

            if (!btn || !leftCol) return;

            let collapsed = false;

            btn.addEventListener('click', () => {
                collapsed = !collapsed;

                if (collapsed) {
                    leftCol.classList.add('collapsed');
                    btn.textContent = '{{tr:>}}';
                } else {
                    leftCol.classList.remove('collapsed');
                    btn.textContent = '{{tr:<}}';
                }
            });
        })();
        </script>

        <!-- ====================================================================== -->
        <!--  BACKUP – OVERLAY INFO + TABLEAUX HTML STATS AVANCÉES -->
        <!-- ====================================================================== -->
        <!-- Overlay Info -->
        <div id=""infoOverlayContainer""></div>

        <script>
            function openInfo(path) {
                refreshEnabled = false;

                fetch('/info?path=' + encodeURIComponent(path))
                    .then(r => r.text())
                    .then(html => {

                        const container = document.getElementById('infoOverlayContainer');
                        container.innerHTML = html;

                        // Exécuter les scripts du popup (TMDB, AudioDB, scroll, closeOverlay)
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

        </body>
        </html>";


        // ==========================
        //  TABLEAUX HTML STATS AVANCÉES
        // ==========================
        private string BuildTopSeriesRows(List<(string Serie, int Count)> list)
        {
            var sb = new StringBuilder();
            foreach (var x in list)
                sb.Append($"<tr><td>{WebUtility.HtmlEncode(x.Serie)}</td><td>{x.Count}</td></tr>");
            return sb.ToString();
        }

        private string BuildTopArtistesRows(List<(string Artiste, int Count)> list)
        {
            var sb = new StringBuilder();
            foreach (var x in list)
                sb.Append($"<tr><td>{WebUtility.HtmlEncode(x.Artiste)}</td><td>{x.Count}</td></tr>");
            return sb.ToString();
        }

        private string BuildTopClientsRows(List<(string Client, int Count)> list)
        {
            var sb = new StringBuilder();
            foreach (var x in list)
                sb.Append($"<tr><td>{WebUtility.HtmlEncode(x.Client)}</td><td>{x.Count}</td></tr>");
            return sb.ToString();
        }
        
        private Dictionary<string, (int Audio, int Serie, int Video, int Rec, int Tv)> 
            GetMediaStatsPerClient(List<BackupItem> items)
        {
            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.ClientDisplay)
                            && !string.IsNullOrWhiteSpace(i.MediaType))
                .GroupBy(i => i.ClientDisplay!)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        int audio = g.Count(x => x.MediaType.Equals("audio", StringComparison.OrdinalIgnoreCase));
                        int serie = g.Count(x => x.MediaType.Equals("serie", StringComparison.OrdinalIgnoreCase));
                        int video = g.Count(x => x.MediaType.Equals("video", StringComparison.OrdinalIgnoreCase));
                        int rec   = g.Count(x => x.MediaType.Equals("rec",   StringComparison.OrdinalIgnoreCase));
                        int tv    = g.Count(x => x.MediaType.Equals("tv",    StringComparison.OrdinalIgnoreCase));

                        return (Audio: audio, Serie: serie, Video: video, Rec: rec, Tv: tv);
                    }
                );
        }

        // ======================================================================
        //  BACKUP – TABLEAU PAR CLIENT (HTML)
        // ======================================================================

        private string BuildMediaStatsPerClientHtml(
            Dictionary<string, (int Audio, int Serie, int Video, int Rec, int Tv)> dict)
        {
            var sb = new StringBuilder();

            foreach (var kv in dict)
            {
                string client = kv.Key;
                var stats = kv.Value;

                bool isTuner = client.StartsWith("DVB-T", StringComparison.OrdinalIgnoreCase);

                sb.Append($@"
        <div class='groupbox'>
            <div class='groupbox-title'>{WebUtility.HtmlEncode(client)}</div>

            <table>
                <thead>
                    <tr>
                        <th>{{tr:Type}}</th>
                        <th>{{tr:Lectures}}</th>
                    </tr>
                </thead>
                <tbody>
        ");

                if (isTuner)
                {
                    sb.Append($@"
                    <tr><td>REC</td><td>{stats.Rec}</td></tr>
        ");
                }
                else
                {
                    sb.Append($@"
                    <tr><td>{{tr:Séries}}</td><td>{stats.Serie}</td></tr>
                    <tr><td>{{tr:Audio}}</td><td>{stats.Audio}</td></tr>
                    <tr><td>{{tr:Vidéos}}</td><td>{stats.Video}</td></tr>
                    <tr><td>REC</td><td>{stats.Rec}</td></tr>
                    <tr><td>TV</td><td>{stats.Tv}</td></tr>
        ");
                }

                sb.Append(@"
                </tbody>
            </table>
        </div>
        ");
            }

            return HTMLTranslator.Translate(sb.ToString());
        }

        // ==========================
        //  MODELES BACKUP
        // ==========================
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

        private class BackupItem
        {
            public string? ClientDisplay { get; set; }
            public string? MediaType { get; set; }
            public string? Nom { get; set; }
            public string? FileName { get; set; }
            public string? Path { get; set; }
            public int Saison { get; set; }
            public int Episode { get; set; }
            public DateTime Timestamp { get; set; }
            public string? Channel { get; set; }
        }

        // ======================================================================
        //  DOWNLOAD PDF – GÉNÉRATION DU PDF DE BACKUP
        // ======================================================================

        // ==========================
        //  DOWNLOAD PDF
        // ==========================
        private void DownloadBackup(HttpListenerContext ctx)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";
            var files = Directory.GetFiles(folder, "history_*.json");

            if (files.Length == 0)
            {
                SendHtml(ctx, HTMLTranslator.Translate(@"
            <html>
            <head>
                <meta charset='utf-8'>
                <title>{{tr:Aucune sauvegarde}}</title>
                <link rel=""icon"" type=""image/x-icon"" href=""/favicon.ico"">
                <style>
                    body {
                        background-color: #1e1e1e;
                        color: #ffffff;
                        font-family: Segoe UI, Arial, sans-serif;
                        margin: 0;
                        padding: 40px;
                    }
                    .container {
                        max-width: 700px;
                        margin: auto;
                        background: #2b2b2b;
                        padding: 25px;
                        border-radius: 8px;
                        box-shadow: 0 0 10px #000;
                        text-align: center;
                    }
                    h2 {
                        color: #f55;
                    }
                    a.btn {
                        display: inline-block;
                        margin-top: 20px;
                        padding: 10px 18px;
                        background: #444;
                        color: white;
                        text-decoration: none;
                        border-radius: 5px;
                        font-weight: bold;
                    }
                    a.btn:hover {
                        background: #666;
                    }
                </style>
            </head>
            <body>
                <div class='container'>
                    <h2>{{tr:Aucune sauvegarde disponible}}</h2>
                    <a href='/' class='btn'>{{tr:Retour}}</a>
                </div>
            </body>
            </html>"));
                return;
            }

            string lastFile = files.OrderByDescending(f => f).First();
            string json = File.ReadAllText(lastFile);

            BackupFileModel? backup = JsonSerializer.Deserialize<BackupFileModel>(json);
            if (backup == null || backup.Reports == null)
            {
                SendHtml(ctx, "<html><body><h2>{{tr:Sauvegarde invalide}}</h2></body></html>");
                return;
            }

            var items = backup.Reports
                .Where(r => r.Items != null)
                .SelectMany(r => r.Items)
                .OrderByDescending(i => i.Timestamp)
                .ToList();

            using var doc = new PdfDocument();
            doc.Info.Title = "Backup MediaMonitor";

            var page = doc.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 10);

            double y = 20;

            string title = HTMLTranslator.Translate("{{tr:Historique sauvegardé}}");

            gfx.DrawString(title,
                new XFont("Arial", 14, XFontStyle.Bold),
                XBrushes.Black,
                new XPoint(20, y));

            y += 30;

            foreach (var item in items)
            {
                string mediaType = item.MediaType ?? "";

                // Canal fourni par le moteur
                string canal = item.Channel ?? "";

                // Titre propre fourni par le moteur
                string titreAffiche = item.Nom ?? "";

                // Saison / Épisode fournis par le moteur
                int saisonAffiche = item.Saison;
                int episodeAffiche = item.Episode;

                // Ligne PDF propre et complète
                string line =
                    $"{item.Timestamp:dd/MM/yyyy HH:mm}  |  " +
                    $"{mediaType}  |  " +
                    $"{canal}  |  " +
                    $"{titreAffiche}  " +
                    $"{(saisonAffiche > 0 ? $"S{saisonAffiche}" : "")}" +
                    $"{(episodeAffiche > 0 ? $"E{episodeAffiche}" : "")}";

                gfx.DrawString(line, font, XBrushes.Black, new XPoint(20, y));
                y += 15;

                if (y > page.Height - 40)
                {
                    page = doc.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    y = 20;
                }
            }

            using var ms = new MemoryStream();
            doc.Save(ms);
            byte[] pdfBytes = ms.ToArray();

            var dates = backup.Reports
                .Where(r => r.Items != null && r.Items.Count > 0)
                .Select(r => r.Date)
                .OrderBy(d => d)
                .ToList();

            string pdfName;

            if (dates.Count == 1)
                pdfName = $"backup_{dates[0]:yyyy-MM-dd}.pdf";
            else if (dates.Count > 1)
                pdfName = $"backup_{dates.First():yyyy-MM-dd}_to_{dates.Last():yyyy-MM-dd}.pdf";
            else
                pdfName = "backup.pdf";

            ctx.Response.ContentType = "application/pdf";
            ctx.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{pdfName}\"");
            ctx.Response.OutputStream.Write(pdfBytes, 0, pdfBytes.Length);
            ctx.Response.OutputStream.Close();
        }

        // ======================================================================
        //  BACKUP – PURGE DES SAUVEGARDES + CONDITIONS HTML
        // ======================================================================

        private void PurgeBackups(HttpListenerContext ctx)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";

            if (Directory.Exists(folder))
            {
                foreach (var f in Directory.GetFiles(folder, "history_*.json"))
                    File.Delete(f);
            }

            string html = @"
            <html>
            <head>
                <meta charset='utf-8'>
                <title>{{tr:Purge des sauvegardes}}</title>
                <link rel=""icon"" type=""image/x-icon"" href=""/favicon.ico"">
                <style>
                    body {
                        background-color: #1e1e1e;
                        color: #ffffff;
                        font-family: Arial, sans-serif;
                        margin: 0;
                        padding: 20px;
                    }
                    .container {
                        max-width: 700px;
                        margin: auto;
                        background: #2b2b2b;
                        padding: 25px;
                        border-radius: 8px;
                        box-shadow: 0 0 10px #000;
                        text-align: center;
                    }
                    h2 {
                        color: #4fc3f7;
                    }
                    a.btn {
                        display: inline-block;
                        margin-top: 20px;
                        padding: 10px 18px;
                        background: #444;
                        color: white;
                        text-decoration: none;
                        border-radius: 5px;
                        font-weight: bold;
                    }
                    a.btn:hover {
                        background: #666;
                    }
                </style>
            </head>
            <body>
                <div class='container'>
                    <h2>{{tr:Toutes les sauvegardes ont été supprimées}}</h2>
                    <a href='/backup' class='btn'>{{tr:Retour}}</a>
                </div>
            </body>
            </html>";

            // ?? La ligne magique qui manquait
            html = HTMLTranslator.Translate(html);

            SendHtml(ctx, html);
        }
        
        private static string ApplyConditional(string html, string tag, bool condition)
        {
            string start = "{{#" + tag + "}}";
            string end = "{{/" + tag + "}}";

            while (true)
            {
                int i1 = html.IndexOf(start, StringComparison.Ordinal);
                if (i1 < 0) break;

                int i2 = html.IndexOf(end, i1, StringComparison.Ordinal);
                if (i2 < 0) break;

                int blockEnd = i2 + end.Length;

                // Bloc complet incluant les balises
                string block = html.Substring(i1, blockEnd - i1);

                if (condition)
                {
                    // Contenu interne (sans les balises)
                    string inner = html.Substring(i1 + start.Length, i2 - (i1 + start.Length));
                    html = html.Replace(block, inner);
                }
                else
                {
                    // On supprime tout le bloc
                    html = html.Replace(block, "");
                }
            }

            return html;
        }
    }
}
