using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Handlers;
using MediaMonitor.Service.Web.Rendering;
using MediaMonitor.Service.Web.Templates;
using MediaMonitor.Service.Web.Models;

namespace MediaMonitor.Service.Web
{
    public class WebServer
    {
        private readonly HttpListener _listener;
        private readonly MediaMonitorEngine _engine;
        private readonly int _port;

        private long _requestCount;
        private DateTime _lastRequestTime;
        private string _lastRequestIp = "";

        private Thread? _thread;
        private bool _running;

        public WebServer(MediaMonitorEngine engine, int port)
        {
            _engine = engine;
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
        }

        public void Start()
        {
            if (_running) return;

            _running = true;
            _listener.Start();

            _thread = new Thread(ServerLoop)
            {
                IsBackground = true,
                Name = "MediaMonitor.WebServer"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;

            try
            {
                _listener.Stop();
            }
            catch { }

            try
            {
                _listener.Close();
            }
            catch { }
        }

        private void ServerLoop()
        {
            while (_running)
            {
                HttpListenerContext? ctx = null;

                try
                {
                    ctx = _listener.GetContext();
                }
                catch
                {
                    if (!_running) break;
                    continue;
                }

                Interlocked.Increment(ref _requestCount);
                _lastRequestTime = DateTime.Now;
                _lastRequestIp = ctx.Request.RemoteEndPoint?.Address.ToString() ?? "";

                ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                if (!CheckAuth(ctx))
                    return;

                string url = ctx.Request.RawUrl ?? "/";
                string path = ctx.Request.Url?.AbsolutePath ?? "/";

                switch (path.ToLowerInvariant())
                {
                    case "/":
                        {
                            string html = HomePageRenderer.Render(
                                _engine,
                                _port,
                                _requestCount,
                                _lastRequestTime,
                                _lastRequestIp);
                            SendHtml(ctx, html);
                            break;
                        }

                    case "/info":
                        InfoHandler.Handle(ctx, _engine);
                        break;

                    case "/backup":
                        BackupHandler.Handle(ctx, _engine);
                        break;

                    case "/download":
                        DownloadHandler.Handle(ctx);
                        break;

                    case "/purge":
                        PurgeHandler.Handle(ctx);
                        break;

                    case "/report":
                        ReportHandler.Handle(ctx, _engine);
                        break;

                    case "/clear":
                        ClearHandler.Handle(ctx, _engine);
                        break;

                    case "/resources":
                    case "/resources/":
                    default:
                        if (url.StartsWith("/resources/", StringComparison.OrdinalIgnoreCase))
                        {
                            ResourcesHandler.Handle(ctx);
                        }
                        else
                        {
                            SendNotFound(ctx);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                SendError(ctx, ex);
            }
        }

        private bool CheckAuth(HttpListenerContext ctx)
        {
            // Auth simple par IP locale
            var ip = ctx.Request.RemoteEndPoint?.Address;
            if (ip == null)
            {
                SendForbidden(ctx);
                return false;
            }

            if (!ip.Equals(System.Net.IPAddress.Loopback) &&
                !ip.Equals(System.Net.IPAddress.IPv6Loopback))
            {
                SendForbidden(ctx);
                return false;
            }

            return true;
        }

        // ======================================================================
        //  UTILITAIRES D’ENVOI
        // ======================================================================

        public static void SendHtml(HttpListenerContext ctx, string html)
        {
            byte[] data = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
        }

        public static void SendJson(HttpListenerContext ctx, object obj)
        {
            string json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            byte[] data = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
        }

        private void SendNotFound(HttpListenerContext ctx)
        {
            ctx.Response.StatusCode = 404;
            SendHtml(ctx, "<html><body><h2>404 - Not Found</h2></body></html>");
        }

        private void SendForbidden(HttpListenerContext ctx)
        {
            ctx.Response.StatusCode = 403;
            SendHtml(ctx, "<html><body><h2>403 - Forbidden</h2></body></html>");
        }

        private void SendError(HttpListenerContext ctx, Exception ex)
        {
            ctx.Response.StatusCode = 500;
            string html = $@"
            <html>
            <head><meta charset='utf-8'><title>Erreur</title></head>
            <body style='background:#111; color:#eee; font-family:Arial; padding:40px;'>
                <h2>Erreur serveur</h2>
                <pre>{WebUtility.HtmlEncode(ex.ToString())}</pre>
            </body>
            </html>";
            SendHtml(ctx, html);
        }

        // ======================================================================
        //  MÉTHODES UTILITAIRES (si tu veux les garder ici)
        // ======================================================================

        public (string? Serie, int Saison, int Episode) ExtractSerie(string? title)
        {
            // Ton implémentation existante, si tu veux la garder ici
            // ou la déplacer dans un helper dédié.
            return (null, 0, 0);
        }

        public (string? Artist, string? Track) ExtractAudio(string? title)
        {
            // Ton implémentation existante, si tu veux la garder ici
            // ou la déplacer dans un helper dédié.
            return (null, null);
        }

        public (string Serie, int Count)[] GetTopSeries()
        {
            var history = _engine.GetHistory();
            return history
                .Where(h => !string.IsNullOrWhiteSpace(h.SeriesName))
                .GroupBy(h => h.SeriesName!)
                .Select(g => (Serie: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .Take(20)
                .ToArray();
        }

        public (string Artiste, int Count)[] GetTopArtistes()
        {
            var history = _engine.GetHistory();
            return history
                .Where(h => !string.IsNullOrWhiteSpace(h.Artist))
                .GroupBy(h => h.Artist!)
                .Select(g => (Artiste: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .Take(20)
                .ToArray();
        }

        public (string Client, int Count)[] GetTopClientsStats()
        {
            var history = _engine.GetHistory();
            return history
                .Where(h => !string.IsNullOrWhiteSpace(h.ClientDisplay))
                .GroupBy(h => h.ClientDisplay!)
                .Select(g => (Client: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .Take(20)
                .ToArray();
        }
    }
}
