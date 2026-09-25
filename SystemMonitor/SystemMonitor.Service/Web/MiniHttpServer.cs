using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using MCEMonitor.Languages;

namespace SystemMonitor.Service.Web
{
    public class MiniHttpServer
    {
        private readonly SystemMonitorEngine _engine;
        private readonly SystemMonitorSettings _settings;
        private HttpListener _listener;
        private Thread _thread;
        private bool _running;
        private int _currentPort;

        public MiniHttpServer(SystemMonitorEngine engine, SystemMonitorSettings settings)
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

                CoreLog.Write($"WebServer démarré sur http://+:{_currentPort}/ (langue = {LanguageManager.CurrentLanguage})");
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

        public void Restart()
        {
            Stop();
            Thread.Sleep(500);
            Start();
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
                    ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"SystemMonitor\"");
                    ctx.Response.Close();
                    return;
                }

                string lang = ctx.Request.QueryString["lang"];
                if (!string.IsNullOrEmpty(lang))
                {
                    LanguageManager.Load(lang);
                }

                string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

                if (path == "/" || path == "/system")
                {
                    string html = WebHandler.BuildSystemPage(_engine, _settings);
                    SendHtml(ctx, html);
                }
                else if (path == "/favicon.ico")
                {
                    ServeFavicon(ctx);
                }
                else if (path.StartsWith("/resources/icons/") ||
                         path.StartsWith("/resources/images/"))
                {
                    ServeStaticFile(ctx, path);
                }                
                else
                {
                    SendHtml(ctx, "<html><body><h2>404 - Not Found</h2></body></html>", 404);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebServer ERROR : " + ex.Message);
            }
        }

        private static void ServeFavicon(HttpListenerContext ctx)
        {
            try
            {
                string icoPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "SystemMonitor.ico"
                );

                if (!File.Exists(icoPath))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                byte[] ico = File.ReadAllBytes(icoPath);

                ctx.Response.ContentType = "image/x-icon";
                ctx.Response.ContentLength64 = ico.Length;
                ctx.Response.OutputStream.Write(ico, 0, ico.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebServer favicon ERROR : " + ex.Message);

                try
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
                catch { }
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

                return decoded == $"{_settings.WebUsername}:{_settings.WebPassword}";
            }
            catch
            {
                return false;
            }
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
        
        private static void ServeStaticFile(HttpListenerContext ctx, string path)
        {
            try
            {
                // path ressemble à "/resources/icons/cpu.png"
                // On le transforme en chemin Windows : Resources\Icons\cpu.png
                string relative = path.TrimStart('/')
                                      .Replace('/', Path.DirectorySeparatorChar);

                string baseDir = AppContext.BaseDirectory;
                string fullPath = Path.Combine(baseDir, relative);

                // Sécurité : empêcher la remontée de dossier (../)
                string baseFull = Path.GetFullPath(baseDir);
                string requestedFull = Path.GetFullPath(fullPath);

                if (!requestedFull.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 403;
                    ctx.Response.Close();
                    return;
                }

                if (!File.Exists(fullPath))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                // Déterminer le Content-Type
                string ext = Path.GetExtension(fullPath).ToLowerInvariant();
                string contentType = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".svg" => "image/svg+xml",
                    ".webp" => "image/webp",
                    ".ico" => "image/x-icon",
                    _ => "application/octet-stream"
                };

                byte[] data = File.ReadAllBytes(fullPath);

                ctx.Response.ContentType = contentType;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                CoreLog.Write("ServeStaticFile ERROR : " + ex.Message);

                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch { }
            }
        }        
    }
}