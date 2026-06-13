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

        public static void AddToJson(string jsonPath, string key, string value)
        {
            Dictionary<string, string> dict;

            if (File.Exists(jsonPath))
                dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(jsonPath))!;
            else
                dict = new Dictionary<string, string>();

            dict[key] = value;

            dict = dict.OrderBy(k => k.Key).ToDictionary(k => k.Key, v => v.Value);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            File.WriteAllText(jsonPath, JsonSerializer.Serialize(dict, options));
        }

        public static string ExtractLeftPart(string line)
        {
            int idx = line.IndexOf('=');
            return idx < 0 ? line : line.Substring(0, idx + 2);
        }

        public static string ExtractKeyFromLine(string line)
        {
            var m = Regex.Match(line, @"LanguageManager\.Get\s*\(\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : "";
        }

        public static string ExtractFallbackText(string line)
        {
            var m = Regex.Match(line, @"\?\?\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}

