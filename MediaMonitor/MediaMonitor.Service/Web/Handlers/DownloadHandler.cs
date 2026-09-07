using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using MediaMonitor.Service.Web.Models;
using MediaMonitor.Service.Web.Templates;

namespace MediaMonitor.Service.Web.Handlers
{
    public static class DownloadHandler
    {
        public static void Handle(HttpListenerContext ctx)
        {
            DownloadBackup(ctx);
        }

        private static void DownloadBackup(HttpListenerContext ctx)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";
            var files = Directory.GetFiles(folder, "history_*.json");

            if (files.Length == 0)
            {
                string html = @"
                <html>
                <head>
                    <meta charset='utf-8'>
                    <title>{{tr:Aucune sauvegarde}}</title>
                    <link rel=""icon"" type=""image/x-icon"" href=""/favicon.ico"">
                    <style>
                        body {
                            background-color: #1e1e1e;
                            color: #ffffff;
                            font-family: Segoe UI, Arial, sans-serif;
                            margin: 0;
                            padding: 40px;
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
                            color: #f55;
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
                        <h2>{{tr:Aucune sauvegarde disponible}}</h2>
                        <a href='/' class='btn'>{{tr:Retour}}</a>
                    </div>
                </body>
                </html>";

                html = TemplateEngine.Translate(html);
                WebServer.SendHtml(ctx, html);
                return;
            }

            string lastFile = files.OrderByDescending(f => f).First();
            string json = File.ReadAllText(lastFile);

            BackupFileModel? backup = JsonSerializer.Deserialize<BackupFileModel>(json);
            if (backup == null || backup.Reports == null)
            {
                WebServer.SendHtml(ctx, "<html><body><h2>{{tr:Sauvegarde invalide}}</h2></body></html>");
                return;
            }

            var items = backup.Reports
                .Where(r => r.Items != null)
                .SelectMany(r => r.Items)
                .OrderByDescending(i => i.Timestamp)
                .ToList();

            using var doc = new PdfDocument();
            doc.Info.Title = "Backup MediaMonitor";

            var page = doc.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 10);

            double y = 20;

            string title = TemplateEngine.Translate("{{tr:Historique sauvegardé}}");

            gfx.DrawString(title,
                new XFont("Arial", 14, XFontStyle.Bold),
                XBrushes.Black,
                new XPoint(20, y));

            y += 30;

            foreach (var item in items)
            {
                string mediaType = item.MediaType ?? "";
                string canal = item.Channel ?? "";
                string titreAffiche = item.Nom ?? "";
                int saisonAffiche = item.Saison;
                int episodeAffiche = item.Episode;

                string line =
                    $"{item.Timestamp:dd/MM/yyyy HH:mm}  |  " +
                    $"{mediaType}  |  " +
                    $"{canal}  |  " +
                    $"{titreAffiche}  " +
                    $"{(saisonAffiche > 0 ? $"S{saisonAffiche}" : "")}" +
                    $"{(episodeAffiche > 0 ? $"E{episodeAffiche}" : "")}";

                gfx.DrawString(line, font, XBrushes.Black, new XPoint(20, y));
                y += 15;

                if (y > page.Height - 40)
                {
                    page = doc.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    y = 20;
                }
            }

            using var ms = new MemoryStream();
            doc.Save(ms);
            byte[] pdfBytes = ms.ToArray();

            var dates = backup.Reports
                .Where(r => r.Items != null && r.Items.Count > 0)
                .Select(r => r.Date)
                .OrderBy(d => d)
                .ToList();

            string pdfName;

            if (dates.Count == 1)
                pdfName = $"backup_{dates[0]:yyyy-MM-dd}.pdf";
            else if (dates.Count > 1)
                pdfName = $"backup_{dates.First():yyyy-MM-dd}_to_{dates.Last():yyyy-MM-dd}.pdf";
            else
                pdfName = "backup.pdf";

            ctx.Response.ContentType = "application/pdf";
            ctx.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{pdfName}\"");
            ctx.Response.OutputStream.Write(pdfBytes, 0, pdfBytes.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}
