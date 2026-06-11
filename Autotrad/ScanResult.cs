namespace Autotrad
{
    public class ScanResult
    {
        public int LineNumber { get; set; }
        public string FullLine { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Selected { get; set; }

        // ?? Doit être modifiable pour que Scanner.cs puisse l'écrire
        public bool IsTranslated { get; set; }

        // Aperçu affiché dans la grille
        public string Preview => $"{LineNumber}: {FullLine}";
    }
}

