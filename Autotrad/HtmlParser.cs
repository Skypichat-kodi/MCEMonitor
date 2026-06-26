using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class HtmlParser
    {
        // Détection stricte : {{tr:clé}}
        private static readonly Regex _regexTr =
            new Regex(@"\{\{tr:([^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ---------------------------------------------------------
        // PARSE LIGNE PAR LIGNE
        // ---------------------------------------------------------
        public static List<XamlEntry> Parse(string[] lines)
        {
            var results = new List<XamlEntry>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                foreach (Match m in _regexTr.Matches(line))
                {
                    string key = m.Groups[1].Value.Trim();

                    results.Add(new XamlEntry
                    {
                        Key = key,
                        LineNumber = i + 1,
                        Raw = line,
                        Preview = key   // propre, sans {{ }}
                    });
                }
            }

            return results;
        }

        // ---------------------------------------------------------
        // PARSE D’UN FRAGMENT HTML (chaînes C#)
        // ---------------------------------------------------------
        public static List<XamlEntry> ParseFragment(string html, int lineNumber, string rawLine)
        {
            var results = new List<XamlEntry>();

            foreach (Match m in _regexTr.Matches(html))
            {
                string key = m.Groups[1].Value.Trim();

                results.Add(new XamlEntry
                {
                    Key = key,
                    LineNumber = lineNumber,
                    Raw = rawLine,
                    Preview = key
                });
            }

            return results;
        }
    }
}

