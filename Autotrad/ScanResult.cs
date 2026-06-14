using System;

namespace Autotrad
{
    /// <summary>
    /// Représente une entrée trouvée par le Scanner PRO.
    /// Chaque ligne analysée (C# ou XAML) devient un ScanResult.
    /// </summary>
    public class ScanResult
    {
        /// <summary>
        /// Identifiant unique pour éviter les doublons dans le DataGridView.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Sous-index pour distinguer plusieurs entrées provenant de la même ligne.
        /// </summary>
        public int SubIndex { get; set; }

        /// <summary>
        /// Chemin complet du fichier où la ligne a été trouvée.
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// Nom du fichier (extrait automatiquement).
        /// </summary>
        public string FileName
        {
            get
            {
                try { return System.IO.Path.GetFileName(FilePath); }
                catch { return FilePath; }
            }
        }

        /// <summary>
        /// Numéro de ligne dans le fichier.
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// Ligne brute telle qu'elle apparaît dans le fichier.
        /// Sert pour l'export et les remplacements.
        /// </summary>
        public string FullLine { get; set; } = "";

        /// <summary>
        /// Texte détecté (fallback ou texte brut).
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Texte affiché dans la colonne "Aperçu".
        /// Peut être différent de Text si on veut un rendu propre.
        /// </summary>
        public string Preview { get; set; } = "";

        /// <summary>
        /// L'utilisateur a coché la ligne pour export ?
        /// </summary>
        public bool Selected { get; set; }

        /// <summary>
        /// La ligne utilise déjà une clé (Binding Lang ou LM.Get) ?
        /// </summary>
        public bool IsTranslated { get; set; }

        /// <summary>
        /// La clé n'existe pas dans le JSON ?
        /// </summary>
        public bool IsMissingKey { get; set; }

        /// <summary>
        /// La clé existe mais le fallback ne correspond pas ?
        /// </summary>
        public bool IsMismatch { get; set; }
    }
}

