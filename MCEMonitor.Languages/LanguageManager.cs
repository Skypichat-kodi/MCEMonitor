using System;
using System.Collections.Generic;
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
        /// Charge un fichier de langue JSON depuis ProgramData
        /// </summary>
        public static void Load(string languageCode)
        {
            try
            {
                // C:\ProgramData\MCEMonitor\Languages
                string basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Languages"
                );

                // Chemin du fichier demandé
                string filePath = Path.Combine(basePath, $"{languageCode}.json");

                // Fallback automatique si le fichier n'existe pas
                if (!File.Exists(filePath))
                {
                    languageCode = "en-GB";
                    filePath = Path.Combine(basePath, "en-GB.json");
                }

                // Lecture du JSON
                string json = File.ReadAllText(filePath);
                _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                _currentLanguage = languageCode;
            }
            catch
            {
                // Fallback en cas d'erreur
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

            return null;
        }

        /// <summary>
        /// Retourne la langue actuellement chargée
        /// </summary>
        public static string CurrentLanguage => _currentLanguage;
    }
}

