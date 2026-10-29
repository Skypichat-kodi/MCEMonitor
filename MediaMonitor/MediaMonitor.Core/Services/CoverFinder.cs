using System;
using System.IO;

namespace MediaMonitor.Core.Services
{
    /// <summary>
    /// Recherche une image de couverture externe pour un fichier média,
    /// en suivant les conventions standards (folder.jpg, cover.jpg, etc.).
    ///
    /// Utilisé en fallback quand le fichier média n'a pas d'image embarquée
    /// dans ses tags (ID3v2 APIC, FLAC PICTURE, etc.).
    /// </summary>
    public static class CoverFinder
    {
        // Extensions supportées par BitmapImage (WPF).
        // WebP n'est PAS inclus : WPF ne le décode pas nativement.
        private static readonly string[] Extensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif"
        };

        // Noms génériques à essayer, par ordre de priorité.
        // On teste chaque nom avec toutes les extensions.
        private static readonly string[] GenericNames =
        {
            "folder", "Folder",
            "cover",  "Cover",
            "front",  "Front",
            "albumart", "AlbumArt",
            "album", "Album"
        };

        /// <summary>
        /// Cherche une cover pour le fichier média spécifié, selon
        /// l'ordre de priorité :
        ///   1. folder.jpg / cover.jpg / front.jpg / etc. dans le même dossier
        ///   2. {nom_fichier}.jpg dans le même dossier
        ///   3. {nom_dossier}.jpg dans le même dossier
        ///   4. {nom_dossier}.jpg dans le dossier parent (Artiste/Album/track.mp3 → Artiste/folder.jpg)
        ///
        /// Retourne les bytes de l'image, ou null si rien n'est trouvé.
        /// </summary>
        public static byte[]? FindExternalCover(string mediaPath)
        {
            if (string.IsNullOrWhiteSpace(mediaPath))
                return null;

            try
            {
                string? dir = Path.GetDirectoryName(mediaPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return null;

                // 1. Noms génériques (folder.jpg, cover.jpg…)
                byte[]? bytes = TryGenericNamesInDir(dir);
                if (bytes != null) return bytes;

                // 2. {nom_fichier}.jpg (ex : Film.mkv → Film.jpg)
                string fileNameNoExt = Path.GetFileNameWithoutExtension(mediaPath);
                if (!string.IsNullOrEmpty(fileNameNoExt))
                {
                    bytes = TryExactNameInDir(dir, fileNameNoExt);
                    if (bytes != null) return bytes;
                }

                // 3. {nom_dossier}.jpg (ex : dossier "Inception (2010)" → "Inception (2010).jpg")
                string folderName = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(folderName))
                {
                    bytes = TryExactNameInDir(dir, folderName);
                    if (bytes != null) return bytes;
                }

                // 4. Image du dossier parent (Artiste/Album/track.mp3 → Artiste/folder.jpg)
                string? parent = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    bytes = TryGenericNamesInDir(parent);
                    if (bytes != null) return bytes;

                    // Bonus : on essaie aussi {nom_dossier_parent}.jpg
                    string parentName = Path.GetFileName(parent);
                    if (!string.IsNullOrEmpty(parentName))
                    {
                        bytes = TryExactNameInDir(parent, parentName);
                        if (bytes != null) return bytes;
                    }
                }
            }
            catch
            {
                // Ignore les erreurs (permissions, SMB lent, etc.)
            }

            return null;
        }

        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------
        private static byte[]? TryGenericNamesInDir(string dir)
        {
            foreach (var name in GenericNames)
            {
                foreach (var ext in Extensions)
                {
                    string candidate = Path.Combine(dir, name + ext);
                    if (File.Exists(candidate))
                        return File.ReadAllBytes(candidate);
                }
            }
            return null;
        }

        private static byte[]? TryExactNameInDir(string dir, string baseName)
        {
            foreach (var ext in Extensions)
            {
                string candidate = Path.Combine(dir, baseName + ext);
                if (File.Exists(candidate))
                    return File.ReadAllBytes(candidate);
            }
            return null;
        }
    }
}