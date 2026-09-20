using System;
using System.IO;
using System.Text;

namespace Autotrad
{
    public static class Utils
    {
        // ---------------------------------------------------------
        //  DÉTECTION D'ENCODAGE
        // ---------------------------------------------------------
        public static Encoding DetectEncoding(string path)
        {
            try
            {
                byte[] buffer = new byte[4];

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    int read = fs.Read(buffer, 0, 4);
                    if (read < 2)
                        return new UTF8Encoding(false);
                }

                // UTF-8 avec BOM
                if (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                    return new UTF8Encoding(true);

                // UTF-16 LE
                if (buffer[0] == 0xFF && buffer[1] == 0xFE)
                    return Encoding.Unicode;

                // UTF-16 BE
                if (buffer[0] == 0xFE && buffer[1] == 0xFF)
                    return Encoding.BigEndianUnicode;

                // UTF-32 LE
                if (buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00)
                    return Encoding.UTF32;

                // Test UTF-8 sans BOM
                try
                {
                    var utf8 = new UTF8Encoding(false, true);
                    File.ReadAllText(path, utf8);
                    return utf8;
                }
                catch
                {
                    // Pas du UTF-8 valide
                }

                // Dernier recours : Windows-1252 (ANSI)
                return Encoding.GetEncoding(1252);
            }
            catch
            {
                // En cas d'erreur, on part sur UTF-8 par défaut
                return new UTF8Encoding(false);
            }
        }
    }
}