using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MCEMonitor.Languages;

namespace NetMonitor.Tray
{
    internal static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_NetMonitor_Tray", out createdNew);

            if (!createdNew)
                return;

            // Langue : fichier partage avec les services (C:\ProgramData\MCEMonitor\language.config)
            try
            {
                string cfgLang = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor", "language.config");
                string lang = File.Exists(cfgLang) ? File.ReadAllText(cfgLang).Trim() : "fr-FR";
                if (string.IsNullOrWhiteSpace(lang)) lang = "fr-FR";
                LanguageManager.Load(lang);
            }
            catch { /* repli : les textes restent en francais */ }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                bool serviceRunning = Process.GetProcessesByName("NetMonitor.Service").Any();

                if (!serviceRunning)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        Thread.Sleep(1200);
                        if (Process.GetProcessesByName("NetMonitor.Service").Any())
                        {
                            serviceRunning = true;
                            break;
                        }
                    }
                }

                if (!serviceRunning)
                    return;

                Application.Run(new TrayApplicationContext());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    (LanguageManager.Get("Erreur dans NetMonitor.Tray :") ?? "Erreur dans NetMonitor.Tray :") + " " + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}