using System;
using System.Threading;
using System.Windows;

namespace MediaMonitor.UI
{
    public partial class App : Application
    {
        private static Mutex _mutex;   // ← AJOUT

        protected override void OnStartup(StartupEventArgs e)
        {
            // ← AJOUT : Mutex anti-multi-instance
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

            base.OnStartup(e);
        }
    }
}

