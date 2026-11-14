using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace NetMonitor.Tray
{
    internal static class EmbeddedResourceLoader
    {
        private static Assembly Asm => typeof(EmbeddedResourceLoader).Assembly;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>Charge un .ico embarqué en tant qu'Icon.</summary>
        public static Icon? LoadIcon(string name)
        {
            try
            {
                using var s = Asm.GetManifestResourceStream(name);
                if (s == null) return null;
                return new Icon(s);
            }
            catch (Exception ex)
            {
                Logs.CoreLog.Warn($"LoadIcon('{name}') : {ex.Message}");
                return null;
            }
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

                try
                {
                    using var tmp = Icon.FromHandle(h);
                    return (Icon)tmp.Clone();
                }
                finally
                {
                    // Icon.FromHandle ne prend pas possession du handle :
                    // il faut le libérer explicitement pour éviter une fuite GDI
                    DestroyIcon(h);
                }
            }
            catch (Exception ex)
            {
                Logs.CoreLog.Warn($"LoadPngAsIcon('{name}') : {ex.Message}");
                return null;
            }
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
            catch (Exception ex)
            {
                Logs.CoreLog.Warn($"LoadPngAsImage('{name}') : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Charge un .wav embarqué et le copie dans un MemoryStream
        /// (nécessaire car SoundPlayer garde le Stream ouvert pendant la lecture).
        ///
        /// ? Le MemoryStream n'est volontairement PAS disposé :
        ///   - SoundPlayer n'implémente pas IDisposable
        ///   - Il garde une référence au stream pendant toute la durée de vie du process
        ///   - Disposer le stream prématurément provoquerait ObjectDisposedException
        ///     lors du prochain PlaySync()
        ///
        /// Impact : ~100 Ko par son × 2 sons = ~200 Ko résidents jusqu'à l'arrêt du Tray.
        /// Négligeable pour un process de tray. Si un jour le Tray est refactoré
        /// en process longue durée, charger les WAV dans des fichiers temp avec
        /// SoundPlayer(string path) et cleanup à l'arrêt.
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
            catch (Exception ex)
            {
                Logs.CoreLog.Warn($"LoadSound('{name}') : {ex.Message}");
                return null;
            }
        }
    }
}