using System;
using System.IO;
using System.Windows.Forms;

namespace NetMonitor.Tray
{
    /// <summary>
    /// Retrouve un fichier de ressources (icône, son, PNG) en cherchant dans
    /// plusieurs emplacements possibles : sous-dossier, Resources, racine.
    /// Permet de garder une arbo organisée sans casser au déploiement.
    /// </summary>
    internal static class ResourceLocator
    {
        public static string? FindAppIcon(string fileName)   => Find(fileName, "Assets");
        public static string? FindIcon(string fileName)      => Find(fileName, "Resources\\Icons");
        public static string? FindSound(string fileName)     => Find(fileName, "Resources\\Sounds");

        private static string? Find(string fileName, params string[] subFolders)
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";

            // 1) Sous-dossier explicite (ex : Resources\Icons)
            foreach (var sf in subFolders)
            {
                var p = Path.Combine(exeDir, sf, fileName);
                if (File.Exists(p)) return p;
            }

            // 2) Racine de l'exe
            var root = Path.Combine(exeDir, fileName);
            if (File.Exists(root)) return root;

            // 3) Resources à la racine
            var resRoot = Path.Combine(exeDir, "Resources", fileName);
            if (File.Exists(resRoot)) return resRoot;

            return null;
        }
    }
}