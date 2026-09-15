using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using MediaMonitor.Core.Models;
using MediaMonitor.Service.Web;

namespace MediaMonitor.Service.Web.Reports
{
    /// <summary>
    /// Construit le HTML du rapport à partir d'une liste de MediaUsageItem.
    /// Utilisé à la fois par la route /report et par l'envoi d'email.
    /// </summary>
    public static class ReportHtmlBuilder
    {
        public static string Build(IEnumerable<MediaUsageItem> items)
        {
            var list = items as List<MediaUsageItem> ?? new List<MediaUsageItem>(items);

            int totalMedias = list.Count;

            if (totalMedias == 0)
                return "<html><body><h2>Aucun fichier ouvert depuis le démarrage du service.</h2></body></html>";

            int mediasParPage = 200;
            int totalPages = (int)Math.Ceiling(totalMedias / (double)mediasParPage);

            // Statistiques par type
            var countByType = new Dictionary<string, int>();
            foreach (var item in list)
            {
                string type = item.MediaType ?? "Autre";
                if (!countByType.ContainsKey(type))
                    countByType[type] = 0;
                countByType[type]++;
            }

            var typeStatsSb = new StringBuilder();
            foreach (var kv in countByType)
                typeStatsSb.Append($"<li><b>{WebUtility.HtmlEncode(kv.Key)}</b> : {kv.Value}</li>");

            // Lignes du tableau
            var rowsSb = new StringBuilder();
            foreach (var item in list)
            {
                rowsSb.Append("<tr>");
                rowsSb.Append($"<td>{item.Timestamp:HH:mm:ss}</td>");
                rowsSb.Append($"<td>{WebUtility.HtmlEncode(item.ClientDisplay ?? "")}</td>");
                rowsSb.Append($"<td>{WebUtility.HtmlEncode(item.MediaType ?? "")}</td>");
                rowsSb.Append($"<td>{WebUtility.HtmlEncode(item.Nom ?? "")}</td>");
                rowsSb.Append($"<td>{item.Saison}</td>");
                rowsSb.Append($"<td>{item.Episode}</td>");
                rowsSb.Append($"<td>{WebUtility.HtmlEncode(item.FileName ?? "")}</td>");
                rowsSb.Append($"<td>{WebUtility.HtmlEncode(item.Path ?? "")}</td>");
                rowsSb.Append("</tr>");
            }

            var model = new Dictionary<string, object?>
            {
                ["TotalMedias"]    = totalMedias,
                ["TotalPages"]     = totalPages,
                ["MediasPerPage"]  = mediasParPage,
                ["TypeStats"]      = typeStatsSb.ToString(),
                ["Rows"]           = rowsSb.ToString()
            };

            return ViewRenderer.Render("Report.html", model);
        }
    }
}