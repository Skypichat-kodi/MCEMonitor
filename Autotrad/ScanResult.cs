using System;

namespace Autotrad
{
    /// <summary>
    /// Représente une entrée trouvée par le scanner.
    /// Chaque ligne analysée (C# ou XAML) devient un ScanResult.
    /// </summary>
    public class ScanResult
    {
        public string FilePath { get; set; } = "";

        public string FileName
        {
            get
            {
                try { return System.IO.Path.GetFileName(FilePath); }
                catch { return FilePath; }
            }
        }

        public int LineNumber { get; set; }
        public string FullLine { get; set; } = "";
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";
        public string Preview { get; set; } = "";

        /// <summary>
        /// Valeur JSON associée à la clé (affichée dans la 3e colonne)
        /// </summary>
        public string JsonValue { get; set; } = "";

        public bool IsTranslated { get; set; }
        public bool IsMissingKey { get; set; }
    }
}

