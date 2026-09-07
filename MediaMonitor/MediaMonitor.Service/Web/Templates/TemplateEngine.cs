using System;
using System.Text.RegularExpressions;
using MediaMonitor.Core.Language;

namespace MediaMonitor.Service.Web.Templates
{
    public static class TemplateEngine
    {
        // ======================================================================
        //  TRADUCTION HTML VIA {{tr:...}}
        // ======================================================================

        private static readonly Regex TrHtmlRegex =
            new(@"\{\{tr:(.+?)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string TranslateHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            return TrHtmlRegex.Replace(html, match =>
            {
                string key = match.Groups[1].Value.Trim();
                string translated = LanguageManager.Get(key) ?? key;
                return string.IsNullOrEmpty(translated) ? key : translated;
            });
        }

        // ======================================================================
        //  TRADUCTION GLOBALE (HTMLTranslator)
        // ======================================================================

        public static string Translate(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            // Applique d'abord {{tr:...}}
            html = TranslateHtml(html);

            // Puis ton système de traduction complet
            html = HTMLTranslator.Translate(html);

            return html;
        }

        // ======================================================================
        //  BLOCS CONDITIONNELS MUSTACHE {{#Tag}} ... {{/Tag}}
        // ======================================================================

        public static string ApplyConditional(string html, string tag, bool condition)
        {
            string start = "{{#" + tag + "}}";
            string end = "{{/" + tag + "}}";

            while (true)
            {
                int i1 = html.IndexOf(start, StringComparison.Ordinal);
                if (i1 < 0) break;

                int i2 = html.IndexOf(end, i1, StringComparison.Ordinal);
                if (i2 < 0) break;

                int blockEnd = i2 + end.Length;

                // Bloc complet incluant les balises
                string block = html.Substring(i1, blockEnd - i1);

                if (condition)
                {
                    // Contenu interne (sans les balises)
                    string inner = html.Substring(i1 + start.Length, i2 - (i1 + start.Length));
                    html = html.Replace(block, inner);
                }
                else
                {
                    // On supprime tout le bloc
                    html = html.Replace(block, "");
                }
            }

            return html;
        }
    }
}
