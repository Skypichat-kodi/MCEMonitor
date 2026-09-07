using System.Net;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Templates;

namespace MediaMonitor.Service.Web.Handlers
{
    public static class ReportHandler
    {
        public static void Handle(HttpListenerContext ctx, MediaMonitorEngine engine)
        {
            // Fire & forget
            _ = engine.SendReportEmail();

            string html = @"
                <html>
                <head>
                    <meta charset='utf-8'>
                    <title>{{tr:Rapport envoyé}}</title>
                    <link rel='icon' type='image/x-icon' href='/favicon.ico'>
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
                        <h2>{{tr:Le rapport a été envoyé (email)}} </h2>
                        <a href='/' class='btn'>{{tr:Retour}}</a>
                    </div>
                </body>
                </html>";

            html = TemplateEngine.Translate(html);
            WebServer.SendHtml(ctx, html);
        }
    }
}
