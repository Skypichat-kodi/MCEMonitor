using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCEMonitor.Languages;

namespace SystemMonitor.Service.Web
{
    public class MiniHttpServer
    {
        private readonly SystemMonitorEngine    _engine;
        private readonly SystemMonitorSettings  _settings;
        private HttpListener?                   _listener;  
        private Thread?                         _thread;    
        private bool                            _running;
        private int                             _currentPort;

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
                string path = (ctx.Request.Url?.AbsolutePath ?? "/").ToLowerInvariant();
                string method = ctx.Request.HttpMethod;
                string remoteIp = ctx.Request.RemoteEndPoint?.Address?.ToString() ?? "";
                bool isLocal = IsLocalRequest(remoteIp);
                bool isApiRoute = path == "/ping"
                               || path == "/api/summary"
                               || path == "/api/detail"
                               || path == "/peers";

                // ============================================================
                //  0. Ressources statiques (pas d'auth)
                // ============================================================
                if (path.StartsWith("/resources/") || path == "/favicon.ico")
                {
                    if (path == "/favicon.ico") ServeFavicon(ctx);
                    else ServeStaticFile(ctx, path);
                    return;
                }

                // ============================================================
                //  1. Routes API : Basic Auth seul (clients programmatiques)
                // ============================================================
                if (isApiRoute)
                {
                    if (!CheckAuth(ctx))
                    {
                        ctx.Response.StatusCode = 401;
                        ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"SystemMonitor\"");
                        ctx.Response.Close();
                        return;
                    }
                    // continue vers le routage
                }

                // ============================================================
                //  2. Routes publiques (login, logout) — pas de session requise
                // ============================================================
                else if (path == "/login" && method == "GET")
                {
                    string? original = ctx.Request.QueryString["original"];
                    LoginPages.SendLoginPage(ctx, error: null, original);
                    return;
                }
                else if (path == "/login" && method == "POST")
                {
                    var form = ReadForm(ctx);
                    form.TryGetValue("username", out var username);
                    form.TryGetValue("password", out var password);
                    form.TryGetValue("original", out var original);

                    if (!LoginService.CheckCredentials(username, password, _settings))
                    {
                        LoginPages.SendLoginPage(ctx, "Identifiant ou mot de passe incorrect.", original);
                        return;
                    }

                    string session = SessionStore.Create(15);
                    SetCookie(ctx, "sysmon_session", session, 15 * 60);
                    CoreLog.Write($"[LOGIN] Session créée pour {remoteIp}");
                    RedirectTo(ctx, string.IsNullOrWhiteSpace(original) ? "/" : original);
                    return;
                }
                else if (path == "/logout")
                {
                    string? session = GetCookie(ctx, "sysmon_session");
                    SessionStore.Invalidate(session);
                    SetCookie(ctx, "sysmon_session", "deleted", 0);
                    CoreLog.Write($"[LOGIN] Logout : session invalidée");
                    RedirectTo(ctx, "/login");
                    return;
                }

                // ============================================================
                //  3. Routes protégées : session requise
                // ============================================================
                else
                {
                    string? session = GetCookie(ctx, "sysmon_session");
                    if (!SessionStore.IsValid(session))
                    {
                        string original = Uri.EscapeDataString(path);
                        RedirectTo(ctx, $"/login?original={original}");
                        return;
                    }
                }

                // ============================================================
                //  4. Routage normal
                // ============================================================
                string? lang = ctx.Request.QueryString["lang"];
                if (!string.IsNullOrEmpty(lang))
                {
                    LanguageManager.Load(lang);
                }

                if (path == "/" || path == "/system")
                {
                    string publicHost = ctx.Request.Url?.Host ?? "localhost";
                    string html = WebHandler.BuildSystemPage(_engine, _settings, publicHost);
                    SendHtml(ctx, html);
                }
                else if (path == "/ping")
                {
                    SendHtml(ctx, "pong");
                }
                else if (path == "/api/summary")
                {
                    var summary = new SystemMonitor.Service.Api.ApiSummary
                    {
                        service = "SystemMonitor",
                        machine = Environment.MachineName,
                        status = _engine.WorstSeverity,
                        worstSeverity = _engine.WorstSeverity
                    };

                    foreach (var p in _engine.GetProblems(20))
                    {
                        summary.problems.Add(new SystemMonitor.Service.Api.ApiProblem
                        {
                            severity = p.Severity,
                            category = p.Category,
                            target   = p.Target,
                            message = p.Message
                        });
                    }

                    string json = System.Text.Json.JsonSerializer.Serialize(summary,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                            WriteIndented = true
                        });

