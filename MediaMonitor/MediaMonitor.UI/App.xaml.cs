using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MediaMonitor.UI
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        private const string MutexName = "Global\\MCEMonitor_MediaMonitorUI";
        private const string PipeName  = "MCEMonitor_MediaMonitorUI_Activate";

        // ============================================================
        //  Win32 imports pour l'activation de fenêtre
        // ============================================================
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
            // ============================================================
            //  Mutex anti-multi-instance
            // ============================================================
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // Une instance tourne déjà → lui demander de s'activer
                SignalExistingInstance();
                Shutdown();
                return;
            }

            // Première instance → démarrer le listener IPC
            StartActivationListener();

            // ============================================================
            //  Patch anti-écran blanc (VNC / RDP)
            // ============================================================
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // ============================================================
            //  GESTION DE LA LANGUE TRANSMISE PAR MCEMonitor
            // ============================================================
            string selectedLang = "fr-FR"; // fallback

            // Recherche de l'argument -lang
            int idx = Array.IndexOf(e.Args, "-lang");
            if (idx >= 0 && idx < e.Args.Length - 1)
            {
                selectedLang = e.Args[idx + 1];
            }

            // Application de la culture
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture   = new CultureInfo(selectedLang);

            // Chargement du JSON de langue
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

            // Déjà au premier plan ET pas minimisée ?
            if (GetForegroundWindow() == hwnd &&
                window.WindowState != WindowState.Minimized)
            {
                MessageBox.Show(
                    "MediaMonitor.UI est déjà au premier plan.",
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
    }
}