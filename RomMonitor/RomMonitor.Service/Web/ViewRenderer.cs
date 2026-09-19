using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RomMonitor.Service.Web
{
    /// <summary>
    /// Charge un fichier .html depuis /Templates et applique le TemplateEngine.
    /// Supporte les templates principaux et les partials (sous-dossier /partials).
    /// </summary>
    public static class ViewRenderer
    {
        private static readonly string TemplatesRoot =
            Path.Combine(AppContext.BaseDirectory, "Templates");

        // Cache mémoire : évite de relire le disque à chaque requête.
        private static readonly Dictionary<string, string> _cache = new();
        private static readonly object _lock = new();

        // ---------------------------------------------------------------
        //  Templates principaux (Templates/Xxx.html)
        // ---------------------------------------------------------------
        public static string Render(string templateName, IDictionary<string, object?>? model = null)
        {
            string template = LoadTemplate(templateName);
            return TemplateEngine.Render(template, model ?? new Dictionary<string, object?>());
        }

        public static string LoadTemplate(string templateName)
        {
            return LoadFile(templateName, subFolder: null);
        }

        // ---------------------------------------------------------------
        //  Partials (Templates/partials/Xxx.html)
        // ---------------------------------------------------------------
        public static string RenderPartial(string partialName, IDictionary<string, object?>? model = null)
        {
            string partial = LoadPartial(partialName);
            return TemplateEngine.Render(partial, model ?? new Dictionary<string, object?>());
        }

        public static string LoadPartial(string partialName)
        {
            return LoadFile(partialName, subFolder: "partials");
        }

        // ---------------------------------------------------------------
        //  Chargement avec cache
        // ---------------------------------------------------------------
        private static string LoadFile(string name, string? subFolder)
        {
            string cacheKey = subFolder == null ? name : $"{subFolder}/{name}";

            lock (_lock)
            {
                if (_cache.TryGetValue(cacheKey, out var cached))
                    return cached;
            }

            string path = subFolder == null
                ? Path.Combine(TemplatesRoot, name)
                : Path.Combine(TemplatesRoot, subFolder, name);

            if (!File.Exists(path))
                throw new FileNotFoundException($"Template introuvable : {path}");

            string content = File.ReadAllText(path, Encoding.UTF8);

            lock (_lock)
            {
                _cache[cacheKey] = content;
            }

            return content;
        }

        /// <summary>
        /// À appeler si tu modifies un template à chaud (dev uniquement).
        /// </summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _cache.Clear();
            }
        }
    }
}