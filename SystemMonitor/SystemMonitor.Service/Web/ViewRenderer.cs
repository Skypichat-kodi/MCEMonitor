using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SystemMonitor.Service.Web
{
    public static class ViewRenderer
    {
        private static readonly string TemplatesRoot =
            Path.Combine(AppContext.BaseDirectory, "Templates");

        private static readonly Dictionary<string, string> _cache = new();
        private static readonly object _lock = new();

        public static string Render(string templateName, IDictionary<string, object?>? model = null)
        {
            string template = LoadTemplate(templateName);
            return TemplateEngine.Render(template, model ?? new Dictionary<string, object?>());
        }

        public static string LoadTemplate(string templateName)
        {
            return LoadFile(templateName, subFolder: null);
        }

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

        public static void ClearCache()
        {
            lock (_lock)
            {
                _cache.Clear();
            }
        }
    }
}