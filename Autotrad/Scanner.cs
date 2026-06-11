using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class Scanner
    {
        private static readonly Regex _regexText =
            new Regex(@"this\.\w+\.Text\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex _regexTranslated =
            new Regex(@"LanguageManager\.Get\(""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex _regexFallback =
            new Regex(@"\?\?\s*""([^""]+)""", RegexOptions.Compiled);

        public static List<ScanResult> ScanFile(string path)
        {
            var results = new List<ScanResult>();

            var lines = File.ReadAllLines(path, Encoding.GetEncoding(1252));

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                foreach (Match m in _regexTranslated.Matches(line))
                {
                    results.Add(new ScanResult
                    {
                        LineNumber = i + 1,
                        FullLine = line.Trim(),
                        Text = m.Groups[1].Value,
                        Selected = false
                    });
                }

                foreach (Match m in _regexText.Matches(line))
                {
                    results.Add(new ScanResult
                    {
                        LineNumber = i + 1,
                        FullLine = line.Trim(),
                        Text = m.Groups[1].Value,
                        Selected = false
                    });
                }

                foreach (Match m in _regexFallback.Matches(line))
                {
                    results.Add(new ScanResult
                    {
                        LineNumber = i + 1,
                        FullLine = line.Trim(),
                        Text = m.Groups[1].Value,
                        Selected = false
                    });
                }
            }

            return results;
        }
    }
}

