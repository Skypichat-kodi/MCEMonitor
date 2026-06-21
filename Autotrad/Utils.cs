using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Autotrad
{
    public static class Utils
    {
        // ---------------------------------------------------------
        //  DÉTECTION D’ENCODAGE
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

            // Dernier recours : ANSI Windows-1252
            return Encoding.GetEncoding(1252);
        }

        // ---------------------------------------------------------
        //  EXTRAIRE LA CLÉ D'UNE LIGNE C# OU XAML
        // ---------------------------------------------------------
        public static string ExtractKeyFromLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return "";

            // C# : LanguageManager.Get("clé")
            var m = Regex.Match(line, @"LanguageManager\.Get\(""([^""]+)""\)");
            if (m.Success)
                return m.Groups[1].Value;

            // XAML : Binding 'clé'
            m = Regex.Match(line, @"Binding\s+'([^']+)'");
            if (m.Success)
                return m.Groups[1].Value;

            // XAML : {loc:Tr 'clé'} ou {loc:Tr "clé"}
            m = Regex.Match(line, @"\{loc:Tr\s*['""]([^'""]+)['""]\}");
            if (m.Success)
                return m.Groups[1].Value;

            return "";
        }
    }
}

