using System;
using System.IO;

namespace Autotrad
{
    /// <summary>
    /// Représente une entrée trouvée par le scanner.
    /// Chaque ligne analysée (C#, XAML ou HTML) devient un ScanResult.
    /// </summary>
    public class ScanResult
    {
        /// <summary>
        /// Chemin complet du fichier scanné.
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// Nom du fichier (sans le chemin). Utilisé en mode dossier.
        /// </summary>
        public string FileName
        {
            get
            {
                if (string.IsNullOrEmpty(FilePath))
                    return "";

                try
                {
                    return Path.GetFileName(FilePath);
                }
                catch
                {
                    return FilePath;
                }
            }
        }

        /// <summary>
        /// Numéro de ligne où la clé a été trouvée.
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// Ligne complète du fichier source (pour l'aperçu).
        /// </summary>
        public string FullLine { get; set; } = "";

        /// <summary>
        /// Clé de traduction (ex: "Fichier", "Aucun rapport envoyé").
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Texte source détecté (souvent identique à Key).
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Aperçu affiché dans la colonne "Aperçu" du tableau.
        /// </summary>
        public string Preview { get; set; } = "";

        /// <summary>
        /// Valeur JSON associée à la clé (affichée dans la colonne "Traduction JSON").
        /// Vide si la clé n'existe pas encore dans le JSON.
        /// </summary>
        public string JsonValue { get; set; } = "";

        /// <summary>
        /// True si la clé existe déjà dans le JSON (traduite).
        /// </summary>
        public bool IsTranslated { get; set; }

        /// <summary>
        /// True si la clé est absente du JSON (à traduire).
        /// </summary>
        public bool IsMissingKey { get; set; }
    }
}