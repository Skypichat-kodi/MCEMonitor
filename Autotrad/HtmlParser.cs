using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class HtmlParser
    {
        // ---------------------------------------------------------
        //  Détection stricte : {{tr:clé}}
        // ---------------------------------------------------------
        private static readonly Regex _regexTr =
            new Regex(
                @"\{\{tr:([^}]+)\}\}",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ---------------------------------------------------------
        //  PARSE LIGNE PAR LIGNE
        // ---------------------------------------------------------
        public static List<XamlEntry> Parse(string[] lines)
        {
            var results = new List<XamlEntry>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // =========================================================
                //  {{tr:clé}} — TOUTES les occurrences
                // =========================================================
                foreach (Match m in _regexTr.Matches(line))
                {
                    string key = m.Groups[1].Value.Trim();

                    if (string.IsNullOrEmpty(key))
                        continue;

                    results.Add(new XamlEntry
                    {
                        Key = key,
                        LineNumber = i + 1,
                        Raw = line,
                        Preview = key
                    });
                }
            }

            return results;
        }
    }
}