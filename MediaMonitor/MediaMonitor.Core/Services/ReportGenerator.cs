using System;
using System.Collections.Generic;
using System.Linq;
using MediaMonitor.Core.Models;

namespace MediaMonitor.Core.Services
{
    public static class ReportGenerator
    {
        public static string GenerateHtmlReport(IEnumerable<MediaUsageItem> items)
        {
            var recent = items.ToList();

            string html = @"
<html><body>
<h1>Rapport de lecture du serveur</h1>
<p>Fichiers lus dans les dernières 24 heures : " + recent.Count + @"</p>
<table border='1'>
<tr><th>Client</th><th>Nom</th><th>Saison</th><th>Épisode</th><th>Fichier</th><th>Heure</th></tr>";

            foreach (var item in recent)
            {
                html += "<tr>" +
                        "<td>" + item.ClientDisplay + "</td>" +
                        "<td>" + item.Nom + "</td>" +
                        "<td>" + item.Saison + "</td>" +
                        "<td>" + item.Episode + "</td>" +
                        "<td>" + item.FileName + "</td>" +
                        "<td>" + item.Timestamp.ToString("HH:mm") + "</td>" +
                        "</tr>";
            }

            html += "</table></body></html>";

            return html;
        }
    }
}