                    SendJson(ctx, json);
                }                
                else if (path == "/api/detail")
                {
                    var snap = _engine.LastSnapshot;

                    var detail = new SystemMonitor.Service.Api.ApiDetail
                    {
                        service = "SystemMonitor",
                        machine = Environment.MachineName,
                        timestamp = DateTime.Now,
                        worstSeverity = _engine.WorstSeverity,
                        system = new SystemMonitor.Service.Api.SystemInfoDto
                        {
                            lastUpdate = _engine.LastUpdateTime,
                            cpu = new SystemMonitor.Service.Api.CpuDto
                            {
                                name = snap.Cpu.Name,
                                usagePercent = snap.Cpu.UsagePercent,
                                temperature = snap.Cpu.Temperature,
                                frequencyMHz = snap.Cpu.FrequencyMHz,
                                maxFrequencyMHz = snap.Cpu.MaxFrequencyMHz,
                                cores = System.Linq.Enumerable.ToList(
                                    System.Linq.Enumerable.Select(snap.Cpu.Cores, c =>
                                        new SystemMonitor.Service.Api.CpuCoreDto
                                        {
                                            name = c.Name,
                                            usagePercent = c.UsagePercent
                                        }))
                            },
                            ram = new SystemMonitor.Service.Api.RamDto
                            {
                                totalGB = snap.Ram.TotalGB,
                                usedGB = snap.Ram.UsedGB,
                                freeGB = snap.Ram.FreeGB,
                                usagePercent = snap.Ram.UsagePercent
                            },
                            gpus = System.Linq.Enumerable.ToList(
                                System.Linq.Enumerable.Select(snap.Gpus, g =>
                                    new SystemMonitor.Service.Api.GpuDto
                                    {
                                        name = g.Name,
                                        usagePercent = g.UsagePercent,
                                        temperature = g.Temperature,
                                        vramUsedMB = g.VramUsedMB,
                                        vramTotalMB = g.VramTotalMB
                                    })),
                            networks = System.Linq.Enumerable.ToList(
                                System.Linq.Enumerable.Select(snap.Networks, n =>
                                    new SystemMonitor.Service.Api.NetworkDto
                                    {
                                        name = n.Name,
                                        downloadKBps = n.DownloadKBps,
                                        uploadKBps = n.UploadKBps
                                    })),
                            history = System.Linq.Enumerable.ToList(
                                System.Linq.Enumerable.Select(_engine.GetHistory(), h =>
                                    new SystemMonitor.Service.Api.HistoryPointDto
                                    {
                                        timestamp = h.Timestamp,
                                        cpuUsage = h.CpuUsage,
                                        ramUsage = h.RamUsage,
                                        cpuTemp = h.CpuTemp,
                                        gpuUsage = h.GpuUsage,
                                        gpuTemp = h.GpuTemp
                                    }))
                        },
                        bsods = System.Linq.Enumerable.ToList(
                            System.Linq.Enumerable.Select(_engine.GetBsods(), b =>
                                new SystemMonitor.Service.Api.BsodDto
                                {
                                    timestamp = b.Timestamp,
                                    bugCheckCode = b.BugCheckCode,
                                    bugCheckName = b.BugCheckName,
                                    faultyModule = b.FaultyModule,
                                    dumpExists = b.DumpExists
                                })),
                        alerts = System.Linq.Enumerable.ToList(
                            System.Linq.Enumerable.Select(_engine.GetAlerts(), a =>
                                new SystemMonitor.Service.Api.AlertDto
                                {
                                    timestamp = a.Timestamp,
                                    type = a.Type.ToString(),
                                    severity = a.Severity,
                                    target = a.Target,
                                    message = a.Message,
                                    emailSent = a.EmailSent
                                }))
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(detail,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                            WriteIndented = false,
                            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                        });

                    SendJson(ctx, json);
                }                
                else if (path == "/peers")
                {
                    var peers = PeerStatusService.CheckAllAsync("SystemMonitor")
                                                   .GetAwaiter().GetResult();

                    string json = System.Text.Json.JsonSerializer.Serialize(peers,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                        });
                    SendJson(ctx, json);
                }
                else if (path == "/clear-history")
                {
                    _engine.ClearHistory();
                    _engine.ClearAlerts();

                    CoreLog.Write("WebServer : historique vidé via /clear-history");

                    ctx.Response.StatusCode = 302;
                    ctx.Response.Headers["Location"] = "/";
                    ctx.Response.Close();
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
            string? auth = ctx.Request.Headers["Authorization"]; 

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
        
        // ============================================================
        //  Helpers login
        // ============================================================
        private bool IsLocalRequest(string remoteIp)
        {
            if (string.IsNullOrWhiteSpace(remoteIp)) return false;
            if (remoteIp == "127.0.0.1" || remoteIp == "::1") return true;

            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (string.Equals(ua.Address.ToString(), remoteIp, StringComparison.OrdinalIgnoreCase))
                            return true;
                }
            }
            catch { }

            return false;
        }

        private static string? GetCookie(HttpListenerContext ctx, string name)
        {
            var cookie = ctx.Request.Cookies[name];
            return cookie?.Value;
        }

        private static void SetCookie(HttpListenerContext ctx, string name, string value, int maxAgeSeconds)
        {
            string cookieStr;
            if (maxAgeSeconds > 0)
                cookieStr = $"{name}={value}; Path=/; HttpOnly; SameSite=Strict; Max-Age={maxAgeSeconds}";
            else
                cookieStr = $"{name}=deleted; Path=/; HttpOnly; Max-Age=0";

            ctx.Response.Headers.Add("Set-Cookie", cookieStr);
        }

        private static Dictionary<string, string> ReadForm(HttpListenerContext ctx)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!ctx.Request.HasEntityBody) return dict;
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            string body = reader.ReadToEnd();
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2)
                    dict[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            }
            return dict;
        }
        
        private static void RedirectTo(HttpListenerContext ctx, string location)
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers["Location"] = location;
            ctx.Response.Close();
        }                        
    }
}