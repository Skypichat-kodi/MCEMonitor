using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MCEMonitor.Languages;

namespace MediaMonitor.Service.Web
{
    /// <summary>
    /// Moteur de template maison.
    /// Syntaxe supportée :
    ///   {{var}}                      ? remplacement simple
    ///   {{tr:clé}}                   ? traduction via LanguageManager
    ///   {{#IfTag}}...{{/IfTag}}      ? bloc conditionnel (bool)
    ///   {{#for:Items}}...{{/for:Items}} ? boucle sur une liste de dictionnaires
    ///   {{Item.Prop}}                ? propriété d'un item (dans une boucle)
    /// </summary>
    public static class TemplateEngine
    {
        private static readonly Regex TrRegex =
            new(@"\{\{tr:([^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex VarRegex =
            new(@"\{\{([A-Za-z0-9_\.]+)\}\}", RegexOptions.Compiled);

        // ---------------------------------------------------------------
        //  Point d'entrée principal
        // ---------------------------------------------------------------
        public static string Render(string template, IDictionary<string, object?> model)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            string html = template;

            // 1) Boucles
            html = ProcessLoops(html, model);

            // 2) Conditionnels
            html = ProcessConditionals(html, model);

            // 3) Traductions (passe 1)
            html = TranslatePass(html);

            // 4) Variables
            html = VarRegex.Replace(html, m =>
            {
                string key = m.Groups[1].Value;
                if (key.StartsWith("#") || key.StartsWith("/"))
                    return m.Value;

                if (model.TryGetValue(key, out var val))
                    return val?.ToString() ?? "";

                return "";
            });

            // 5) Traductions (passe 2) — pour les {{tr:...}} injectés par les variables
            html = TranslatePass(html);

            return html;
        }

        private static string TranslatePass(string html)
        {
            return TrRegex.Replace(html, m =>
            {
                string key = m.Groups[1].Value.Trim();
                string translated = LanguageManager.Get(key);
                return string.IsNullOrEmpty(translated) ? key : translated;
            });
        }

        // ---------------------------------------------------------------
        //  Conditionnels
        // ---------------------------------------------------------------
        private static string ProcessConditionals(string html, IDictionary<string, object?> model)
        {
            // On boucle tant qu'il reste des {{#If...}} à traiter
            // (utile si des blocs sont imbriqués)
            while (true)
            {
                var match = Regex.Match(html, @"\{\{#(If[A-Za-z0-9_]+)\}\}");
                if (!match.Success)
                    break;

                string tag = match.Groups[1].Value;
                string start = "{{#" + tag + "}}";
                string end = "{{/" + tag + "}}";

                int i1 = html.IndexOf(start, StringComparison.Ordinal);
                if (i1 < 0) break;

                int i2 = html.IndexOf(end, i1, StringComparison.Ordinal);
                if (i2 < 0) break; // tag non fermé ? on arrête pour éviter une boucle infinie

                int blockEnd = i2 + end.Length;
                string inner = html.Substring(i1 + start.Length, i2 - (i1 + start.Length));

                bool condition = false;
                if (model.TryGetValue(tag, out var val) && val is bool b)
                    condition = b;

                string replacement = condition ? inner : "";
                html = html.Substring(0, i1) + replacement + html.Substring(blockEnd);
            }

            return html;
        }

        // ---------------------------------------------------------------
        //  Boucles {{#for:Items}} ... {{/for:Items}}
        // ---------------------------------------------------------------
        private static string ProcessLoops(string html, IDictionary<string, object?> model)
        {
            while (true)
            {
                var match = Regex.Match(html, @"\{\{#for:([A-Za-z0-9_]+)\}\}");
                if (!match.Success)
                    break;

                string listName = match.Groups[1].Value;
                string start = "{{#for:" + listName + "}}";
                string end = "{{/for:" + listName + "}}";

                int i1 = html.IndexOf(start, StringComparison.Ordinal);
                if (i1 < 0) break;

                int i2 = html.IndexOf(end, i1, StringComparison.Ordinal);
                if (i2 < 0) break;

                int blockEnd = i2 + end.Length;
                string inner = html.Substring(i1 + start.Length, i2 - (i1 + start.Length));

                var sb = new StringBuilder();

                if (model.TryGetValue(listName, out var val) &&
                    val is System.Collections.IEnumerable list &&
                    !(val is string))
                {
                    foreach (var item in list)
                    {
                        string itemHtml = inner;

                        if (item is IDictionary<string, object?> dict)
                        {
                            foreach (var kv in dict)
                            {
                                string token = "{{" + kv.Key + "}}";
                                itemHtml = itemHtml.Replace(token, kv.Value?.ToString() ?? "");
                            }
                        }

                        sb.Append(itemHtml);
                    }
                }

                html = html.Substring(0, i1) + sb + html.Substring(blockEnd);
            }

            return html;
        }
    }
}