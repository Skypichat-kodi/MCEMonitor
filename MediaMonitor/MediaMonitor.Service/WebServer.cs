using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Threading;
using MediaMonitor.Core.Services;
using MCEMonitor.Languages;
using MediaMonitor.Service.Web;
using MediaMonitor.Service.Web.Handlers;

namespace MediaMonitor.Service
{
    public class WebServer
    {
        // --- Champs ---
        private readonly HttpListener _listener = new();
        private readonly MediaMonitorEngine _engine;
        private readonly int _port;
        private Thread? _thread;
        private bool _running = false;
        private long _requestCount = 0;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private string _lastRequestIp = "N/A";
        private WebServerSettings _settings;

        // --- Handlers ---
        private readonly HomeHandler _homeHandler;
        private readonly BackupHandler _backupHandler;
        private readonly InfoHandler _infoHandler;
        private readonly DownloadHandler _downloadHandler;
        private readonly PurgeHandler _purgeHandler;
        private readonly LogoHandler _logoHandler;
        private readonly ResourceHandler _resourceHandler;
        private readonly ApiHandler _apiHandler;

        // --- Routeur ---
        private readonly Router _router = new();

        public WebServer(int port, MediaMonitorEngine engine)
        {
            _port = port;
            _engine = engine;
            _settings = WebServerSettings.Load();

            _homeHandler     = new HomeHandler(engine, port);
            _backupHandler   = new BackupHandler(engine);
            _infoHandler     = new InfoHandler();
            _downloadHandler = new DownloadHandler();
            _purgeHandler    = new PurgeHandler();
            _logoHandler     = new LogoHandler();
            _resourceHandler = new ResourceHandler();
            _apiHandler      = new ApiHandler();

            RegisterRoutes();

            _listener.Prefixes.Add($"http://+:{port}/");
        }

        private void RegisterRoutes()
        {
            // Pages HTML            
            _router.Map("/",         ctx => HttpWriter.WriteHtml(ctx.Http, _homeHandler.Render(_requestCount, _lastRequestTime, _lastRequestIp, ctx.Http.Request.Url?.Host ?? "localhost")));
            _router.Map("/back",     ctx => HttpWriter.WriteHtml(ctx.Http, _homeHandler.Render(_requestCount, _lastRequestTime, _lastRequestIp, ctx.Http.Request.Url?.Host ?? "localhost")));
            _router.Map("/backup",   ctx => HttpWriter.WriteHtml(ctx.Http, _backupHandler.Render(BackupRequest.FromHttp(ctx.Http.Request))));
            _router.Map("/info",     ctx => _infoHandler.Show(ctx));

            // Actions
            _router.Map("/download", ctx => _downloadHandler.Download(ctx));
            _router.Map("/purge",    ctx => _purgeHandler.Purge(ctx));
            _router.Map("/logo",     ctx => _logoHandler.ServeAsync(ctx).GetAwaiter().GetResult());
            _router.Map("/ping",     ctx => HttpWriter.WriteHtml(ctx.Http, "pong"));
            _router.Map("/peers",    ctx => _apiHandler.Peers(ctx));

            // API JSON
            _router.Map("/history",     ctx => _apiHandler.History(ctx));
            _router.Map("/live",        ctx => _apiHandler.Live(ctx));
            _router.Map("/status",      ctx => _apiHandler.Status(ctx));
            _router.Map("/lastimage",   ctx => _apiHandler.LastImage(ctx));
            _router.Map("/report",      ctx => _apiHandler.Report(ctx));
            _router.Map("/clear",       ctx => _apiHandler.Clear(ctx));
            _router.Map("/api/summary", ctx =>  ApiSummary(ctx));
            _router.Map("/api/detail",  ctx => ApiDetailHandler(ctx));

            // Ressources (routes par préfixe)
            _router.Map("/favicon.ico",               ctx => _resourceHandler.Favicon(ctx));
            _router.MapPrefix("/resources/icons/",    ctx => _resourceHandler.Icon(ctx));
            _router.MapPrefix("/resources/images/",   ctx => _resourceHandler.Image(ctx));
        }

        public void ReloadSettings()
        {
            _settings = WebServerSettings.Load();
            CoreLog.Write("WebServer : paramètres rechargés.");
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _listener.Start();
            _thread = new Thread(ServerLoop) { IsBackground = true };
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

                if (!CheckAuth(ctx))
                {
                    HttpWriter.Write401(ctx);
                    return;
                }

                // Langue
                string? lang = ctx.Request.QueryString["lang"];   
                if (!string.IsNullOrEmpty(lang))
                    LanguageManager.Load(lang);

                string path = (ctx.Request.Url?.AbsolutePath ?? "/").ToLowerInvariant();   

                var hctx = new HandlerContext(ctx, _engine, _settings, _port);

                if (!_router.TryInvoke(hctx, path))
                {
                    HttpWriter.WriteHtml(ctx,
                        "<html><body><h2>404 - Not Found</h2></body></html>", 404);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebServer ERROR: " + ex.Message);
            }
        }

