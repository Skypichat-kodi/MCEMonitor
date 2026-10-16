using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace SystemMonitor.Tray
{
    /// <summary>
    /// Charge les ressources embarquées dans l'assembly.
    /// Avantage : plus jamais de chemin, plus jamais de fichier manquant.
    /// </summary>
    internal static class EmbeddedResourceLoader
    {
        private static Assembly Asm => typeof(EmbeddedResourceLoader).Assembly;

        /// <summary>Charge un .ico embarqué en tant qu'Icon.</summary>
        public static Icon? LoadIcon(string name)
        {
            try
            {
                using var s = Asm.GetManifestResourceStream(name);
                if (s == null) return null;
                return new Icon(s);
            }
            catch { return null; }
        }

        /// <summary>Charge un .png embarqué et le convertit en Icon.</summary>
        public static Icon? LoadPngAsIcon(string name)
        {
            try
            {
                using var s = Asm.GetManifestResourceStream(name);
                if (s == null) return null;

                using var bmp = new Bitmap(s);
                IntPtr h = bmp.GetHicon();
                using var tmp = Icon.FromHandle(h);
                return (Icon)tmp.Clone();
            }
            catch { return null; }
        }

        /// <summary>Charge un .png embarqué en tant qu'Image (pour popup).</summary>
        public static Image? LoadPngAsImage(string name)
        {
            try
            {
                using var s = Asm.GetManifestResourceStream(name);
                if (s == null) return null;

                using var img = Image.FromStream(s);
                return new Bitmap(img);
            }
            catch { return null; }
        }

        /// <summary>
        /// Charge un .wav embarqué et le copie dans un MemoryStream
        /// (nécessaire car SoundPlayer garde le Stream ouvert pendant la lecture).
        /// </summary>
        public static System.Media.SoundPlayer? LoadSound(string name)
        {
            try
            {
                using var s = Asm.GetManifestResourceStream(name);
                if (s == null) return null;

                var ms = new MemoryStream();
                s.CopyTo(ms);
                ms.Position = 0;

                return new System.Media.SoundPlayer(ms);
            }
            catch { return null; }
        }
    }
}