using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MediaMonitor.Tray
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;

            // ?? Mutex global pour empêcher plusieurs instances du Tray
            _mutex = new Mutex(true, "Global\\MediaMonitor_Tray", out createdNew);

            if (!createdNew)
            {
                // Une instance existe déjà ? on quitte proprement
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // Vérifier si le service tourne
                bool serviceRunning = Process.GetProcessesByName("MediaMonitor.Service").Any();

                if (!serviceRunning)
                {
                    // Le Tray ne doit pas être visible si le service n'est pas actif
                    return;
                }

                Application.Run(new TrayApplicationContext());
            }
            catch (Exception ex)
            {
                // ?? On ne casse jamais le Tray, ni l'UI
                MessageBox.Show(
                    "Erreur dans MediaMonitor.Tray : " + ex.Message,
                    "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}

