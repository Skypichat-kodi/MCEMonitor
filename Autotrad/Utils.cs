using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;

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

        public static string GenerateKeyFromText(string module, string text)
        {
            string cleaned = Regex.Replace(text, @"[^a-zA-Z0-9]+", " ").Trim();
            cleaned = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned);
            cleaned = cleaned.Replace(" ", "");

            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = "Text";

            return $"{module}.{cleaned}";
        }

        public static void ReplaceLineInFile(string path, int lineNumber, string newLine)
        {
            var lines = File.ReadAllLines(path, Encoding.GetEncoding(1252));
            lines[lineNumber - 1] = newLine;
            File.WriteAllLines(path, lines, Encoding.GetEncoding(1252));
        }

        // ?? VERSION BLINDÉE : ne coupe plus jamais les textes après \n
        public static void AddToJson(string jsonPath, string key, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

            Dictionary<string, string> dict;

            // Charger JSON existant ou repartir propre
            if (File.Exists(jsonPath) && new FileInfo(jsonPath).Length > 0)
            {
                try
                {
                    dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(jsonPath)
                    ) ?? new Dictionary<string, string>();
                }
                catch
                {
                    dict = new Dictionary<string, string>();
                }
            }
            else
            {
                dict = new Dictionary<string, string>();
            }

            // ?? Garder TOUT le texte, même après \n
            dict[key] = value;

            // Tri alphabétique
            dict = dict.OrderBy(k => k.Key).ToDictionary(k => k.Key, v => v.Value);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            File.WriteAllText(jsonPath, JsonSerializer.Serialize(dict, options));
        }

        // ?? VERSION CORRIGÉE : ne coupe plus jamais après le premier '='
        public static string ExtractLeftPart(string line)
        {
            var m = Regex.Match(line, @"^(.*?\.Text\s*=\s*)");
            if (m.Success)
                return m.Groups[1].Value;

            return line;
        }

        public static string ExtractKeyFromLine(string line)
        {
            var m = Regex.Match(line, @"LanguageManager\.Get\s*\(\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : "";
        }

        public static string ExtractFallbackText(string line)
        {
            // Capture toutes les chaînes "..."
            var matches = Regex.Matches(line, @"""([^""]*)""")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();

            if (matches.Count == 0)
                return "";

            // Si la première chaîne est la clé ? on l’ignore
            // Exemple : "Wake.RunSuccess"
            if (matches[0].Contains("."))
                matches.RemoveAt(0);

            // Recompose le texte final
            string txt = string.Join("", matches);

            // Remet les vrais \n
            txt = txt.Replace("\\n", "\n")
                     .Replace("\\r", "\r")
                     .Replace("\\t", "\t");

            return txt;
        }
    }
}

