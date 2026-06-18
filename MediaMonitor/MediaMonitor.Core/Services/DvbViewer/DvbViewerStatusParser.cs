using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace MediaMonitor.Core.DvbViewer
{
    public static class DvbViewerStatusParser
    {
        public static List<DvbViewerClientStream> Parse(string html)
        {
            var result = new List<DvbViewerClientStream>();

            // 1) TUNERS ACTIFS (enregistrements)
            var tuners = ParseActiveTuners(html);

            foreach (var t in tuners)
            {
                string shortName = ReduceTunerName(t.TunerName);

                result.Add(new DvbViewerClientStream
                {
                    Client = shortName,
                    Type = $"REC {t.Channel}",
                    Nom = t.Title
                });
            }

            // 2) CLIENTS LIVE TV
            var clients = ParseClients(html);

            foreach (var c in clients)
            {
                result.Add(new DvbViewerClientStream
                {
                    Client = c.Ip,
                    Type = "TV",
                    Nom = c.Type
                });
            }

            return result;
        }

        // ---------------------------------------------------------
        // TUNERS ACTIFS
        // ---------------------------------------------------------
private static List<DvbTunerInfo> ParseActiveTuners(string html)
{
    var list = new List<DvbTunerInfo>();

    // Match les headers de tuner
    var tunerRegex = new Regex(
        @"<th[^>]*colspan=""4""[^>]*>(.*?)</th>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    var matches = tunerRegex.Matches(html);

    // Match toutes les lignes <tr class="even"> (enregistrements)
    var trRegex = new Regex(
        @"<tr class=""even""[^>]*>(.*?)</tr>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    var trMatches = trRegex.Matches(html);

    foreach (Match tr in trMatches)
    {
        int trIndex = tr.Index;

        // Trouver le tuner le plus proche au-dessus
        Match bestTuner = null;

        foreach (Match tuner in matches)
        {
            if (tuner.Index < trIndex)
                bestTuner = tuner;
        }

        if (bestTuner == null)
            continue;

        string tunerName = Strip(bestTuner.Groups[1].Value);
        string trHtml = tr.Value;

        string channel = Extract(trHtml, @"<td[^>]*colspan=""2""[^>]*class=""top""[^>]*>(.*?)</td>");
        string title   = Extract(trHtml, @"<td[^>]*class=""top""[^>]*>(.*?)</td>", 2);

        if (string.IsNullOrWhiteSpace(channel))
            continue;

        list.Add(new DvbTunerInfo
        {
            TunerName = tunerName,
            Channel   = channel,
            Title     = title
        });
    }

    return list;
}


        // ---------------------------------------------------------
        // CLIENTS LIVE TV
        // ---------------------------------------------------------
        private static List<(string Ip, string Type)> ParseClients(string html)
        {
            var list = new List<(string Ip, string Type)>();

            int headerIndex = html.IndexOf("<th>Clients</th>", StringComparison.OrdinalIgnoreCase);
            if (headerIndex < 0) return list;

            int tableStart = html.LastIndexOf("<table", headerIndex, StringComparison.OrdinalIgnoreCase);
            int tableEnd = html.IndexOf("</table>", headerIndex, StringComparison.OrdinalIgnoreCase);
            if (tableStart < 0 || tableEnd < 0) return list;

            string table = html.Substring(tableStart, tableEnd - tableStart);

            var rowRegex = new Regex(
                @"<tr[^>]*>\s*<td[^>]*>(.*?)</td>\s*<td[^>]*>(.*?)</td>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match m in rowRegex.Matches(table))
            {
                string ip = Strip(m.Groups[1].Value);
                string type = Strip(m.Groups[2].Value);

                if (!string.IsNullOrWhiteSpace(ip))
                    list.Add((ip, type));
            }

            return list;
        }

        // ---------------------------------------------------------
        // HELPERS
        // ---------------------------------------------------------
        private static string Extract(string text, string pattern, int occurrence = 1)
        {
            var matches = Regex.Matches(text, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matches.Count < occurrence) return "";
            return Strip(matches[occurrence - 1].Groups[1].Value);
        }

        private static string Strip(string html)
        {
            string text = Regex.Replace(html, "<.*?>", "").Trim();
            return WebUtility.HtmlDecode(text);
        }

        private static string ReduceTunerName(string full)
        {
            var m = Regex.Match(full, @"(DVB-[A-Z].*?\(\d+\))");
            if (m.Success)
                return m.Groups[1].Value;

            return full;
        }
    }
}

