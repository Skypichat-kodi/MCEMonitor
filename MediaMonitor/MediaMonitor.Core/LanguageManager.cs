using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json; 

namespace MediaMonitor.Core.Language
{
    public static class LanguageManager
    {
        private static Dictionary<string, string> _translations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string _currentLanguage = "en-GB";

        /// <summary>
        /// Charge un fichier de langue JSON (ex: fr-FR.json)
        /// </summary>
        public static void Load(string languageCode)
        {
            try
            {
                string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");
                string filePath = Path.Combine(basePath, $"{languageCode}.json");

                if (!File.Exists(filePath))
                {
                    // Fallback automatique
                    languageCode = "en-GB";
                    filePath = Path.Combine(basePath, "en-GB.json");
                }

                string json = File.ReadAllText(filePath);
                _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                _currentLanguage = languageCode;
            }
            catch
            {
                // En cas d'erreur ? fallback en anglais
                _translations = new Dictionary<string, string>();
                _currentLanguage = "en-GB";
            }
        }

        /// <summary>
        /// Retourne la traduction d'une clé ou null si absente
        /// </summary>
        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            if (_translations.TryGetValue(key, out string value))
                return value;

            return null; // Laisse le fallback A2 du Designer gérer
        }

        /// <summary>
        /// Retourne la langue actuellement chargée
        /// </summary>
        public static string CurrentLanguage => _currentLanguage;
    }
}

