namespace Autotrad
{
    public class ScanResult
    {
        public int LineNumber { get; set; }
        public string FullLine { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Selected { get; set; } = false;

        // Aperçu avec surbrillance non destructive
        public string Preview
        {
            get
            {
                if (string.IsNullOrEmpty(Text) || string.IsNullOrEmpty(FullLine))
                    return FullLine;

                return FullLine.Replace(Text, $"***{Text}***");
            }
        }

        // Déjà traduit ?
        public bool IsTranslated =>
            FullLine.Contains("LanguageManager.Get(");
    }
}

