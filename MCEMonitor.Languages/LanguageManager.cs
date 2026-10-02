using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MCEMonitor.Languages
{
    public static class LanguageManager
    {
        // Dictionnaire principal (exact)
        private static Dictionary<string, string> _translations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Dictionnaire normalisé (fallback tolérant)
        private static Dictionary<string, string> _normalizedTranslations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string _currentLanguage = "en-GB";

        public static void Load(string languageCode)
        {
            try
            {
                string basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Languages"
                );

                string filePath = Path.Combine(basePath, $"{languageCode}.json");

                if (!File.Exists(filePath))
                {
                    languageCode = "en-GB";
                    filePath = Path.Combine(basePath, "en-GB.json");
                }

                string json = File.ReadAllText(filePath);
                _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Construction du dictionnaire normalisé
                _normalizedTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in _translations)
                {
                    string normalized = NormalizeKey(kv.Key);
                    if (!_normalizedTranslations.ContainsKey(normalized))
                        _normalizedTranslations[normalized] = kv.Value;
                }

                _currentLanguage = languageCode;
            }
            catch
            {
                _translations = new Dictionary<string, string>();
                _normalizedTranslations = new Dictionary<string, string>();
                _currentLanguage = "en-GB";
            }
        }

        /// <summary>
        /// Retourne la traduction d'une clé.
        /// Essaie d'abord en exact, puis en normalisé (tolérant aux espaces).
        /// </summary>
        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            // 1) Lookup exact
            if (_translations.TryGetValue(key, out string exact))
                return exact;

            // 2) Lookup normalisé (trim, espaces multiples, espaces insécables)
            string normalized = NormalizeKey(key);
            if (_normalizedTranslations.TryGetValue(normalized, out string tolerant))
                return tolerant;

            return null;
        }

        /// <summary>
        /// Normalise une clé pour comparaison :
        ///  - Remplace les espaces insécables (U+00A0) par des espaces normaux
        ///  - Réduit les espaces multiples à un seul
        ///  - Trim
        /// </summary>
        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            // Remplace tous types d'espaces par un espace normal
            string result = key
                .Replace('\u00A0', ' ')   // espace insécable
                .Replace('\u202F', ' ')   // espace fine insécable
                .Replace('\u2009', ' ')   // espace fine
                .Replace('\t', ' ');

            // Réduit les espaces multiples
            while (result.Contains("  "))
                result = result.Replace("  ", " ");

            return result.Trim();
        }

        public static string CurrentLanguage => _currentLanguage;
    }
}