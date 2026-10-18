using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using NetMonitor.Service.Models;

namespace NetMonitor.Service
{
    public static class PdfExporter
    {
        public static byte[] GenerateReport(
            string machineName,
            List<IntrusionEvent> history,
            List<BlockedIpInfo> blocked,
            List<IntrusionEvent> audit)
        {
            using var document = new PdfDocument();
            document.Info.Title = "Rapport NetMonitor";
            document.Info.Author = "MCEMonitor";

            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var fontTitle = new XFont("Arial", 20, XFontStyle.Bold);
            var fontSub = new XFont("Arial", 12, XFontStyle.Regular);
            var fontHeader = new XFont("Arial", 10, XFontStyle.Bold);
            var fontText = new XFont("Arial", 9, XFontStyle.Regular);

            double y = 40;
            double margin = 40;

            // --- En-tête ---
            gfx.DrawString("Rapport NetMonitor", fontTitle, XBrushes.DarkBlue, new XPoint(margin, y));
            y += 25;
            gfx.DrawString($"Machine : {machineName}", fontSub, XBrushes.Black, new XPoint(margin, y));
            y += 18;
            gfx.DrawString($"Généré le : {DateTime.Now:dd/MM/yyyy HH:mm:ss}", fontSub, XBrushes.Gray, new XPoint(margin, y));
            y += 30;

            // --- Tableau 1 : Historique des intrusions ---
            gfx.DrawString($"Historique des intrusions ({history.Count})", fontHeader, XBrushes.Black, new XPoint(margin, y));
            y += 15;

            gfx.DrawLine(XPens.Gray, margin, y, page.Width - margin, y);
            y += 5;

            foreach (var item in history.OrderByDescending(x => x.Timestamp).Take(30))
            {
                if (y > page.Height - 50)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    y = margin;
                }

                string line = $"{item.Timestamp:dd/MM HH:mm} | {item.Severity,-8} | {item.Source,-15} | {item.RemoteIp} | {Truncate(item.Message, 60)}";
                gfx.DrawString(line, fontText, XBrushes.Black, new XPoint(margin, y));
                y += 14;
            }
            y += 20;

            // --- Tableau 2 : IP Bloquées ---
            if (y > page.Height - 100)
            {
                page = document.AddPage();
                gfx = XGraphics.FromPdfPage(page);
                y = margin;
            }

            gfx.DrawString($"IP Bloquées au pare-feu ({blocked.Count})", fontHeader, XBrushes.Black, new XPoint(margin, y));
            y += 15;
            gfx.DrawLine(XPens.Gray, margin, y, page.Width - margin, y);
            y += 5;

            foreach (var item in blocked.OrderByDescending(x => x.BlockedAt).Take(20))
            {
                if (y > page.Height - 50)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    y = margin;
                }

                string line = $"{item.BlockedAt.ToLocalTime():dd/MM HH:mm} | {item.Ip,-15} | {item.Source,-10} | {Truncate(item.Message, 50)}";
                gfx.DrawString(line, fontText, XBrushes.DarkRed, new XPoint(margin, y));
                y += 14;
            }

            // --- Tableau 3 : Audit (Whitelist) ---
            y += 20;
            if (y > page.Height - 100)
            {
                page = document.AddPage();
                gfx = XGraphics.FromPdfPage(page);
                y = margin;
            }

            gfx.DrawString($"Alertes ignorées (IP de confiance) ({audit.Count})", fontHeader, XBrushes.Black, new XPoint(margin, y));
            y += 15;
            gfx.DrawLine(XPens.Gray, margin, y, page.Width - margin, y);
            y += 5;

            foreach (var item in audit.OrderByDescending(x => x.Timestamp).Take(20))
            {
                if (y > page.Height - 50)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    y = margin;
                }

                string line = $"{item.Timestamp:dd/MM HH:mm} | {item.Source,-15} | {item.RemoteIp,-15} | {Truncate(item.Message, 50)}";
                gfx.DrawString(line, fontText, XBrushes.Gray, new XPoint(margin, y));
                y += 14;
            }

            using var ms = new MemoryStream();
            document.Save(ms, false);
            return ms.ToArray();
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3) + "...";
        }
    }
}