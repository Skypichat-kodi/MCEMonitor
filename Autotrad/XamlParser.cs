using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class XamlParser
    {
        // ---------------------------------------------------------
        //  1) {loc:Tr 'clé'} ou {loc:Tr "clé"}
        // ---------------------------------------------------------
        private static readonly Regex _regexLocTr =
            new Regex(
                @"\{loc:Tr\s*['""]([^'""]+)['""]\}",
                RegexOptions.Compiled);

        // ---------------------------------------------------------
        //  2) Binding 'clé' ... Lang
        // ---------------------------------------------------------
        private static readonly Regex _regexBindingLang =
            new Regex(
                @"Binding\s+'([^']+)'.*?Lang",
                RegexOptions.Compiled | RegexOptions.Singleline);

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
                //  1) {loc:Tr 'clé'} — TOUTES les occurrences
                // =========================================================
                foreach (Match m in _regexLocTr.Matches(line))
                {
                    string key = m.Groups[1].Value.Trim();

                    if (string.IsNullOrEmpty(key))
                        continue;

                    results.Add(new XamlEntry
                    {
                        Key = key,
                        LineNumber = i + 1,
                        Raw = line,
                        Preview = line
                    });
                }

                // =========================================================
                //  2) Binding 'clé' ... Lang
                // =========================================================
                var mBind = _regexBindingLang.Match(line);
                if (mBind.Success)
                {
                    string key = mBind.Groups[1].Value.Trim();

                    if (!string.IsNullOrEmpty(key))
                    {
                        results.Add(new XamlEntry
                        {
                            Key = key,
                            LineNumber = i + 1,
                            Raw = line,
                            Preview = line
                        });
                    }
                }
            }

            return results;
        }
    }

    // =========================================================
    //  Entrée XAML (résultat d'un parsing)
    // =========================================================
    public class XamlEntry
    {
        public string Key { get; set; } = "";
        public int LineNumber { get; set; }
        public string Raw { get; set; } = "";
        public string Preview { get; set; } = "";
    }
}