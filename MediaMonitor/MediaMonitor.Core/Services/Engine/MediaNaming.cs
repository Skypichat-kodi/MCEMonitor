using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MediaMonitor.Core.Services.Engine
{
    /// <summary>
    /// Nettoyage des noms de fichiers multimédia (SMB).
    /// </summary>
    public static class MediaNaming
    {
        /// <summary>
        /// Nettoie un nom de fichier en retirant les marqueurs S01E02, les tirets
        /// en double, et applique un Title Case.
        /// </summary>
        public static string CleanEpisodeName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);

            name = Regex.Replace(name, @"\b(S?\d{1,2}[xE]\d{1,2})\b", "", RegexOptions.IgnoreCase);

            name = name.Replace("  ", " ");
            name = name.Replace(" -  - ", " - ");
            name = name.Replace(" -  ", " - ");
            name = name.Replace("  - ", " - ");
            name = name.Replace("-  -", "-");
            name = name.Replace("- -", "-");

            name = Regex.Replace(name, @"\s*-\s*", " - ");

            name = name.Trim();
            name = name.ToLower();
            name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);

            return name;
        }
    }
}