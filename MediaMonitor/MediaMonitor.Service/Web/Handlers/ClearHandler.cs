using System;
using System.Net;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Templates;

namespace MediaMonitor.Service.Web.Handlers
{
    public static class ClearHandler
    {
        public static void Handle(HttpListenerContext ctx, MediaMonitorEngine engine)
        {
            ClearHistory(ctx, engine);
        }

        private static void ClearHistory(HttpListenerContext ctx, MediaMonitorEngine engine)
        {
            try
            {
                engine.ClearHistory();

                string html = @"
                <html>
                <head>
                    <meta charset='utf-8'>
                    <title>{{tr:Historique effacé}}</title>
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
                        <h2>{{tr:L'historique a été effacé}}</h2>
                        <a href='/' class='btn'>{{tr:Retour}}</a>
                    </div>
                </body>
                </html>";

                html = TemplateEngine.Translate(html);
                WebServer.SendHtml(ctx, html);
            }
            catch (Exception ex)
            {
                string html = $@"
                <html>
                <head>
                    <meta charset='utf-8'>
                    <title>{{tr:Erreur}}</title>
                </head>
                <body style='background:#111; color:#eee; font-family:Arial; padding:40px;'>
                    <h2>{{tr:Erreur lors de l'effacement de l'historique}}</h2>
                    <pre>{WebUtility.HtmlEncode(ex.Message)}</pre>
                    <a href='/' style='color:#4fc3f7;'>{{tr:Retour}}</a>
                </body>
                </html>";

                html = TemplateEngine.Translate(html);
                WebServer.SendHtml(ctx, html);
            }
        }
    }
}
