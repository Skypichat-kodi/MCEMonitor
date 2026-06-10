using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Globalization;
using System.Threading;
using MCEMonitor.Services;
using MCEMonitor.Utils;

namespace MCEMonitor
{
    internal static class Program
    {
        private static Mutex _mutex;   // ? AJOUT

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                // ? AJOUT : support des encodages legacy (CP850)
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                // ? AJOUT : Mutex anti-multi-instance
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

                // 1) Argument développeur
                if (args.Contains("-EN", StringComparer.OrdinalIgnoreCase))
                    selectedLang = "en-GB";
                else if (args.Contains("-FR", StringComparer.OrdinalIgnoreCase))
                    selectedLang = "fr-FR";

                // 2) Sinon : auto-détection Windows
                if (selectedLang == null)
                {
                    string[] supportedLanguages = { "fr-FR", "en-GB" };
                    string windowsLang = CultureInfo.InstalledUICulture.Name;

                    selectedLang = supportedLanguages.Contains(windowsLang)
                        ? windowsLang
                        : "en-GB";
                }

                // 3) Application de la langue
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

                // Installer la tâche ONLOGON du Tray si absente
                if (!ServiceInstaller.TrayTaskExists())
                {
                    ServiceInstaller.CreateTrayTask();
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

                ApplicationConfiguration.Initialize();
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

