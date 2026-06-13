using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class Scanner
    {
        private static readonly Regex _regexTranslatedSingle =
            new Regex(@"LanguageManager\.Get\(""([^""]+)""\)\s*\?\?\s*""([^""]*)""", RegexOptions.Compiled);

        private static readonly Regex _regexTextSingle =
            new Regex(@"this\.\w+\.Text\s*=\s*""([^""]*)""", RegexOptions.Compiled);

        // Multi-ligne : this.xxx.Text = [LanguageManager.Get("KEY") ??] "..." + "..." + ...;
        private static readonly Regex _regexMultiline =
            new Regex(
                @"this\.\w+\.Text\s*=\s*(?:LanguageManager\.Get\(""([^""]+)""\)\s*\?\?\s*)?((?:\s*@?""[^""]*""\s*\+\s*)*\s*@?""[^""]*""\s*);",
                RegexOptions.Compiled | RegexOptions.Singleline);

        private static bool IsUselessText(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt))
                return true;

            string t = txt.Trim();

            string[] useless =
            {
                "", ".", "..", "...", "....",
                "-", "--", "---",
                "_", "*",
                "0",
                "()", "[]", "{}"
            };

            if (useless.Contains(t))
                return true;

            if (t.All(c => c == '.' || c == '-' || c == '_' || c == '*'))
                return true;

            return false;
        }
        
private static readonly Regex _regexXamlText =
    new Regex(@"\b(Text|Content|Header|ToolTip)\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

        public static List<ScanResult> ScanFile(string path, Dictionary<string, string> existingKeys)
        {
            var results = new List<ScanResult>();
            var lines = File.ReadAllLines(path, Encoding.GetEncoding(1252));
            string fullText = File.ReadAllText(path, Encoding.GetEncoding(1252));

            // Pour éviter les doublons : on mémorise les numéros de lignes déjà ajoutés
            var usedLines = new HashSet<int>();

            // 1) Détection multi-ligne
            foreach (Match m in _regexMultiline.Matches(fullText))
            {
                string key = m.Groups[1].Value;
                string raw = m.Groups[2].Value;

                // Recompose le texte final
                string txt = string.Join("", Regex.Matches(raw, @"""([^""]*)""")
                    .Select(x => x.Groups[1].Value));

                if (IsUselessText(txt))
                    continue;

                bool isTranslated = !string.IsNullOrEmpty(key);
                bool mismatch = false;

                if (isTranslated && existingKeys.ContainsKey(key))
                    mismatch = existingKeys[key] != txt;
                else if (isTranslated && !existingKeys.ContainsKey(key))
                    mismatch = true;

                int index = fullText.IndexOf(m.Value);
                int lineNumber = fullText.Substring(0, index).Count(c => c == '\n') + 1;

                if (!usedLines.Add(lineNumber))
                    continue;

                results.Add(new ScanResult
                {
                    LineNumber = lineNumber,
                    FullLine = m.Value,
                    Text = txt,
                    Selected = false,
                    IsTranslated = isTranslated,
                    IsMismatch = mismatch
                });
            }

            // 2) Détection simple, ligne par ligne (en évitant les lignes déjà traitées)
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                if (usedLines.Contains(lineNumber))
                    continue;

                string line = lines[i].Trim();

                // 2.1) Déjà traduit simple
                var m1 = _regexTranslatedSingle.Match(line);
                if (m1.Success)
                {
                    string key = m1.Groups[1].Value;
                    string fallback = m1.Groups[2].Value;

                    if (IsUselessText(fallback))
                        continue;

                    bool mismatch = !existingKeys.ContainsKey(key) || existingKeys[key] != fallback;

                    usedLines.Add(lineNumber);

                    results.Add(new ScanResult
                    {
                        LineNumber = lineNumber,
                        FullLine = line,
                        Text = fallback,
                        Selected = false,
                        IsTranslated = true,
                        IsMismatch = mismatch
                    });

                    continue;
                }

                // 2.2) Texte brut simple
                var m2 = _regexTextSingle.Match(line);
                if (m2.Success)
                {
                    string txt = m2.Groups[1].Value;

                    if (IsUselessText(txt))
                        continue;

                    bool isTranslated = existingKeys.Values.Contains(txt);
                    bool mismatch = !isTranslated;

                    usedLines.Add(lineNumber);

                    results.Add(new ScanResult
                    {
                        LineNumber = lineNumber,
                        FullLine = line,
                        Text = txt,
                        Selected = false,
                        IsTranslated = isTranslated,
                        IsMismatch = mismatch
                    });
                }
            }
            // 3) Détection XAML
            if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    var m = _regexXamlText.Match(line);
                    if (!m.Success)
                        continue;

                    string txt = m.Groups[2].Value;

                    if (IsUselessText(txt))
                        continue;

                    bool isTranslated = existingKeys.Values.Contains(txt);
                    bool mismatch = !isTranslated;

                    results.Add(new ScanResult
                    {
                        LineNumber = i + 1,
                        FullLine = line,
                        Text = txt,
                        Selected = false,
                        IsTranslated = isTranslated,
                        IsMismatch = mismatch
                    });
                }
            }
            return results;
        }
    }
}

