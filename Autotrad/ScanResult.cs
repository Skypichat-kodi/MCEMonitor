namespace Autotrad
{
    public class ScanResult
    {
        public int LineNumber { get; set; }
        public string FullLine { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Selected { get; set; }
        public bool IsTranslated { get; set; }
        public bool IsMismatch { get; set; }

        // Chemin complet du fichier (interne)
        public string FilePath { get; set; } = "";

        // Nom du fichier (affiché uniquement en mode dossier)
        public string FileName => System.IO.Path.GetFileName(FilePath);

        // Aperçu simple
        public string Preview => Text;
    }
}


