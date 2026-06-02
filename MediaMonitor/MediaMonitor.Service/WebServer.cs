using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
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

        public WebServer(int port, MediaMonitorEngine engine)
        {
            _port = port;
            _engine = engine;

            _listener.Prefixes.Add($"http://+:{port}/");
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
    }
}


