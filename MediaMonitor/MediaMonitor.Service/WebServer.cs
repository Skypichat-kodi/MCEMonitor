using System;
using System.IO;
using System.Net;
using System.Text;
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
        private Thread _thread;
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
            _router.Map("/",         ctx => HttpWriter.WriteHtml(ctx.Http, _homeHandler.Render(_requestCount, _lastRequestTime, _lastRequestIp)));
            _router.Map("/back",     ctx => HttpWriter.WriteHtml(ctx.Http, _homeHandler.Render(_requestCount, _lastRequestTime, _lastRequestIp)));
            _router.Map("/backup",   ctx => HttpWriter.WriteHtml(ctx.Http, _backupHandler.Render(BackupRequest.FromHttp(ctx.Http.Request))));
            _router.Map("/info",     ctx => _infoHandler.Show(ctx));

            // Actions
            _router.Map("/download", ctx => _downloadHandler.Download(ctx));
            _router.Map("/purge",    ctx => _purgeHandler.Purge(ctx));
            _router.Map("/logo",     ctx => _logoHandler.ServeAsync(ctx).GetAwaiter().GetResult());

            // API JSON
            _router.Map("/history",   ctx => _apiHandler.History(ctx));
            _router.Map("/live",      ctx => _apiHandler.Live(ctx));
            _router.Map("/status",    ctx => _apiHandler.Status(ctx));
            _router.Map("/lastimage", ctx => _apiHandler.LastImage(ctx));
            _router.Map("/report",    ctx => _apiHandler.Report(ctx));
            _router.Map("/clear",     ctx => _apiHandler.Clear(ctx));

            // Ressources (routes par préfixe)
            _router.Map("/favicon.ico",         ctx => _resourceHandler.Favicon(ctx));
            _router.MapPrefix("/resources/icons/",  ctx => _resourceHandler.Icon(ctx));
            _router.MapPrefix("/resources/images/", ctx => _resourceHandler.Image(ctx));
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
                string lang = ctx.Request.QueryString["lang"];
                if (!string.IsNullOrEmpty(lang))
                    LanguageManager.Load(lang);

                string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

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
            string auth = ctx.Request.Headers["Authorization"];
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
    }
}