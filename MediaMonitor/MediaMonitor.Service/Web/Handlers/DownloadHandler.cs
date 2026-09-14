using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaMonitor.Core.Language;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MediaMonitor.Service.Web.Handlers
{
    public class DownloadHandler
    {
        private const string BackupDir = @"C:\ProgramData\MCEMonitor\Backups";

        public void Download(HandlerContext ctx)
        {
            var files = Directory.GetFiles(BackupDir, "history_*.json");

            if (files.Length == 0)
            {
                HttpWriter.WriteHtml(ctx.Http,
                    HTMLTranslator.Translate(NoBackupHtml));
                return;
            }

            string lastFile = files.OrderByDescending(f => f).First();
            string json = File.ReadAllText(lastFile);

            BackupFileModel? backup = JsonSerializer.Deserialize<BackupFileModel>(json);
            if (backup == null || backup.Reports == null)
            {
                HttpWriter.WriteHtml(ctx.Http,
                    "<html><body><h2>{{tr:Sauvegarde invalide}}</h2></body></html>");
                return;
            }

            var items = backup.Reports
                .Where(r => r.Items != null)
                .SelectMany(r => r.Items)
                .OrderByDescending(i => i.Timestamp)
                .ToList();

            byte[] pdfBytes = BuildPdf(backup, items);

            string pdfName = BuildPdfName(backup);

            ctx.Http.Response.ContentType = "application/pdf";
            ctx.Http.Response.AddHeader("Content-Disposition",
                $"attachment; filename=\"{pdfName}\"");
            ctx.Http.Response.OutputStream.Write(pdfBytes, 0, pdfBytes.Length);
            ctx.Http.Response.OutputStream.Close();
        }

        private static byte[] BuildPdf(BackupFileModel backup, List<MediaUsageItem> items)
        {
            using var doc = new PdfDocument();
            doc.Info.Title = "Backup MediaMonitor";

            var page = doc.AddPage();
            page.Orientation = PdfSharp.PageOrientation.Landscape;

            var gfx = XGraphics.FromPdfPage(page);

            var titleFont  = new XFont("Arial", 18, XFontStyle.Bold);
            var headerFont = new XFont("Arial", 10, XFontStyle.Bold);
            var textFont   = new XFont("Arial", 9);
            var footerFont = new XFont("Arial", 8);

            double y = 20;
            int pageNumber = 1;

            DrawBlueBanner(gfx, page, titleFont);

            y = 70;
            DrawHeaderInfo(gfx, backup, items, headerFont, y);
            y += 25;

            DrawTableHeader(gfx, headerFont, y);
            y += 40;

            bool alternate = false;

            foreach (var item in items)
            {
                if (y > page.Height - 40)
                {
                    DrawFooter(gfx, footerFont, page, pageNumber);
                    page = doc.AddPage();
                    page.Orientation = PdfSharp.PageOrientation.Landscape;
                    gfx = XGraphics.FromPdfPage(page);
                    pageNumber++;
                    y = 20;
                    DrawTableHeader(gfx, headerFont, y);
                    y += 40;
                }

                if (alternate)
                {
                    gfx.DrawRectangle(XBrushes.WhiteSmoke, 20, y - 10, 780, 18);
                }
                alternate = !alternate;

                DrawTableRow(gfx, item, textFont, y);
                y += 18;
            }

            DrawFooter(gfx, footerFont, page, pageNumber);

            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }

        private static void DrawBlueBanner(XGraphics gfx, PdfPage page, XFont titleFont)
        {
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(0, 122, 204)),
                0, 0, page.Width, 45);
            gfx.DrawString("MediaMonitor", titleFont, XBrushes.White,
                new XRect(0, 8, page.Width, 30), XStringFormats.Center);
        }

        private static void DrawHeaderInfo(XGraphics gfx, BackupFileModel backup,
            List<MediaUsageItem> items,
            XFont headerFont, double y)
        {
            gfx.DrawString($"Historique sur {backup.RetentionDays} jours",
                headerFont, XBrushes.Black, 20, y);
            gfx.DrawString($"Evénements : {items.Count}",
                headerFont, XBrushes.Black, 220, y);
            gfx.DrawString($"Généré le {DateTime.Now:dd/MM/yyyy HH:mm}",
                headerFont, XBrushes.Black, 420, y);
        }

        private static void DrawTableHeader(XGraphics gfx, XFont headerFont, double y)
        {
            gfx.DrawRectangle(XBrushes.DarkGray, 20, y, 780, 20);
            gfx.DrawString("Date",   headerFont, XBrushes.White, 25,  y + 14);
            gfx.DrawString("Client", headerFont, XBrushes.White, 140, y + 14);
            gfx.DrawString("Type",   headerFont, XBrushes.White, 260, y + 14);
            gfx.DrawString("Canal",  headerFont, XBrushes.White, 340, y + 14);
            gfx.DrawString("Titre",  headerFont, XBrushes.White, 430, y + 14);
            gfx.DrawString("S/E",    headerFont, XBrushes.White, 730, y + 14);
        }

        private static void DrawTableRow(XGraphics gfx, MediaUsageItem item,
            XFont textFont, double y)
        {
            string mediaType = item.MediaType ?? "";
            string client = item.ClientDisplay ?? "";
            string canal = item.Channel ?? "";
            string titre = item.Nom ?? "";

            if (titre.Length > 60)
                titre = titre.Substring(0, 60) + "...";

            string se = "";
            if (item.Saison > 0)
            {
                se = $"S{item.Saison:D2}";
                if (item.Episode > 0) se += $"E{item.Episode:D2}";
            }

            XBrush typeBrush = mediaType.ToUpperInvariant() switch
            {
                "AUDIO" => XBrushes.SteelBlue,
                "SERIE" => XBrushes.MediumPurple,
                "VIDEO" => XBrushes.DarkOrange,
                "REC"   => XBrushes.Red,
                "TV"    => XBrushes.DarkGoldenrod,
                _       => XBrushes.Black
            };

            gfx.DrawString(item.Timestamp.ToString("dd/MM/yyyy HH:mm"), textFont, XBrushes.Black, 25,  y);
            gfx.DrawString(client,    textFont, XBrushes.Black, 140, y);
            gfx.DrawString(mediaType, textFont, typeBrush,      260, y);
            gfx.DrawString(canal,     textFont, XBrushes.Black, 340, y);
            gfx.DrawString(titre,     textFont, XBrushes.Black, 430, y);
            gfx.DrawString(se,        textFont, XBrushes.Black, 730, y);
        }

        private static void DrawFooter(XGraphics gfx, XFont footerFont, PdfPage page, int pageNumber)
        {
            gfx.DrawString($"Page {pageNumber}", footerFont, XBrushes.Gray,
                new XRect(0, page.Height - 15, page.Width - 20, 20),
                XStringFormats.CenterRight);
        }

        private static string BuildPdfName(BackupFileModel backup)
        {
            var dates = backup.Reports
                .Where(r => r.Items != null && r.Items.Count > 0)
                .Select(r => r.Date)
                .OrderBy(d => d)
                .ToList();

            if (dates.Count == 1) return $"backup_{dates[0]:yyyy-MM-dd}.pdf";
            if (dates.Count > 1)  return $"backup_{dates.First():yyyy-MM-dd}_to_{dates.Last():yyyy-MM-dd}.pdf";
            return "backup.pdf";
        }

        private const string NoBackupHtml = @"
            <html>
            <head>
                <meta charset='utf-8'>
                <title>{{tr:Aucune sauvegarde}}</title>
                <link rel=""icon"" type=""image/x-icon"" href=""/favicon.ico"">
                <style>
                    body { background-color: #1e1e1e; color: #ffffff; font-family: Segoe UI, Arial, sans-serif; margin: 0; padding: 40px; }
                    .container { max-width: 700px; margin: auto; background: #2b2b2b; padding: 25px; border-radius: 8px; box-shadow: 0 0 10px #000; text-align: center; }
                    h2 { color: #f55; }
                    a.btn { display: inline-block; margin-top: 20px; padding: 10px 18px; background: #444; color: white; text-decoration: none; border-radius: 5px; font-weight: bold; }
                    a.btn:hover { background: #666; }
                </style>
            </head>
            <body>
                <div class='container'>
                    <h2>{{tr:Aucune sauvegarde disponible}}</h2>
                    <a href='/' class='btn'>{{tr:Retour}}</a>
                </div>
            </body>
            </html>";
    }
}