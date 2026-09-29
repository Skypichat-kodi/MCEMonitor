using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaMonitor.Core.Models;
using MediaMonitor.UI.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MediaMonitor.UI.Services
{
    public static class PdfBackupGenerator
    {
        public static void Generate(BackupFileModel backup, string outputPath)
        {
            var items = BackupLoader.Flatten(backup);

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
            gfx.DrawString($"Historique sur {backup.RetentionDays} jours",
                headerFont, XBrushes.Black, 20, y);
            gfx.DrawString($"Événements : {items.Count}",
                headerFont, XBrushes.Black, 220, y);
            gfx.DrawString($"Généré le {DateTime.Now:dd/MM/yyyy HH:mm}",
                headerFont, XBrushes.Black, 420, y);
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
                    gfx.DrawRectangle(XBrushes.WhiteSmoke, 20, y - 10, 780, 18);
                alternate = !alternate;

                DrawTableRow(gfx, item, textFont, y);
                y += 18;
            }

            DrawFooter(gfx, footerFont, page, pageNumber);
            doc.Save(outputPath);
        }

        private static void DrawBlueBanner(XGraphics gfx, PdfPage page, XFont titleFont)
        {
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(0, 122, 204)),
                0, 0, page.Width, 45);
            gfx.DrawString("MediaMonitor", titleFont, XBrushes.White,
                new XRect(0, 8, page.Width, 30), XStringFormats.Center);
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

        private static void DrawTableRow(XGraphics gfx, MediaUsageItem item, XFont textFont, double y)
        {
            string mediaType = item.MediaType ?? "";
            string client    = item.ClientDisplay ?? "";
            string canal     = item.Channel ?? "";
            string titre     = item.Nom ?? "";

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
                "IMAGE" => new XSolidBrush(XColor.FromArgb(76, 175, 80)),
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
    }
}