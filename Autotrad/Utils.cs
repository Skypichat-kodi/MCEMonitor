using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class Utils
    {
        // ---------------------------------------------------------
        //  DÉTECTION D’ENCODAGE (PRO++)
        // ---------------------------------------------------------
        public static Encoding DetectEncoding(string path)
        {
            byte[] buffer = new byte[4];

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                fs.Read(buffer, 0, 4);
            }

            // UTF-8 BOM
            if (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                return new UTF8Encoding(true);

            // UTF-16 LE
            if (buffer[0] == 0xFF && buffer[1] == 0xFE)
                return Encoding.Unicode;

            // UTF-16 BE
            if (buffer[0] == 0xFE && buffer[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            // UTF-32
            if (buffer[0] == 0x00 && buffer[1] == 0x00 &&
                buffer[2] == 0xFE && buffer[3] == 0xFF)
                return Encoding.UTF32;

            // Test UTF-8 sans BOM
            var utf8 = new UTF8Encoding(false, true);
            try
            {
                File.ReadAllText(path, utf8);
                return utf8;
            }
            catch { }

            // Dernier recours : ANSI Windows-1252 (français)
            return Encoding.GetEncoding(1252);
        }

        // ---------------------------------------------------------
        //  MODULE (nom du module depuis le chemin)
        // ---------------------------------------------------------
        public static string GetModuleFromFilename(string filePath)
        {
            try
            {
                string file = Path.GetFileNameWithoutExtension(filePath);
                return file.Replace(".", "_").Replace(" ", "_");
            }
            catch
            {
                return "Module";
            }
        }

        // ---------------------------------------------------------
        //  GÉNÉRATION DE CLÉ (accents gérés)
        // ---------------------------------------------------------
        public static string GenerateKeyFromText(string module, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return module + "_Empty";

            // Normalisation Unicode
            string cleaned = text.Normalize(NormalizationForm.FormD);

            // Retrait des accents
            var sb = new StringBuilder();
            foreach (char c in cleaned)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            cleaned = sb.ToString().Normalize(NormalizationForm.FormC);

            cleaned = cleaned
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Replace("\t", " ")
                .Trim();

            cleaned = Regex.Replace(cleaned, @"\s+", "_");
            cleaned = Regex.Replace(cleaned, @"[^A-Za-z0-9_]", "");

            if (cleaned.Length > 40)
                cleaned = cleaned.Substring(0, 40);

            return $"{module}_{cleaned}_{Guid.NewGuid().ToString("N")[..4]}";
        }

        // ---------------------------------------------------------
        //  ESCAPE XAML (complet)
        // ---------------------------------------------------------
        public static string EscapeForXamlAttribute(string text)
        {
            if (text == null) return "";

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        // ---------------------------------------------------------
        //  ESCAPE C# (complet)
        // ---------------------------------------------------------
        public static string EscapeForCSharpLiteral(string text)
        {
            if (text == null) return "";

            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        // ---------------------------------------------------------
        //  REMPLACER UNE LIGNE DANS UN FICHIER (UTF-8 BOM)
        // ---------------------------------------------------------
        public static void ReplaceLineInFile(string filePath, int lineNumber, string newLine)
        {
            var enc = DetectEncoding(filePath);
            var lines = File.ReadAllLines(filePath, enc);

            if (lineNumber - 1 < 0 || lineNumber - 1 >= lines.Length)
                return;

            lines[lineNumber - 1] = newLine;

            // On réécrit dans le même encodage
            File.WriteAllLines(filePath, lines, enc);
        }

        // ---------------------------------------------------------
        //  AJOUT / MISE À JOUR JSON (UTF-8 sans BOM)
        // ---------------------------------------------------------
public static void AddToJson(string folder, string fileName, string key, string value)
{
    string path = Path.Combine(folder, fileName);

    Dictionary<string, string> dict;

    if (File.Exists(path))
    {
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
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

    dict[key] = value;

    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    File.WriteAllText(path,
        JsonSerializer.Serialize(dict, options),
        new UTF8Encoding(false)); // UTF-8 sans BOM
}

        // ---------------------------------------------------------
        //  EXTRAIRE LA PARTIE GAUCHE D'UNE LIGNE C#
        // ---------------------------------------------------------
        public static string ExtractLeftPart(string line)
        {
            int idx = line.IndexOf("LanguageManager.Get", StringComparison.Ordinal);
            if (idx <= 0)
                return "";

            return line.Substring(0, idx);
        }

        // ---------------------------------------------------------
        //  EXTRAIRE LA CLÉ D'UNE LIGNE C# OU XAML
        // ---------------------------------------------------------
        public static string ExtractKeyFromLine(string line)
        {
            var m = Regex.Match(line, @"LanguageManager\.Get\(""([^""]+)""\)");
            if (m.Success)
                return m.Groups[1].Value;

            m = Regex.Match(line, @"Binding\s+'([^']+)'");
            if (m.Success)
                return m.Groups[1].Value;

            return "";
        }

        // ---------------------------------------------------------
        //  EXTRAIRE LE FALLBACK D'UNE LIGNE C#
        // ---------------------------------------------------------
        public static string ExtractFallbackText(string line)
        {
            int idx = line.IndexOf("??", StringComparison.Ordinal);
            if (idx < 0)
                return "";

            // Tout ce qui est après "??"
            string after = line.Substring(idx + 2);

            // On coupe avant un éventuel verbatim @"..."
            int verbIndex = after.IndexOf("@\"");
            if (verbIndex >= 0)
                after = after.Substring(0, verbIndex);

            // On coupe avant un +
            int plusIndex = after.IndexOf("+");
            if (plusIndex >= 0)
                after = after.Substring(0, plusIndex);

            // On ne garde que le PREMIER littéral "..."
            var m = Regex.Match(after, "\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success)
                return "";

            string inner = m.Groups[1].Value;

            // Dé-escape C# classique
            return inner
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\\", "\\");
        }
    }
}

