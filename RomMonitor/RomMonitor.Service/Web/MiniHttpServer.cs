using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using MCEMonitor.Languages;

namespace RomMonitor.Service.Web
{
    /// <summary>
    /// Serveur HTTP minimaliste pour RomMonitor.
    /// </summary>
    public class MiniHttpServer
    {
        private readonly RomMonitorEngine _engine;
        private readonly RomMonitorSettings _settings;
        private HttpListener _listener;
        private Thread _thread;
        private bool _running;
        private int _currentPort;

        public MiniHttpServer(RomMonitorEngine engine, RomMonitorSettings settings)
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
                CoreLog.Write("WebServer arreté");
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
                // Auth Basic
                if (!CheckAuth(ctx))
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"RomMonitor\"");
                    ctx.Response.Close();
                    return;
                }

                // Langue demandée par l'URL (?lang=fr-FR)
                string lang = ctx.Request.QueryString["lang"];
                if (!string.IsNullOrEmpty(lang))
                {
                    LanguageManager.Load(lang);
                }

                string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

                if (path == "/" || path == "/rom")
                {
                    string publicHost = ctx.Request.Url?.Host ?? "localhost";
                    string html = WebHandler.BuildRomPage(_engine, _settings, publicHost);
                    SendHtml(ctx, html);
                }
                
                else if (path == "/ping")
                {
                    SendHtml(ctx, "pong");
                }
                else if (path == "/api/summary")
                {
                    var summary = new RomMonitor.Service.Api.ApiSummary
                    {
                        service = "RomMonitor",
                        machine = Environment.MachineName,
                        status = _engine.WorstSeverity,
                        worstSeverity = _engine.WorstSeverity
                    };

                    // SMART Critical / Warning
                    foreach (var s in _engine.LastSmart)
                    {
                        if (!s.Available) continue;
                        if (s.Status != "Critical" && s.Status != "Warning") continue;

                        summary.problems.Add(new RomMonitor.Service.Api.ApiProblem
                        {
                            severity = s.Status == "Critical" ? "critical" : "warning",
                            category = "Smart",
                            message = $"{s.Model} : {s.StatusReason}"
                        });
                    }

                    // Espace disque
                    var settings = RomMonitorSettings.Load();

                    foreach (var d in _engine.LastDisks)
                    {
                        bool critical = d.FreePercent < settings.DiskSpaceCriticalPercent
                                     || d.FreeGo < settings.DiskSpaceCriticalGo;

                        bool warn = d.FreePercent < settings.DiskSpaceWarnPercent
                                 || d.FreeGo < settings.DiskSpaceWarnGo;

                        if (critical)
                        {
                            summary.problems.Add(new RomMonitor.Service.Api.ApiProblem
                            {
                                severity = "critical",
                                category = "DiskSpace",
                                message = $"Disque {d.Name} : {d.FreePercent:F1}% libre ({d.FreeGo:F1} Go)"
                            });
                        }
                        else if (warn)
                        {
                            summary.problems.Add(new RomMonitor.Service.Api.ApiProblem
                            {
                                severity = "warning",
                                category = "DiskSpace",
                                message = $"Disque {d.Name} : espace faible ({d.FreePercent:F1}%)"
                            });
                        }
                    }

                    string json = System.Text.Json.JsonSerializer.Serialize(summary,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                            WriteIndented = true
                        });

                    SendJson(ctx, json);
                }                
                else if (path == "/clear-history")
                {
                    try
                    {
                        _engine.ClearAlerts();
                        CoreLog.Write("WebServer : historique des alertes vidé via web");
                    }
                    catch (Exception ex)
                    {
                        CoreLog.Write("WebServer clear-history ERROR : " + ex.Message);
                    }

                    // Redirection vers la page d'accueil
                    ctx.Response.StatusCode = 302;
                    ctx.Response.Headers["Location"] = "/";
                    ctx.Response.Close();
                }                
                else if (path == "/peers")
                {
                    var peers = PeerStatusService.CheckAllAsync("RomMonitor")
                                                   .GetAwaiter().GetResult();

                    string json = System.Text.Json.JsonSerializer.Serialize(peers,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                        });
                    SendJson(ctx, json);
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
                    "RomMonitor.ico"
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
                // "/resources/images/web-background.png" ? "Resources\Images\web-background.png"
                string relative = path.TrimStart('/')
                                      .Replace('/', Path.DirectorySeparatorChar);

                string baseDir = AppContext.BaseDirectory;
                string fullPath = Path.Combine(baseDir, relative);

                // Sécurité : empecher la remontée de dossier
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