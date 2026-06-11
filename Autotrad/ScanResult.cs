namespace Autotrad
{
    public class ScanResult
    {
        public int LineNumber { get; set; }
        public string FullLine { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Selected { get; set; }

        // Ligne déjà traduite (clé existante ou LanguageManager.Get)
        public bool IsTranslated { get; set; }

        // ?? Divergence JSON ? CS
        public bool IsMismatch { get; set; }

        public string Preview => $"{LineNumber}: {FullLine}";
    }
}

