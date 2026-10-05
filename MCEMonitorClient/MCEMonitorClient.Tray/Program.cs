using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MCEMonitorClient.Tray
{
    internal static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitorClient_Tray", out createdNew);

            if (!createdNew)
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // Vérifie que le Service tourne
                bool serviceRunning = Process.GetProcessesByName("MCEMonitorClient.Service").Any();

                if (!serviceRunning)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        Thread.Sleep(1200);
                        if (Process.GetProcessesByName("MCEMonitorClient.Service").Any())
                        {
                            serviceRunning = true;
                            break;
                        }
                    }
                }

                if (!serviceRunning)
                {
                    // Pas de service ? pas de Tray
                    return;
                }

                Application.Run(new TrayApplicationContext());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Erreur dans MCEMonitorClient.Tray : " + ex.Message,
                    "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}