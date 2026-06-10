using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop; 

namespace MediaMonitor.UI
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
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

            // PATCH ANTI-ÉCRAN BLANC (VNC / Remote Desktop)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            base.OnStartup(e);
        }
    }
}

