using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class CsParser
    {
        // ---------------------------------------------------------
        //  1) Détection LanguageManager.Get("clé")
        // ---------------------------------------------------------
        private static readonly Regex _regexKey =
            new Regex(
                @"LanguageManager\.Get\(""([^""]+)""\)",
                RegexOptions.Compiled);

        // ---------------------------------------------------------
        //  2) Détection des TR : {{tr:clé}}
        // ---------------------------------------------------------
        private static readonly Regex _regexTr =
            new Regex(
                @"\{\{tr:([^}]+)\}\}",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ---------------------------------------------------------
        //  PARSE LIGNE PAR LIGNE
        // ---------------------------------------------------------
        public static List<CsEntry> Parse(string[] lines)
        {
            var results = new List<CsEntry>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // =========================================================
                //  1) LanguageManager.Get("clé") — TOUTES les occurrences
                // =========================================================
                foreach (Match m in _regexKey.Matches(line))
                {
                    string key = m.Groups[1].Value.Trim();

                    if (string.IsNullOrEmpty(key))
                        continue;

                    results.Add(new CsEntry
                    {
                        LineNumber = i + 1,
                        Raw = line,
                        Key = key,
                        Preview = key
                    });
                }

                // =========================================================
                //  2) {{tr:clé}} — TOUTES les occurrences
                // =========================================================
                foreach (Match tr in _regexTr.Matches(line))
                {
                    string key = tr.Groups[1].Value.Trim();

                    if (string.IsNullOrEmpty(key))
                        continue;

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

    // =========================================================
    //  Entrée C# (résultat d'un parsing)
    // =========================================================
    public class CsEntry
    {
        public int LineNumber { get; set; }
        public string Raw { get; set; } = "";
        public string Key { get; set; } = "";
        public string Preview { get; set; } = "";
    }
}