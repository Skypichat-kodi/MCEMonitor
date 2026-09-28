using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SystemMonitor.UI
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        private const string MutexName = "Global\\MCEMonitor_SystemMonitorUI";
        private const string PipeName  = "MCEMonitor_SystemMonitorUI_Activate";

        // Win32 imports pour l'activation de fenêtre
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // Une instance tourne déjà ? lui demander de s'activer
                SignalExistingInstance();

                Shutdown();
                return;
            }

            // Première instance ? démarrer le listener IPC
            StartActivationListener();

            // ============================================================
            //  HANDLERS GLOBAUX D'EXCEPTIONS
            // ============================================================
            this.DispatcherUnhandledException += (s, ex) =>
            {
                LogCrash("DispatcherUnhandledException", ex.Exception);

                MessageBox.Show(
                    "Erreur : " + ex.Exception.Message + "\n\n" + ex.Exception.StackTrace,
                    "SystemMonitor.UI - Erreur",
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

            // Patch anti-écran blanc (VNC / RDP)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // Gestion de la langue
            string selectedLang = "fr-FR";

            int idx = Array.IndexOf(e.Args, "-lang");
            if (idx >= 0 && idx < e.Args.Length - 1)
            {
                selectedLang = e.Args[idx + 1];
            }

            Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture   = new CultureInfo(selectedLang);

            MCEMonitor.Languages.LanguageManager.Load(selectedLang);

            base.OnStartup(e);
        }

        // ============================================================
        //  Signale à l'instance existante de venir au premier plan
        // ============================================================
        private static void SignalExistingInstance()
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.Out);

                client.Connect(1500);

                using var writer = new StreamWriter(client);
                writer.WriteLine("ACTIVATE");
                writer.Flush();
            }
            catch
            {
                // Si le pipe ne répond pas, on ne peut rien faire
                // (l'instance existante ne répond pas)
            }
        }

        // ============================================================
        //  Thread qui écoute les demandes d'activation
        // ============================================================
        private void StartActivationListener()
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.None);

                        server.WaitForConnection();

                        using var reader = new StreamReader(server);
                        string? cmd = reader.ReadLine();

                        if (cmd == "ACTIVATE")
                        {
                            Current?.Dispatcher?.Invoke(new Action(ActivateMainWindow));
                        }
                    }
                    catch
                    {
                        // On continue la boucle même en cas d'erreur
                    }
                }
            })
            {
                IsBackground = true,
                Name = "ActivationListener"
            };

            thread.Start();
        }

        // ============================================================
        //  Ramène la fenêtre principale au premier plan
        //  ou affiche un message si elle est déjà active
        // ============================================================
        private void ActivateMainWindow()
        {
            var window = Current?.MainWindow;

            if (window == null)
                return;

            var helper = new WindowInteropHelper(window);
            IntPtr hwnd = helper.Handle;

            if (hwnd == IntPtr.Zero)
                return;

            // Déjà au premier plan ?
            if (GetForegroundWindow() == hwnd)
            {
                MessageBox.Show(
                    "SystemMonitor.UI est déjà au premier plan.",
                    "Instance déjà active",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Restaurer si minimisée
            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);

            // Ramener au premier plan
            SetForegroundWindow(hwnd);

            // Astuce WPF : forcer le focus
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Activate();
        }

        // ============================================================
        //  Log de crash
        // ============================================================
        private static void LogCrash(string source, Exception? ex)
        {
            try
            {
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs",
                    "SystemMonitor.UI.crash.log"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}:\n{ex}\n\n");
            }
            catch { }
        }
    }
}