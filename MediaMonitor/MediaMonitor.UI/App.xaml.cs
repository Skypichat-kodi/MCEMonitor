using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MediaMonitor.UI
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Mutex anti-multi-instance
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_UI", out createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "MediaMonitor.UI est déjà en cours d'exécution.",
                    "Instance déjà ouverte",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                Shutdown();
                return;
            }

            // 🔥 Patch anti-écran blanc (VNC / RDP)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // ============================================================
            // 🔥 GESTION DE LA LANGUE TRANSMISE PAR MCEMonitor
            // ============================================================

            string selectedLang = "fr-FR"; // fallback

            // Recherche de l’argument -lang
            int idx = Array.IndexOf(e.Args, "-lang");
            if (idx >= 0 && idx < e.Args.Length - 1)
            {
                selectedLang = e.Args[idx + 1];
            }

            // Application de la culture
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture = new CultureInfo(selectedLang);

            // Chargement du JSON de langue
            MediaMonitor.Core.Language.LanguageManager.Load(selectedLang);

            base.OnStartup(e);
        }
    }
}

