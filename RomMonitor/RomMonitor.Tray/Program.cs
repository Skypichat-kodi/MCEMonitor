using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace RomMonitor.Tray
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;

            // Mutex global pour empêcher plusieurs instances du Tray
            _mutex = new Mutex(true, "Global\\RomMonitor_Tray", out createdNew);

            if (!createdNew)
            {
                // Une instance existe déjà ? on quitte immédiatement
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // Vérifier si le service tourne AVANT de créer le Tray
                bool serviceRunning = Process.GetProcessesByName("RomMonitor.Service").Any();

                if (!serviceRunning)
                {
                    // Ne pas créer de NotifyIcon ? évite les icônes fantômes Windows 11
                    return;
                }

                // OK ? on lance le Tray
                Application.Run(new TrayApplicationContext());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Erreur dans RomMonitor.Tray : " + ex.Message,
                    "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}