using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class CsParser
    {
        // Détection LanguageManager.Get("clé")
        private static readonly Regex _regexKey =
            new Regex(@"LanguageManager\.Get\(""([^""]+)""\)",
                RegexOptions.Compiled);

        // Détection stricte des TR : {{tr:clé}}
        private static readonly Regex _regexTr =
            new Regex(@"\{\{tr:([^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<CsEntry> Parse(string[] lines)
        {
            var results = new List<CsEntry>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // ---------------------------------------------------------
                // 1) Détection LanguageManager.Get("clé")
                // ---------------------------------------------------------
                var m = _regexKey.Match(line);
                if (m.Success)
                {
                    string key = m.Groups[1].Value;

                    results.Add(new CsEntry
                    {
                        LineNumber = i + 1,
                        Raw = line,
                        Key = key,
                        Preview = key
                    });
                }

                // ---------------------------------------------------------
                // 2) Détection directe des {{tr:clé}} dans la ligne brute
                // ---------------------------------------------------------
                foreach (Match tr in _regexTr.Matches(line))
                {
                    string key = tr.Groups[1].Value.Trim();

                    results.Add(new CsEntry
                    {
                        LineNumber = i + 1,
                        Raw = line,
                        Key = key,
                        Preview = key
                    });
                }
            }

            return results;
        }
    }

    public class CsEntry
    {
        public int LineNumber { get; set; }
        public string Raw { get; set; } = "";
        public string Key { get; set; } = "";
        public string Preview { get; set; } = "";
    }
}

