using System;
using System.IO;
using System.Net;
using MediaMonitor.Service.Web.Templates;

namespace MediaMonitor.Service.Web.Handlers
{
    public static class PurgeHandler
    {
        public static void Handle(HttpListenerContext ctx)
        {
            PurgeBackups(ctx);
        }

        private static void PurgeBackups(HttpListenerContext ctx)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";

            if (Directory.Exists(folder))
            {
                foreach (var f in Directory.GetFiles(folder, "history_*.json"))
                    File.Delete(f);
            }

            string html = @"
            <html>
            <head>
                <meta charset='utf-8'>
                <title>{{tr:Purge des sauvegardes}}</title>
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
                    <h2>{{tr:Toutes les sauvegardes ont été supprimées}}</h2>
                    <a href='/backup' class='btn'>{{tr:Retour}}</a>
                </div>
            </body>
            </html>";

            html = TemplateEngine.Translate(html);

            WebServer.SendHtml(ctx, html);
        }
    }
}