        private bool CheckAuth(HttpListenerContext ctx)
        {
            string? auth = ctx.Request.Headers["Authorization"];   
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Basic "))
                return false;

            try
            {
                string encoded = auth.Substring("Basic ".Length).Trim();
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                return decoded == $"{_settings.Username}:{_settings.Password}";
            }
            catch { return false; }
        }
        
        private void ApiSummary(HandlerContext ctx)
        {
            var summary = new MediaMonitor.Service.Api.ApiSummary
            {
                service = "MediaMonitor",
                machine = Environment.MachineName,
                status = "ok",
                worstSeverity = "ok"
            };

            // Récupérer les médias en cours
            var live = _engine.GetCurrentOpenFiles();

            foreach (var item in live)
            {
                summary.media.Add(new MediaMonitor.Service.Api.ApiMedia
                {
                    client  = item.ClientDisplay ?? "",
                    type    = item.MediaType ?? "",
                    title   = item.Nom ?? "",
                    saison  = item.Saison,
                    episode = item.Episode
                });
            }

            HttpWriter.WriteJson(ctx.Http, summary);
        }
        
        private void ApiDetailHandler(HandlerContext ctx)
        {
            var live    = _engine.GetCurrentOpenFiles();
            var history = _engine.GetHistory();
            var lastImg = _engine.GetLastImage();

            var detail = new MediaMonitor.Service.Api.ApiDetail
            {
                service = "MediaMonitor",
                machine = Environment.MachineName,
                timestamp = DateTime.Now,
                openCount = live.Count,
                historyCount = history.Count,
                lastImage = string.IsNullOrWhiteSpace(lastImg) ? null : lastImg
            };

            // ---- Live ----
            foreach (var item in live)
            {
                detail.live.Add(ConvertToDto(item));
            }

            // ---- History (limité aux 50 plus récents) ----
            var recentHistory = history
                .OrderByDescending(x => x.Timestamp)
                .Take(50)
                .ToList();

            foreach (var item in recentHistory)
            {
                detail.history.Add(ConvertToDto(item));
            }

            // ---- Stats (calculées sur l'historique complet, pas seulement les 50) ----
            detail.stats.total = history.Count;

            foreach (var item in history)
            {
                string key = (item.MediaType ?? "").Trim();
                if (string.IsNullOrEmpty(key)) key = "Inconnu";

                if (!detail.stats.byType.ContainsKey(key))
                    detail.stats.byType[key] = 0;
                detail.stats.byType[key]++;
            }

            // Top séries (uniquement les items avec MediaType == "Serie")
            detail.stats.topSeries = history
                .Where(x => string.Equals(x.MediaType, "Serie", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => string.IsNullOrWhiteSpace(x.SeriesName) ? x.Nom : x.SeriesName)
                .Select(g => new MediaMonitor.Service.Api.MediaStatRowDto
                {
                    label = g.Key ?? "Inconnu",
                    count = g.Count()
                })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToList();

            // Top artistes (uniquement les items avec MediaType == "Audio")
            detail.stats.topArtists = history
                .Where(x => string.Equals(x.MediaType, "Audio", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Artist) ? "Artiste inconnu" : x.Artist)
                .Select(g => new MediaMonitor.Service.Api.MediaStatRowDto
                {
                    label = g.Key,
                    count = g.Count()
                })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToList();

            // Top clients
            detail.stats.topClients = history
                .Where(x => !string.IsNullOrWhiteSpace(x.ClientDisplay))
                .GroupBy(x => x.ClientDisplay)
                .Select(g => new MediaMonitor.Service.Api.MediaStatRowDto
                {
                    label = g.Key,
                    count = g.Count()
                })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToList();

            HttpWriter.WriteJson(ctx.Http, detail);
        }

        private static MediaMonitor.Service.Api.MediaItemDto ConvertToDto(MediaUsageItem item)
        {
            return new MediaMonitor.Service.Api.MediaItemDto
            {
                client       = item.ClientDisplay ?? "",
                mediaType    = item.MediaType ?? "",
                nom          = item.Nom ?? "",
                fileName     = item.FileName ?? "",
                channel      = item.Channel ?? "",
                seriesName   = item.SeriesName ?? "",
                episodeName  = item.EpisodeName ?? "",
                saison       = item.Saison,
                episode      = item.Episode,
                timestamp    = item.Timestamp,
                title        = item.Title ?? "",
                artist       = item.Artist ?? "",
                album        = item.Album ?? ""
            };
        }        
    }
}