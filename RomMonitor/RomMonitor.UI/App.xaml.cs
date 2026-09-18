using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RomMonitor.UI
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Mutex anti-multi-instance
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_RomMonitorUI", out createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "RomMonitor.UI est déjà en cours d'exécution.",
                    "Instance déjà ouverte",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                Shutdown();
                return;
            }

            // ============================================================
            //  HANDLERS GLOBAUX D'EXCEPTIONS
            // ============================================================

            this.DispatcherUnhandledException += (s, ex) =>
            {
                LogCrash("DispatcherUnhandledException", ex.Exception);

                MessageBox.Show(
                    "Erreur : " + ex.Exception.Message + "\n\n" + ex.Exception.StackTrace,
                    "RomMonitor.UI - Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                ex.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                LogCrash("UnhandledException", ex.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                LogCrash("UnobservedTaskException", ex.Exception);
                ex.SetObserved();
            };

            // ============================================================
            //  PATCH ANTI-ÉCRAN BLANC (VNC / RDP)
            // ============================================================
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // ============================================================
            //  GESTION DE LA LANGUE
            // ============================================================
            string selectedLang = "fr-FR";

            int idx = Array.IndexOf(e.Args, "-lang");
            if (idx >= 0 && idx < e.Args.Length - 1)
            {
                selectedLang = e.Args[idx + 1];
            }

            Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture = new CultureInfo(selectedLang);

            MediaMonitor.Core.Language.LanguageManager.Load(selectedLang);

            base.OnStartup(e);
        }

        // ------------------------------------------------------------
        //  Log de crash
        // ------------------------------------------------------------
        private static void LogCrash(string source, Exception? ex)
        {
            try
            {
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs",
                    "RomMonitor.UI.crash.log"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}:\n{ex}\n\n");
            }
            catch { }
        }
    }
}