using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class Utils
    {
        public static string GetModuleFromFilename(string filePath)
        {
            string file = Path.GetFileNameWithoutExtension(filePath);

            if (file.Contains("Email", StringComparison.OrdinalIgnoreCase)) return "Email";
            if (file.Contains("Media", StringComparison.OrdinalIgnoreCase)) return "Media";
            if (file.Contains("Wake", StringComparison.OrdinalIgnoreCase)) return "Wake";
            if (file.Contains("Stop", StringComparison.OrdinalIgnoreCase)) return "Stop";
            if (file.Contains("Shutdown", StringComparison.OrdinalIgnoreCase)) return "Shutdown";
            if (file.Contains("WOL", StringComparison.OrdinalIgnoreCase)) return "WOL";
            if (file.Contains("About", StringComparison.OrdinalIgnoreCase)) return "About";

            return "App";
        }

        private static readonly (string pattern, string key)[] KnownPatterns =
        {
            ("mot de passe", "Password"),
            ("afficher", "Password.Show"),
            ("masquer", "Password.Hide"),
            ("créer tâche", "Task.Create"),
            ("supprimer tâche", "Task.Delete"),
            ("inclure ip locale", "Option.LocalIP"),
            ("inclure ip publique", "Option.PublicIP"),
            ("inclure mac", "Option.MAC"),
            ("inclure usb", "Option.USB"),
            ("inclure cause", "Option.Cause"),
            ("inclure durée", "Option.Duration"),
            ("enregistrer configuration", "Config.Save"),
            ("ouvrir", "UI.Open"),
            ("service", "Service.Label"),
            ("heure", "Hour"),
            ("minute", "Minute"),
            ("type", "Type"),
            ("à propos", "Description"),
            ("description", "Description"),
            ("test", "Run")
        };

        public static string GenerateKeyFromText(string module, string text)
        {
            string lower = text.ToLowerInvariant();

            foreach (var (pattern, key) in KnownPatterns)
            {
                if (lower.Contains(pattern))
                    return $"{module}.{key}";
            }

            return $"{module}.Title";
        }

        public static void ReplaceLineInFile(string path, int lineNumber, string newLine)
        {
            var lines = File.ReadAllLines(path, Encoding.GetEncoding(1252));
            lines[lineNumber - 1] = newLine;
            File.WriteAllLines(path, lines, Encoding.GetEncoding(1252));
        }

        public static void AddToJson(string jsonPath, string key, string value)
        {
            Dictionary<string, string> dict;

            if (File.Exists(jsonPath))
                dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(jsonPath))!;
            else
                dict = new Dictionary<string, string>();

            if (!dict.ContainsKey(key))
                dict[key] = value;

            File.WriteAllText(jsonPath,
                JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static string ExtractLeftPart(string line)
        {
            int idx = line.IndexOf('=');
            return line.Substring(0, idx + 2);
        }

        public static string ExtractKeyFromLine(string line)
        {
            var m = Regex.Match(line, @"LanguageManager\.Get\(""([^""]+)""");
            return m.Success ? m.Groups[1].Value : "";
        }

        public static string ExtractFallbackText(string line)
        {
            var m = Regex.Match(line, @"\?\?\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}

