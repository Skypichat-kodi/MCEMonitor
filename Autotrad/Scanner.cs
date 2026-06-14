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

            bool jsonIsEmpty = existingKeys.Count == 0;

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

                int subIndex = 0;

                foreach (var entry in parsed)
                {
                    string key = entry.Key;
                    string fallback = entry.Fallback;

                    // Cas : pas de clé ? texte non traduit
                    if (string.IsNullOrEmpty(key))
                    {
                        results.Add(new ScanResult
                        {
                            Id = Guid.NewGuid(),
                            SubIndex = subIndex++,

                            FilePath = path,
                            LineNumber = entry.LineNumber,
                            FullLine = entry.Raw,
                            Text = fallback,
                            Preview = entry.Preview,
                            Selected = false,
                            IsTranslated = false,
                            IsMissingKey = false,
                            IsMismatch = false
                        });
                        continue;
                    }

                    // Cas : clé mais JSON vide ou clé absente
                    if (jsonIsEmpty || !existingKeys.ContainsKey(key))
                    {
                        results.Add(new ScanResult
                        {
                            Id = Guid.NewGuid(),
                            SubIndex = subIndex++,

                            FilePath = path,
                            LineNumber = entry.LineNumber,
                            FullLine = entry.Raw,
                            Text = fallback,
                            Preview = entry.Preview,
                            Selected = false,
                            IsTranslated = true,
                            IsMissingKey = true,
                            IsMismatch = false
                        });
                        continue;
                    }

                    // Cas : clé existante ? mismatch ?
                    bool mismatch = existingKeys[key] != fallback;

                    results.Add(new ScanResult
                    {
                        Id = Guid.NewGuid(),
                        SubIndex = subIndex++,

                        FilePath = path,
                        LineNumber = entry.LineNumber,
                        FullLine = entry.Raw,

                        // ?? ON GARDE LE TEXTE EXACT DU PROGRAMME
                        Text = fallback,

                        Preview = entry.Preview,
                        Selected = false,
                        IsTranslated = true,
                        IsMissingKey = false,
                        IsMismatch = mismatch
                    });

                }
            }

            // ---------------------------------------------------------
            // 2) Analyse XAML
            // ---------------------------------------------------------
            if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = XamlParser.Parse(lines);

                int subIndex = 0;

                foreach (var entry in parsed)
                {
                    string key = entry.Key;
                    string fallback = entry.Fallback;

                    // Cas : déjà traduit
                    if (entry.IsTranslated)
                    {
                        if (jsonIsEmpty || !existingKeys.ContainsKey(key))
                        {
                            results.Add(new ScanResult
                            {
                                Id = Guid.NewGuid(),
                                SubIndex = subIndex++,

                                FilePath = path,
                                LineNumber = entry.LineNumber,
                                FullLine = entry.Raw,
                                Text = fallback,
                                Preview = entry.Preview,
                                Selected = false,
                                IsTranslated = true,
                                IsMissingKey = true,
                                IsMismatch = false
                            });
                            continue;
                        }

                        // Cas : clé existante ? mismatch ?
                        bool mismatch = existingKeys[key] != fallback;

                        results.Add(new ScanResult
                        {
                            Id = Guid.NewGuid(),
                            SubIndex = subIndex++,

                            FilePath = path,
                            LineNumber = entry.LineNumber,
                            FullLine = entry.Raw,

                            // ?? ON GARDE LE TEXTE EXACT DU PROGRAMME
                            Text = fallback,

                            Preview = entry.Preview,
                            Selected = false,
                            IsTranslated = true,
                            IsMissingKey = false,
                            IsMismatch = mismatch
                        });


                        continue;
                    }

                    // Cas : non traduit
                    results.Add(new ScanResult
                    {
                        Id = Guid.NewGuid(),
                        SubIndex = subIndex++,

                        FilePath = path,
                        LineNumber = entry.LineNumber,
                        FullLine = entry.Raw,
                        Text = fallback,
                        Preview = entry.Preview,
                        Selected = false,
                        IsTranslated = false,
                        IsMissingKey = false,
                        IsMismatch = false
                    });
                }
            }

            return results;
        }
    }
}

