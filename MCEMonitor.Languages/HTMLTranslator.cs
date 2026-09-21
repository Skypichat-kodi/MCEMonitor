using System;
using System.Text.RegularExpressions;

namespace MediaMonitor.Core.Language
{
    public static class HTMLTranslator
    {
        // Regex compilée pour détecter {{tr:...}}
        private static readonly Regex TrHtmlRegex =
            new(@"\{+\s*tr:([^}]+)\}+", RegexOptions.Compiled | RegexOptions.IgnoreCase);


        /// <summary>
        /// Traduit toutes les occurrences {{tr:clé}} dans un HTML.
        /// </summary>
        public static string Translate(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            return TrHtmlRegex.Replace(html, match =>
            {
                string key = match.Groups[1].Value.Trim();

                // Appel au système de traduction centralisé
                string translated = LanguageManager.Get(key);

                // Si la clé n'existe pas ? on garde la clé brute
                return string.IsNullOrEmpty(translated) ? key : translated;
            });
        }
    }
}

