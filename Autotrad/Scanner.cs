using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Autotrad
{
    public static class Scanner
    {
        // =========================================================
        //  SCAN D'UN FICHIER UNIQUE
        // =========================================================
        public static List<ScanResult> ScanFile(string path, Dictionary<string, string> existingKeys)
        {
            var results = new List<ScanResult>();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return results;

            // ---------------------------------------------------------
            //  Détection d'encodage
            //  ?? File.ReadAllLines fait DÉJÀ la conversion ? pas de
            //     double conversion nécessaire (bug corrigé)
            // ---------------------------------------------------------
            Encoding enc = Utils.DetectEncoding(path);
            string[] lines = File.ReadAllLines(path, enc);

            // ---------------------------------------------------------
            //  1) Analyse C#
            // ---------------------------------------------------------
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = CsParser.Parse(lines);

                foreach (var entry in parsed)
                {
                    if (string.IsNullOrEmpty(entry.Key))
                        continue;

                    results.Add(BuildResult(path, entry.LineNumber, entry.Raw, entry.Key, entry.Preview, existingKeys));
                }
            }

            // ---------------------------------------------------------
            //  2) Analyse XAML
            // ---------------------------------------------------------
            else if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = XamlParser.Parse(lines);

                foreach (var entry in parsed)
                {
                    if (string.IsNullOrEmpty(entry.Key))
                        continue;

                    results.Add(BuildResult(path, entry.LineNumber, entry.Raw, entry.Key, entry.Preview, existingKeys));
                }
            }

            // ---------------------------------------------------------
            //  3) Analyse HTML
            // ---------------------------------------------------------
            else if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = HtmlParser.Parse(lines);

                foreach (var entry in parsed)
                {
                    if (string.IsNullOrEmpty(entry.Key))
                        continue;

                    results.Add(BuildResult(path, entry.LineNumber, entry.Raw, entry.Key, entry.Preview, existingKeys));
                }
            }

            return results;
        }

        // =========================================================
        //  CONSTRUCTION D'UN RÉSULTAT UNIFIÉ
        // =========================================================
        private static ScanResult BuildResult(
            string path,
            int lineNumber,
            string rawLine,
            string key,
            string preview,
            Dictionary<string, string> existingKeys)
        {
            bool exists = existingKeys.ContainsKey(key);

            return new ScanResult
            {
                FilePath = path,
                LineNumber = lineNumber,
                FullLine = rawLine,
                Key = key,
                Text = key,
                Preview = preview,
                JsonValue = exists ? existingKeys[key] : "",
                IsTranslated = exists,
                IsMissingKey = !exists
            };
        }
    }
}