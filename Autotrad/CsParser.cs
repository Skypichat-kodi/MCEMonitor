using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class CsParser
    {
        // Détection simple : LanguageManager.Get("clé")
        private static readonly Regex _regexKey =
            new Regex(@"LanguageManager\.Get\(""([^""]+)""\)",
                RegexOptions.Compiled);

        /// <summary>
        /// Analyse un fichier C# ligne par ligne et retourne les clés trouvées.
        /// </summary>
        public static List<CsEntry> Parse(string[] lines)
        {
            var results = new List<CsEntry>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                var m = _regexKey.Match(line);
                if (!m.Success)
                    continue;

                string key = m.Groups[1].Value;

                results.Add(new CsEntry
                {
                    LineNumber = i + 1,
                    Raw = line,
                    Key = key,
                    Preview = line.Trim()
                });
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

