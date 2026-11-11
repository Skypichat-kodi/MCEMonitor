using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetMonitor.Service.Web;
using MCEMonitor.Languages;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Web
{
    public class MiniHttpServer
    {
        private readonly NetMonitorEngine _engine;
        private readonly NetMonitorSettings _settings;
        private HttpListener? _listener;  
        private Thread?       _thread;    
        private bool _running;
        private int _currentPort;
        private readonly List<string> _localIps = new();

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
                
                try
                {
                    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            string ip = ua.Address.ToString();
                            if (!_localIps.Contains(ip)) _localIps.Add(ip);
                        }
                    }
                    _localIps.Add("127.0.0.1");
                    _localIps.Add("::1");
                }
                catch { }
                
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
                    _ = Task.Run(async () => await HandleRequestAsync(ctx));
                }
                catch
                {
                    if (_running) CoreLog.Write("WebServer : erreur dans ServerLoop");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                string path = (ctx.Request.Url?.AbsolutePath ?? "/").ToLowerInvariant();
                string method = ctx.Request.HttpMethod;
                string remoteIp = ctx.Request.RemoteEndPoint?.Address?.ToString() ?? "";
                bool isLocal = IsLocalRequest(remoteIp, _localIps);
                bool isApiRoute = path == "/ping"
                               || path == "/api/summary"
                               || path == "/api/detail"
                               || path == "/api/stats"
                               || path == "/peers";

                // ============================================================
                //  0. Routes techniques : ressources statiques (pas d'auth)
                // ============================================================
                if (path.StartsWith("/resources/") || path.StartsWith("/data/") || path == "/favicon.ico")
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
                        ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"NetMonitor\"");
                        ctx.Response.Close();
                        return;
                    }
                    // Le routage continue en bas (les routes /api/* sont gérées plus loin)
                }

                // ============================================================
                //  2. Routes publiques (login, logout) — pas de session requise
                // ============================================================
                else if (path == "/login" && method == "GET")
                {
                    string? original = ctx.Request.QueryString["original"];
                    // Premier affichage : step 1, pas de champ OTP
                    LoginPages.SendLoginPage(ctx, step: 1, error: null, original);
                    return;
                }
                else if (path == "/login" && method == "POST")
                {
                    CoreLog.Write($"[LOGIN] POST /login reçu de {remoteIp} (isLocal={isLocal}, otpEnabled={_settings.OtpEnabled})");

                    var form = ReadForm(ctx);
                    form.TryGetValue("step", out var stepStr);
                    form.TryGetValue("username", out var username);
                    form.TryGetValue("password", out var password);
                    form.TryGetValue("otp", out var otpCode);
                    form.TryGetValue("original", out var original);

                    int step = int.TryParse(stepStr, out var s) ? s : 1;
                    CoreLog.Write($"[LOGIN] step={step}, otp={(otpCode == null ? "absent" : (otpCode == "" ? "vide" : "fourni"))}");

                    // 2a. Vérifier user/password
                    if (!LoginService.CheckCredentials(username, password, _settings))
                    {
                        LoginPages.SendLoginPage(ctx, step: 1, "Identifiant ou mot de passe incorrect.", original);
                        return;
                    }

                    // 2b. Si IP locale → session directe (bypass OTP)
                    if (isLocal)
                    {
                        string session = SessionStore.Create(_settings.OtpSessionMinutes);
                        SetCookie(ctx, "netmon_session", session, _settings.OtpSessionMinutes * 60);
                        CoreLog.Write($"[LOGIN] Session créée (local) pour {remoteIp}");
                        RedirectTo(ctx, string.IsNullOrWhiteSpace(original) ? "/" : original);
                        return;
                    }

                    // 2c. OTP désactivé → session directe
                    if (!_settings.OtpEnabled)
                    {
                        string session = SessionStore.Create(_settings.OtpSessionMinutes);
                        SetCookie(ctx, "netmon_session", session, _settings.OtpSessionMinutes * 60);
                        CoreLog.Write($"[LOGIN] Session créée (OTP désactivé) pour {remoteIp}");
                        RedirectTo(ctx, string.IsNullOrWhiteSpace(original) ? "/" : original);
                        return;
                    }

                    // 2d. OTP activé, étape 1 → envoyer l'OTP, réafficher en étape 2
                    if (step == 1)
                    {
                        var (result, sent) = await LoginService.SendOtpIfNeededAsync(remoteIp, _settings);
                        if (result == LoginService.LoginResult.SmtpError)
                        {
                            LoginPages.SendLoginPage(ctx, step: 1,
                                "Impossible d'envoyer le code OTP. Vérifiez la config SMTP.", original);
                            return;
                        }

                        LoginPages.SendLoginPage(ctx, step: 2,
                            "Un code OTP vous a été envoyé par email.", original);
                        return;
                    }

                    // 2e. OTP activé, étape 2 → valider le code
                    if (step == 2)
                    {
                        if (string.IsNullOrWhiteSpace(otpCode))
                        {
                            LoginPages.SendLoginPage(ctx, step: 2, "Veuillez saisir le code OTP reçu par email.", original);
                            return;
                        }

                        var otpResult = LoginService.ValidateOtp(remoteIp, otpCode);
                        if (otpResult != LoginService.LoginResult.Ok)
                        {
                            string msg = otpResult switch
                            {
                                LoginService.LoginResult.BadOtp          => "Code OTP incorrect.",
                                LoginService.LoginResult.OtpExpired      => "Code OTP expiré. Reconnectez-vous.",
                                LoginService.LoginResult.TooManyAttempts => "Trop de tentatives. Reconnectez-vous.",
                                _                                        => "Erreur de validation OTP."
                            };
                            LoginPages.SendLoginPage(ctx, step: 2, msg, original);
                            return;
                        }

                        // OTP validé → session
                        string session = SessionStore.Create(_settings.OtpSessionMinutes);
                        SetCookie(ctx, "netmon_session", session, _settings.OtpSessionMinutes * 60);
                        CoreLog.Write($"[LOGIN] Session créée (OTP validé) pour {remoteIp}");
                        RedirectTo(ctx, string.IsNullOrWhiteSpace(original) ? "/" : original);
                        return;
                    }

                    // Cas imprévu
                    LoginPages.SendLoginPage(ctx, step: 1, "Erreur de formulaire. Réessayez.", original);
                    return;
                }
                else if (path == "/logout")
                {
                    string? session = GetCookie(ctx, "netmon_session");
                    SessionStore.Invalidate(session);
                    SetCookie(ctx, "netmon_session", "deleted", 0);
                    CoreLog.Write($"[LOGIN] Logout : session invalidée");

                    // Redirection vers /login (pas de 401, plus de popup Basic Auth)
                    RedirectTo(ctx, "/login");
                    return;
                }

                // ============================================================
                //  3. Routes protégées : session requise
                // ============================================================
                else
                {
                    string? session = GetCookie(ctx, "netmon_session");
                    if (!SessionStore.IsValid(session))
                    {
                        string original = Uri.EscapeDataString(path);
                        RedirectTo(ctx, $"/login?original={original}");
                        return;
                    }
                }

                // ============================================================
                //  4. Routage normal (les routes /api/* et pages HTML)
                // ============================================================              
                if (path == "/" || path == "/net")
                {
                    string html = WebHandler.BuildNetPage(_engine, _settings, ctx.Request.Url?.Host ?? "localhost");
                    SendHtml(ctx, html);
                }
                else if (path == "/stats")
                {
                    string html = await WebHandler.BuildStatsPageAsync(_engine, _settings, ctx.Request.Url?.Host ?? "localhost");
                    SendHtml(ctx, html);
                }
                else if (path == "/map")
                {
                    // Compatibilité : ancienne route = map complète (les 2 datasets)
                    string html = await WebHandler.BuildMapPageAsync(_engine, _settings);
                    SendHtml(ctx, html);
                }
                else if (path == "/map-active")
                {
                    string html = await WebHandler.BuildMapPageAsync(_engine, _settings, showBlockedOnly: false);
                    SendHtml(ctx, html);
                }
                else if (path == "/map-blocked")
                {
                    string html = await WebHandler.BuildMapPageAsync(_engine, _settings, showBlockedOnly: true);
                    SendHtml(ctx, html);
                }
                else if (path == "/charts")
                {
                    string html = await WebHandler.BuildChartsPageAsync(_engine, _settings);
                    SendHtml(ctx, html);
                }
                else if (path == "/api/stats")
                {
                    var stats = await _engine.GetStatsAsync();
                    string json = System.Text.Json.JsonSerializer.Serialize(stats,
                        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                    SendJson(ctx, json);
                }
                else if (path == "/clear-active")
                {
                    _engine.ClearActiveIntrusions();
                    CoreLog.Write("WebServer : intrusions actives purgées");
                    Redirect(ctx);
                }
                else if (path == "/clear-history")
                {
                    _engine.ClearAlerts();
                    CoreLog.Write("WebServer : historique purgé");
                    Redirect(ctx);
                }
                else if (path == "/clear-audit")
                {
                    _engine.ClearAudit();
                    CoreLog.Write("WebServer : historique d'audit purgé");
                    Redirect(ctx);
                }
                else if (path == "/clear-blocked-retries")
                {
                    _engine.ClearBlockedIpRetries();
                    CoreLog.Write("WebServer : compteurs de tentatives purgés");
                    Redirect(ctx);
                }                
                else if (path == "/block-ip" && ctx.Request.HttpMethod == "POST")
                {
                    string ip = ReadFormValue(ctx, "ip");
                    if (string.IsNullOrWhiteSpace(ip))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }

                    string? source   = null;
                    string? message  = null;
                    string? details  = null;
                    string? severity = null;

                    var matching = _engine.GetActiveIntrusions()
                        .Where(a => string.Equals(a.RemoteIp, ip, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(a => (int)a.Severity)
                        .ThenByDescending(a => a.Timestamp)
                        .FirstOrDefault();

                    if (matching != null)
                    {
                        source   = matching.Source;
                        message  = matching.Message;
                        details  = matching.Details;
                        severity = matching.Severity.ToString();
                    }

                    // Web = toujours une action humaine → bypass IsCriticalIp
                    bool ok = _engine.BlockIp(ip, out string error, source, message, details, severity,
                                              isAutomatic: false,
                                              forceManual: true);
                    CoreLog.Write(ok
                        ? $"WebServer : IP {ip} bloquée" + (matching != null
                            ? $" (source={source})"
                            : " (aucune alerte active — champs vides)")
                        : $"WebServer : échec blocage {ip} — {error}");

                    Redirect(ctx);
                }
                else if (path == "/unblock-ip" && ctx.Request.HttpMethod == "POST")
                {
                    string ip = ReadFormValue(ctx, "ip");
                    if (string.IsNullOrWhiteSpace(ip))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }

                    bool ok = _engine.UnblockIp(ip);
                    CoreLog.Write($"WebServer : IP {ip} débloquée (ok={ok})");
                    Redirect(ctx);
                }
                else if (path == "/set-severity" && ctx.Request.HttpMethod == "POST")
                {
                    string ip = ReadFormValue(ctx, "ip");
                    string severityStr = ReadFormValue(ctx, "severity");

                    if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(severityStr))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }

                    bool ok;
                    if (string.Equals(severityStr, "auto", StringComparison.OrdinalIgnoreCase))
                    {
                        ok = _engine.SetManualSeverity(ip, null);
                    }
                    else if (Enum.TryParse<Severity>(severityStr, ignoreCase: true, out var sev))
                    {
                        ok = _engine.SetManualSeverity(ip, sev);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }

                    CoreLog.Write(ok
                        ? $"WebServer : sévérité de {ip} → {severityStr}"
                        : $"WebServer : échec changement sévérité de {ip}");

                    Redirect(ctx);
                }
                else if (path == "/mark-safe" && ctx.Request.HttpMethod == "POST")
                {
                    string ip = ReadFormValue(ctx, "ip");
                    if (string.IsNullOrWhiteSpace(ip))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }

                    bool ok = _engine.MarkSafe(ip);
                    CoreLog.Write(ok
                        ? $"WebServer : IP {ip} marquée Safe manuellement"
                        : $"WebServer : échec mark-safe {ip}");

                    Redirect(ctx);
                }                                
                else if (path == "/block-process" && ctx.Request.HttpMethod == "POST")
                {
                    string processPath = ReadFormValue(ctx, "path");
                    if (string.IsNullOrWhiteSpace(processPath))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }
                    bool ok = _engine.BlockProcess(processPath, out string err);
                    CoreLog.Write(ok
                        ? $"WebServer : processus {processPath} bloqué"
                        : $"WebServer : échec blocage {processPath} — {err}");
                    Redirect(ctx);
                }
                else if (path == "/whitelist-process" && ctx.Request.HttpMethod == "POST")
                {
                    string processPath = ReadFormValue(ctx, "path");
                    if (string.IsNullOrWhiteSpace(processPath))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }
                    bool ok = _engine.WhitelistProcess(processPath);
                    CoreLog.Write(ok
                        ? $"WebServer : processus {processPath} whitelisté"
                        : $"WebServer : échec whitelist {processPath}");
                    Redirect(ctx);
                }
                else if (path == "/dismiss-pending" && ctx.Request.HttpMethod == "POST")
                {
                    string processPath = ReadFormValue(ctx, "path");
                    if (string.IsNullOrWhiteSpace(processPath))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }
                    bool ok = _engine.DismissPendingProcess(processPath);
                    CoreLog.Write(ok
                        ? $"WebServer : processus {processPath} retiré de la liste d'attente"
                        : $"WebServer : échec retrait {processPath}");
                    Redirect(ctx);
                }                
                else if (path == "/whitelist")
                {
                    string html = WebHandler.BuildWhitelistPage(_engine, _settings, ctx.Request.Url?.Host ?? "localhost");
                    SendHtml(ctx, html);
                }
                else if (path == "/unwhitelist-process" && ctx.Request.HttpMethod == "POST")
                {
                    string processPath = ReadFormValue(ctx, "path");
                    if (string.IsNullOrWhiteSpace(processPath))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }
                    bool ok = _engine.RemoveWhitelistEntry(processPath);
                    CoreLog.Write(ok
                        ? $"WebServer : entrée {processPath} retirée de la liste blanche"
                        : $"WebServer : échec retrait whitelist {processPath}");
                    RedirectTo(ctx, "/whitelist");
                }
                else if (path == "/toggle-whitelist-entry" && ctx.Request.HttpMethod == "POST")
                {
                    string processPath = ReadFormValue(ctx, "path");
                    string enabledStr = ReadFormValue(ctx, "enabled");
                    if (string.IsNullOrWhiteSpace(processPath))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }
                    bool enabled = enabledStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                    bool ok = _engine.SetWhitelistEntryEnabled(processPath, enabled);
                    CoreLog.Write(ok
                        ? $"WebServer : entrée {processPath} {(enabled ? "activée" : "désactivée")}"
                        : $"WebServer : échec toggle whitelist {processPath}");
                    RedirectTo(ctx, "/whitelist");
                }                
                else if (path == "/export-pdf")
                {
                    var history = _engine.GetAlerts();
                    var blocked = _engine.GetBlockedIps();
                    var audit = _engine.GetAuditHistory();

                    byte[] pdfBytes = PdfExporter.GenerateReport(Environment.MachineName, history, blocked, audit);

                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/pdf";
                    ctx.Response.AddHeader("Content-Disposition", $"attachment; filename=NetMonitor_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                    ctx.Response.ContentLength64 = pdfBytes.Length;
                    ctx.Response.OutputStream.Write(pdfBytes, 0, pdfBytes.Length);
                    ctx.Response.OutputStream.Close();
                }
                else if (path == "/ping")
                {
                    SendHtml(ctx, "pong");
                }
                else if (path == "/api/detail")
                {
                    var activeIntrusions = _engine.GetActiveIntrusions();
                    var blocked = _engine.GetBlockedIps();
                    var retries = _engine.GetBlockedIpRetries();
                    var pending = _engine.GetPendingProcesses();
                    var persistentProblems = _engine.GetPersistentProblems();

                    var detail = new NetMonitor.Service.Api.ApiDetail
                    {
                        service = "NetMonitor",
                        machine = Environment.MachineName,
                        timestamp = DateTime.Now,
                        worstSeverity = _engine.WorstSeverity,
                        activeCount = activeIntrusions.Count,
                        blockedCount = blocked.Count
                    };

                    // ---- Intrusions actives ----
                    foreach (var a in activeIntrusions)
                    {
                        detail.active.Add(new NetMonitor.Service.Api.IntrusionDetailDto
                        {
                            timestamp      = a.Timestamp,
                            source         = a.Source,
                            severity       = a.Severity.ToString().ToLowerInvariant(),
                            message        = a.Message,
                            remoteIp       = a.RemoteIp,
                            remotePort     = a.RemotePort,
                            localPort      = a.LocalPort,
                            pid            = a.Pid,
                            processName    = a.ProcessName,
                            country        = a.Country,
                            countryCode    = a.CountryCode,
                            isp            = a.Isp,
                            isKnownScanner = a.IsKnownScanner
                        });
                    }

                    // ---- IP bloquées ----
                    foreach (var b in blocked)
                    {
                        detail.blocked.Add(new NetMonitor.Service.Api.BlockedIpDetailDto
                        {
                            ip             = b.Ip,
                            blockedAt      = b.BlockedAt,
                            source         = b.Source,
                            message        = b.Message,
                            severity       = b.Severity,
                            isProcess      = b.IsProcess,
                            processPath    = b.ProcessPath,
                            country        = b.Country,
                            countryCode    = b.CountryCode,
                            city           = b.City,
                            isp            = b.Isp,
                            isPrivate      = b.IsPrivate,
                            isKnownScanner = b.IsKnownScanner
                        });
                    }

                    // ---- Retries ----
                    foreach (var r in retries)
                    {
                        detail.retries.Add(new NetMonitor.Service.Api.BlockedRetryDetailDto
                        {
                            ip               = r.Ip,
                            originalDetector = r.OriginalDetector,
                            blockedAt        = r.BlockedAt,
                            firstRetryAt     = r.FirstRetryAt,
                            lastRetryAt      = r.LastRetryAt,
                            retryCount       = r.RetryCount
                        });
                    }

                    // ---- Processus en attente ----
                    foreach (var p in pending)
                    {
                        detail.pending.Add(new NetMonitor.Service.Api.PendingProcessDetailDto
                        {
                            processPath = p.ProcessPath,
                            processName = p.ProcessName,
                            trustReason = p.TrustReason,
                            remoteIp    = p.RemoteIp,
                            remotePort  = p.RemotePort,
                            pid         = p.Pid,
                            firstSeen   = p.FirstSeen,
                            lastSeen    = p.LastSeen,
                            count       = p.Count,
                            severity    = p.Severity.ToString(),
                            detector    = p.Detector
                        });
                    }

                    // ---- Whitelist processus ----
                    var whitelist = _engine.GetWhitelistEntries();
                    foreach (var w in whitelist)
                    {
                        detail.whitelist.Add(new NetMonitor.Service.Api.WhitelistProcessDto
                        {
                            name    = w.Name ?? "",
                            path    = w.Path ?? "",
                            enabled = w.Enabled
                        });
                    }

                    // ---- Problèmes persistants ----
                    foreach (var p in persistentProblems)
                    {
                        detail.problems.Add(new NetMonitor.Service.Api.ProblemDetailDto
                        {
                            timestamp = p.Timestamp,
                            severity  = p.Severity,
                            category  = p.Category,
                            message   = p.Message
                        });
                    }

                    // ---- Stats (top IPs, pays, détecteurs) ----
                    var stats = await _engine.GetStatsAsync();
                    if (stats != null)
                    {
                        detail.stats.totalAlerts       = stats.TotalAlerts;
                        detail.stats.distinctIps       = stats.DistinctIps;
                        detail.stats.distinctCountries = stats.DistinctCountries;

                        if (stats.Active?.TopIps != null)
                        {
                            foreach (var ip in stats.Active.TopIps.Take(10))
                            {
                                detail.stats.topIps.Add(new NetMonitor.Service.Api.TopIpDto
                                {
                                    ip          = ip.Ip,
                                    count       = ip.Count,
                                    maxSeverity = ip.MaxSeverity,
                                    country     = ip.Country,
                                    countryCode = ip.CountryCode,
                                    isp         = ip.Isp
                                });
                            }
                        }

                        if (stats.Active?.TopCountries != null)
                        {
                            foreach (var c in stats.Active.TopCountries.Take(10))
                            {
                                detail.stats.topCountries.Add(new NetMonitor.Service.Api.TopCountryDto
                                {
                                    country     = c.Country,
                                    countryCode = c.CountryCode,
                                    count       = c.Count,
                                    distinctIps = c.DistinctIps
                                });
                            }
                        }

                        if (stats.Active?.TopDetectors != null)
                        {
                            foreach (var d in stats.Active.TopDetectors.Take(10))
                            {
                                detail.stats.topDetectors.Add(new NetMonitor.Service.Api.TopDetectorDto
                                {
                                    detector = d.Detector,
                                    count    = d.Count
                                });
                            }
                        }
                    }

                    // ---- LastBlock ----
                    var lb = _engine.LastBlock;
                    if (lb != null)
                    {
                        detail.lastBlock = new NetMonitor.Service.Api.LastBlockDetailDto
                        {
                            ip        = lb.Ip,
                            detector  = lb.Detector,
                            timestamp = lb.Timestamp,
                            automatic = lb.Automatic
                        };
                    }

                    string jsonDetail = System.Text.Json.JsonSerializer.Serialize(detail,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                            WriteIndented = false,
                            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                        });

                    SendJson(ctx, jsonDetail);
                }                
                else if (path == "/api/summary")
                {
                    var activeIntrusions = _engine.GetActiveIntrusions();

                    var problemsList = activeIntrusions.Select(a => new
                    {
                        severity = a.Severity.ToString().ToLower(),
                        category = a.Source,
                        message = a.Message
                    }).ToList();

                    var lastBlock = _engine.LastBlock;
                    var summary = new
                    {
                        service = "NetMonitor",
                        machine = Environment.MachineName,
                        status = _engine.WorstSeverity,
                        worstSeverity = _engine.WorstSeverity,
                        activeCount = activeIntrusions.Count,
                        blockedCount = _engine.GetBlockedIps().Count,
                        problems = problemsList,
                        lastBlock = lastBlock == null ? null : new
                        {
                            ip = lastBlock.Ip,
                            detector = lastBlock.Detector,
                            timestamp = lastBlock.Timestamp,
                            automatic = lastBlock.Automatic
                        },
                        blockedIpRetries = _engine.GetBlockedIpRetries(),
                        auditGrouped = _engine.GetAuditHistoryGrouped()
                            .OrderByDescending(a => a.Count)
                            .ThenByDescending(a => a.LastSeen)
                            .ToList()
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(summary,
                        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                    SendJson(ctx, json);
                }
                else if (path == "/favicon.ico")
                {
                    ServeFavicon(ctx);
                }
                else if (path.StartsWith("/resources/icons/") ||
                         path.StartsWith("/resources/images/") ||
                         path.StartsWith("/data/"))
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
            string? auth = ctx.Request.Headers["Authorization"];

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

        // ============================================================
        //  Helpers HTTP
        // ============================================================
        private static void Redirect(HttpListenerContext ctx)
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers["Location"] = "/";
            ctx.Response.Close();
        }

        private static void RedirectTo(HttpListenerContext ctx, string location)
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers["Location"] = location;
            ctx.Response.Close();
        }

        private static string ReadFormValue(HttpListenerContext ctx, string name)
        {
            if (!ctx.Request.HasEntityBody) return "";
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            string body = reader.ReadToEnd();

            foreach (var pair in body.Split('&'))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == name)
                    return Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            }
            return "";
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
                    ".png"     => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".ico"     => "image/x-icon",
                    ".json"    => "application/json",
                    ".geojson" => "application/json",
                    _          => "application/octet-stream"
                };

                byte[] data = File.ReadAllBytes(fullPath);
                ctx.Response.ContentType = contentType;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
                ctx.Response.OutputStream.Close();
            }
            catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
        }

        private static void ServeFavicon(HttpListenerContext ctx)
        {
            try
            {
                string icoPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "NetMonitor.ico"
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
        
        // ============================================================
        //  OTP helpers
        // ============================================================
        private static string? GetCookie(HttpListenerContext ctx, string name)
        {
            var cookie = ctx.Request.Cookies[name];
            return cookie?.Value;
        }

        private static void SetCookie(HttpListenerContext ctx, string name, string value, int maxAgeSeconds)
        {
            string cookieStr;
            if (maxAgeSeconds > 0)
            {
                cookieStr = $"{name}={value}; Path=/; HttpOnly; SameSite=Strict; Max-Age={maxAgeSeconds}";
            }
            else
            {
                // Suppression : pas de SameSite, valeur "deleted", Max-Age=0
                cookieStr = $"{name}=deleted; Path=/; HttpOnly; Max-Age=0";
            }

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
        
        private static bool IsLocalRequest(string? remoteIp, System.Collections.Generic.List<string> localIps)
        {
            if (string.IsNullOrWhiteSpace(remoteIp)) return false;

            if (remoteIp == "127.0.0.1" || remoteIp == "::1") return true;

            foreach (var ip in localIps)
                if (string.Equals(ip, remoteIp, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }                
    }
}