using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Autotrad
{
    public static class Scanner
    {
        public static List<ScanResult> ScanFile(string path, Dictionary<string, string> existingKeys)
        {
            var results = new List<ScanResult>();

            // ---------------------------------------------------------
            // Détection d’encodage
            // ---------------------------------------------------------
            Encoding enc = Utils.DetectEncoding(path);
            var lines = File.ReadAllLines(path, enc);

            // Conversion explicite UTF-8
            for (int i = 0; i < lines.Length; i++)
            {
                byte[] raw = enc.GetBytes(lines[i]);
                lines[i] = Encoding.UTF8.GetString(
                    Encoding.Convert(enc, Encoding.UTF8, raw)
                );
            }

            // ---------------------------------------------------------
            // 1) Analyse C#
            // ---------------------------------------------------------
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = CsParser.Parse(lines);

                foreach (var entry in parsed)
                {
                    if (string.IsNullOrEmpty(entry.Key))
                        continue;

                    bool exists = existingKeys.ContainsKey(entry.Key);

                    results.Add(new ScanResult
                    {
                        FilePath = path,
                        LineNumber = entry.LineNumber,
                        FullLine = entry.Raw,
                        Key = entry.Key,
                        Text = entry.Key,
                        Preview = entry.Raw.Trim(),

                        // ?? AJOUT ESSENTIEL
                        JsonValue = exists ? existingKeys[entry.Key] : "",

                        IsTranslated = exists,
                        IsMissingKey = !exists
                    });
                }
            }

            // ---------------------------------------------------------
            // 2) Analyse XAML
            // ---------------------------------------------------------
            if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = XamlParser.Parse(lines);

                foreach (var entry in parsed)
                {
                    bool exists = existingKeys.ContainsKey(entry.Key);

                    results.Add(new ScanResult
                    {
                        FilePath = path,
                        LineNumber = entry.LineNumber,
                        FullLine = entry.Raw,
                        Key = entry.Key,
                        Text = entry.Key,
                        Preview = entry.Preview,

                        // ?? AJOUT ESSENTIEL
                        JsonValue = exists ? existingKeys[entry.Key] : "",

                        IsTranslated = exists,
                        IsMissingKey = !exists
                    });
                }
            }
            
            // ---------------------------------------------------------
            // 3) Analyse HTML
            // ---------------------------------------------------------
            if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = HtmlParser.Parse(lines);

                foreach (var entry in parsed)
                {
                    bool exists = existingKeys.ContainsKey(entry.Key);

                    results.Add(new ScanResult
                    {
                        FilePath = path,
                        LineNumber = entry.LineNumber,
                        FullLine = entry.Raw,
                        Key = entry.Key,
                        Text = entry.Key,
                        Preview = entry.Preview,
                        JsonValue = exists ? existingKeys[entry.Key] : "",
                        IsTranslated = exists,
                        IsMissingKey = !exists
                    });
                }
            }

            return results;
        }
    }
}

