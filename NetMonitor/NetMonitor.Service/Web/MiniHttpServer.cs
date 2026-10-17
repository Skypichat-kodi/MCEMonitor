using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using MCEMonitor.Languages;

namespace NetMonitor.Service.Web
{
    public class MiniHttpServer
    {
        private readonly NetMonitorEngine _engine;
        private readonly NetMonitorSettings _settings;
        private HttpListener _listener;
        private Thread _thread;
        private bool _running;
        private int _currentPort;

        public MiniHttpServer(NetMonitorEngine engine, NetMonitorSettings settings)
        {
            _engine = engine;
            _settings = settings;
        }

        public void Start()
        {
            if (_running) return;

            if (!_settings.WebEnabled)
            {
                CoreLog.Write("WebServer : désactivé (WebEnabled=false)");
                return;
            }

            try
            {
                _currentPort = _settings.WebPort;

                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://+:{_currentPort}/");
                _listener.Start();

                _running = true;

                _thread = new Thread(ServerLoop) { IsBackground = true };
                _thread.Start();

                CoreLog.Write($"WebServer démarré sur http://+:{_currentPort}/");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR WebServer Start : " + ex.Message);
            }
        }

        public void Stop()
        {
            try
            {
                _running = false;
                _listener?.Stop();
                _listener?.Close();
                _listener = null;
                CoreLog.Write("WebServer arrêté");
            }
            catch { }
        }

        private void ServerLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener!.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch
                {
                    if (_running)
                        CoreLog.Write("WebServer : erreur dans ServerLoop");
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                if (!CheckAuth(ctx))
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"NetMonitor\"");
                    ctx.Response.Close();
                    return;
                }

                string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

                if (path == "/" || path == "/net")
                {
                    string html = WebHandler.BuildNetPage(_engine, _settings, ctx.Request.Url?.Host ?? "localhost");
                    SendHtml(ctx, html);
                }
                else if (path == "/clear-active")
                {
                    _engine.ClearActiveIntrusions();
                    CoreLog.Write("WebServer : intrusions actives purgées");
                    ctx.Response.StatusCode = 302;
                    ctx.Response.Headers["Location"] = "/";
                    ctx.Response.Close();
                }
                else if (path == "/clear-history")
                {
                    _engine.ClearAlerts();
                    CoreLog.Write("WebServer : historique purgé");
                    ctx.Response.StatusCode = 302;
                    ctx.Response.Headers["Location"] = "/";
                    ctx.Response.Close();
                }
                else if (path == "/ping")
                {
                    SendHtml(ctx, "pong");
                }
                else if (path == "/api/summary")
                {
                    // Prêt pour MCEMonitorClient / Android
                    var summary = new
                    {
                        service = "NetMonitor",
                        machine = Environment.MachineName,
                        status = _engine.WorstSeverity,
                        worstSeverity = _engine.WorstSeverity,
                        activeCount = _engine.GetActiveIntrusions().Count,
                        blockedCount = _engine.GetBlockedIps().Count
                    };
                    string json = System.Text.Json.JsonSerializer.Serialize(summary,
                        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                    SendJson(ctx, json);
                }
                else if (path.StartsWith("/resources/icons/") || path.StartsWith("/resources/images/"))
                {
                    ServeStaticFile(ctx, path);
                }
                else
                {
                    SendHtml(ctx, "<html><body><h2>404 - Not Found</h2></body></html>", 404);
                }
            }
            catch (Exception ex) { CoreLog.Write("WebServer ERROR : " + ex.Message); }
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
                return decoded == $"{_settings.WebUsername}:{_settings.WebPassword}";
            }
            catch { return false; }
        }

        private static void SendHtml(HttpListenerContext ctx, string html, int status = 200)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
        }

        private static void SendJson(HttpListenerContext ctx, string json)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
        }
        
        private static void ServeStaticFile(HttpListenerContext ctx, string path)
        {
            try
            {
                string relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string baseDir = AppContext.BaseDirectory;
                string fullPath = Path.Combine(baseDir, relative);

                if (!File.Exists(fullPath))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                string ext = Path.GetExtension(fullPath).ToLowerInvariant();
                string contentType = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".ico" => "image/x-icon",
                    _ => "application/octet-stream"
                };

                byte[] data = File.ReadAllBytes(fullPath);
                ctx.Response.ContentType = contentType;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
                ctx.Response.OutputStream.Close();
            }
            catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
        }        
    }
}