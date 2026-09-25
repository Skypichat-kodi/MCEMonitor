using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MCEMonitor.Languages;

namespace SystemMonitor.Service.Web
{
    /// <summary>
    /// Moteur de template maison.
    /// Syntaxe :
    ///   {{var}}                     ? remplacement simple
    ///   {{tr:clé}}                  ? traduction via LanguageManager
    ///   {{#IfTag}}...{{/IfTag}}     ? bloc conditionnel (bool)
    ///   {{#for:Items}}...{{/for:Items}} ? boucle sur une liste de dictionnaires
    ///   {{Item.Prop}}               ? propriété d'un item (dans une boucle)
    /// </summary>
    public static class TemplateEngine
    {
        private static readonly Regex TrRegex =
            new(@"\{\{tr:([^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex VarRegex =
            new(@"\{\{([A-Za-z0-9_\.]+)\}\}", RegexOptions.Compiled);

        public static string Render(string template, IDictionary<string, object?> model)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            string html = template;

            html = ProcessLoops(html, model);
            html = ProcessConditionals(html, model);
            html = TranslatePass(html);

            html = VarRegex.Replace(html, m =>
            {
                string key = m.Groups[1].Value;
                if (key.StartsWith("#") || key.StartsWith("/"))
                    return m.Value;

                if (model.TryGetValue(key, out var val))
                    return val?.ToString() ?? "";

                return "";
            });

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

        private static string ProcessConditionals(string html, IDictionary<string, object?> model)
        {
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
                if (i2 < 0) break;

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