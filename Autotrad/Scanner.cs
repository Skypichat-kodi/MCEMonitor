using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class Scanner
    {
        private static readonly Regex _regexTranslated =
            new Regex(@"LanguageManager\.Get\(""([^""]+)""\)\s*\?\?\s*""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex _regexText =
            new Regex(@"this\.\w+\.Text\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        public static List<ScanResult> ScanFile(string path, Dictionary<string, string> existingKeys)
        {
            var results = new List<ScanResult>();
            var lines = File.ReadAllLines(path, Encoding.GetEncoding(1252));

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                // 1) Déjà traduit : LanguageManager.Get("Key") ?? "Texte"
                var m1 = _regexTranslated.Match(line);
                if (m1.Success)
                {
                    string key = m1.Groups[1].Value;
                    string fallback = m1.Groups[2].Value;

                    bool mismatch = false;

                    if (!existingKeys.ContainsKey(key))
                        mismatch = true;
                    else if (existingKeys[key] != fallback)
                        mismatch = true;

                    results.Add(new ScanResult
                    {
                        LineNumber = i + 1,
                        FullLine = line,
                        Text = fallback,
                        Selected = false,
                        IsTranslated = true,
                        IsMismatch = mismatch
                    });

                    continue;
                }

                // 2) Texte brut : this.xxx.Text = "Texte"
                var m2 = _regexText.Match(line);
                if (m2.Success)
                {
                    string txt = m2.Groups[1].Value;

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

