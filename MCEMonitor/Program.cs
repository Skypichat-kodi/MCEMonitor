using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Globalization;
using System.Threading;
using MCEMonitor.Services;
using MCEMonitor.Utils;
using Krypton.Toolkit;

namespace MCEMonitor
{
    internal static class Program
    {
        private static Mutex _mutex;

        // ? Référence partagée vers le KryptonManager (pour le sélecteur de thème)
        public static KryptonManager SharedManager { get; private set; }

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                // Support des encodages legacy (CP850)
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                // Mutex anti-multi-instance
                bool createdNew;
                _mutex = new Mutex(true, "Global\\MCEMonitor_MainUI", out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show(
                        "MCEMonitor est déjà en cours d'exécution.",
                        "Instance déjà ouverte",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

                // ============================================================
                // GESTION DE LA LANGUE (argument > auto-détection Windows)
                // ============================================================

                string selectedLang = null;

                if (args.Contains("-EN", StringComparer.OrdinalIgnoreCase))
                    selectedLang = "en-GB";
                else if (args.Contains("-FR", StringComparer.OrdinalIgnoreCase))
                    selectedLang = "fr-FR";

                if (selectedLang == null)
                {
                    string[] supportedLanguages = { "fr-FR", "en-GB" };
                    string windowsLang = CultureInfo.InstalledUICulture.Name;

                    selectedLang = supportedLanguages.Contains(windowsLang)
                        ? windowsLang
                        : "en-GB";
                }

                Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
                Thread.CurrentThread.CurrentCulture = new CultureInfo(selectedLang);

                LanguageManager.Load(selectedLang);

                // ============================================================
                // INITIALISATION APPLICATION
                // ============================================================

                AppData.Initialize();

                // ============================================================
                // INSTALLATION AUTOMATIQUE TRAY
                // ============================================================

                if (!ServiceInstaller.TrayTaskExists())
                {
                    ServiceInstaller.CreateTrayTask();
                }

                if (!ServiceInstaller.RomTrayTaskExists())
                {
                    ServiceInstaller.CreateRomTrayTask();
                }

                // ============================================================
                // SERVICES LOCAUX
                // ============================================================

                var media = new MediaMonitorService();
                var wake = new WakeMonitorService();

                if (args.Contains("--media-silent"))
                {
                    media.RunSilent();
                    return;
                }

                if (args.Contains("--wake"))
                {
                    wake.Run();
                    return;
                }

                // ============================================================
                // INITIALISATION APPLICATION (une seule fois !)
                // ============================================================

                ApplicationConfiguration.Initialize();

                // ? Initialisation du thème Krypton
                SharedManager = new KryptonManager();
                SharedManager.GlobalPaletteMode = ThemeSelectorForm.LoadSavedTheme();
                SharedManager.GlobalApplyToolstrips = true;

                // ? Lancement de la fenêtre principale (une seule fois !)
                Application.Run(new MainForm(media, wake));
            }
            catch (Exception ex)
            {
                try
                {
                    string logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MCEMonitor",
                        "startup_error.log"
                    );

                    File.WriteAllText(logPath, ex.ToString());
                }
                catch
                {
                }

                MessageBox.Show(
                    "Une erreur est survenue au démarrage.\n" +
                    "Le détail a été enregistré dans startup_error.log",
                    "Erreur au démarrage",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}