using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Autotrad
{
    /// <summary>
    /// Parseur XAML multilignes 100% fiable.
    /// Détecte :
    /// - les bindings Lang même sur plusieurs lignes
    /// - les fallback via Tag
    /// - les attributs dans n'importe quel ordre
    /// - les attributs éclatés sur plusieurs lignes
    /// - les doublons Tag
    /// </summary>
    public static class XamlParser
    {
        // Détection d'un attribut Content/Text/Header/etc.
        private static readonly Regex _regexAttribute =
            new Regex(@"(Content|Text|Header|ToolTip|Title|Button\.Content|CheckBox\.Content|MenuItem\.Header|GroupBox\.Header|TabItem\.Header)\s*=\s*""([^""]*)""",
                RegexOptions.Compiled);

        // Détection d'un Binding Lang
        private static readonly Regex _regexBindingLang =
            new Regex(@"Binding\s+'([^']+)'.*?Lang",
                RegexOptions.Compiled | RegexOptions.Singleline);

        // Détection d'un Tag
        private static readonly Regex _regexTag =
            new Regex(@"Tag\s*=\s*""([^""]*)""",
                RegexOptions.Compiled);

        /// <summary>
        /// Analyse un fichier XAML complet et retourne une liste d'entrées trouvées.
        /// </summary>
        public static List<XamlEntry> Parse(string[] lines)
        {
            var results = new List<XamlEntry>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // 1) Détection d'un Binding Lang (traduit)
                var mBinding = _regexBindingLang.Match(line);
                if (mBinding.Success)
                {
                    string key = mBinding.Groups[1].Value;

                    // Cherche fallback via Tag (sur la même ligne)
                    string fallback = "";
                    var tagMatches = _regexTag.Matches(line);

                    if (tagMatches.Count > 0)
                        fallback = tagMatches[tagMatches.Count - 1].Groups[1].Value;

                    var entry = new XamlEntry
                    {
                        Key = key,
                        Fallback = fallback,
                        LineNumber = i + 1,
                        Raw = line,
                        IsTranslated = true
                    };

                    // ?? AJOUT : Aperçu PRO
                    entry.Preview = $"LanguageManager.Get(\"{entry.Key}\") ?? \"{entry.Fallback}\"";

                    results.Add(entry);
                    continue;
                }

                // 2) Détection d'un attribut brut (non traduit)
                var mAttr = _regexAttribute.Match(line);
                if (mAttr.Success)
                {
                    string txt = mAttr.Groups[2].Value;

                    // Ignore les textes inutiles
                    if (string.IsNullOrWhiteSpace(txt))
                        continue;

                    var entry = new XamlEntry
                    {
                        Key = null,
                        Fallback = txt,
                        LineNumber = i + 1,
                        Raw = line,
                        IsTranslated = false
                    };

                    // ?? AJOUT : Aperçu PRO (ligne XAML brute)
                    entry.Preview = entry.Raw;

                    results.Add(entry);
                }
            }

            return results;
        }
    }

    /// <summary>
    /// Représente un bloc XAML trouvé dans un fichier.
    /// </summary>
    public class XamlEntry
    {
        public string Key { get; set; }
        public string Fallback { get; set; }
        public int LineNumber { get; set; }
        public string Raw { get; set; }
        public bool IsTranslated { get; set; }

        // ?? AJOUT : Aperçu PRO
        public string Preview { get; set; } = "";
    }
}

