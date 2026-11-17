using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MCEMonitor.Languages;

namespace SystemMonitor.Tray
{
    internal static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;

            // Mutex global pour empêcher plusieurs instances du Tray
            _mutex = new Mutex(true, "Global\\SystemMonitor_Tray", out createdNew);

            if (!createdNew)
            {
                return;
            }

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
                // Vérifier si le service tourne AVANT de créer le Tray
                bool serviceRunning = Process.GetProcessesByName("SystemMonitor.Service").Any();

                if (!serviceRunning)
                {
                    // Le service peut être en train de démarrer : on retente 5 fois sur ~6 secondes
                    for (int i = 0; i < 5; i++)
                    {
                        Thread.Sleep(1200);

                        if (Process.GetProcessesByName("SystemMonitor.Service").Any())
                        {
                            serviceRunning = true;
                            break;
                        }
                    }
                }

                if (!serviceRunning)
                {
                    // Ne pas créer de NotifyIcon — évite les icônes fantômes Windows 11
                    return;
                }

                // OK ? on lance le Tray
                Application.Run(new TrayApplicationContext());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    (LanguageManager.Get("Erreur dans SystemMonitor.Tray :") ?? "Erreur dans SystemMonitor.Tray :") + " " + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}