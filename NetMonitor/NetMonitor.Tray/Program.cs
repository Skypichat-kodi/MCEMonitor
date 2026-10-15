using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NetMonitor.Tray
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_NetMonitor_Tray", out createdNew);

            if (!createdNew)
                return;

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
                    "Erreur dans NetMonitor.Tray : " + ex.Message,
                    "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}