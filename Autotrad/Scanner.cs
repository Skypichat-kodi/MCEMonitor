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

                // 1?? Ligne déjà traduite : LanguageManager.Get("KEY") ?? "fallback"
                var m1 = _regexTranslated.Match(line);
                if (m1.Success)
                {
                    results.Add(new ScanResult
                    {
                        LineNumber = i + 1,
                        FullLine = line,
                        Text = m1.Groups[2].Value, // fallback
                        Selected = false,
                        IsTranslated = true
                    });
                    continue;
                }

                // 2?? Ligne simple : this.xxx.Text = "Texte"
                var m2 = _regexText.Match(line);
                if (m2.Success)
                {
                    string txt = m2.Groups[1].Value;

                    results.Add(new ScanResult
                    {
                        LineNumber = i + 1,
                        FullLine = line,
                        Text = txt,
                        Selected = false,
                        IsTranslated = existingKeys.Values.Contains(txt)
                    });
                }
            }

            return results;
        }
    }
}

