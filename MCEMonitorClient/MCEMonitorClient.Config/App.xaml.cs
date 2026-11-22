using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using MCEMonitor.Languages;

namespace MCEMonitorClient.Config
{
    /// <summary>
    /// Application WPF du client de configuration.
    ///
    /// Le mutex d'instance unique, le pipe d'activation et le choix de la langue
    /// sont repris tels quels de l'ancien Program.cs (WinForms) : seule la création
    /// de la fenêtre change.
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _mutex;

        private const string MutexName = "Global\\MCEMonitorClient_Config";
        private const string PipeName  = "MCEMonitorClient_Config_Activate";

        private const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // Une instance tourne déjà : on lui demande de revenir au premier plan.
                SignalExistingInstance();
                Shutdown();
                return;
            }

            // Indispensable avant tout appel à TaskSchedulerHelper : schtasks écrit en
            // page de codes 850, que .NET n'expose qu'une fois ce fournisseur enregistré.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            string? selectedLang = null;

            if (e.Args.Contains("-EN", StringComparer.OrdinalIgnoreCase))
                selectedLang = "en-GB";
            else if (e.Args.Contains("-FR", StringComparer.OrdinalIgnoreCase))
                selectedLang = "fr-FR";

            if (selectedLang == null)
            {
                string[] supportedLanguages = { "fr-FR", "en-GB" };
                string windowsLang = CultureInfo.InstalledUICulture.Name;

                selectedLang = supportedLanguages.Contains(windowsLang)
                    ? windowsLang
                    : "en-GB";
            }

            Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture   = new CultureInfo(selectedLang);

            LanguageManager.Load(selectedLang);

            StartActivationListener();

            base.OnStartup(e);

            var fenetre = new FenetrePrincipale();
            MainWindow  = fenetre;
            fenetre.Show();
        }

        // ============================================================
        //  Signale à l'instance existante de venir au premier plan
        // ============================================================
        private static void SignalExistingInstance()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1500);

                using var writer = new StreamWriter(client);
                writer.WriteLine("ACTIVATE");
                writer.Flush();
            }
            catch
            {
                // Si le pipe ne répond pas, on ne peut rien faire.
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
                            PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);

                        server.WaitForConnection();

                        using var reader = new StreamReader(server);
                        string? cmd = reader.ReadLine();

                        if (cmd == "ACTIVATE")
                            Current?.Dispatcher?.Invoke(new Action(ActiverFenetre));
                    }
                    catch
                    {
                        // La boucle continue même en cas d'erreur.
                    }
                }
            })
            {
                IsBackground = true,
                Name = "ConfigActivationListener"
            };

            thread.Start();
        }

        private void ActiverFenetre()
        {
            Window? fenetre = Current?.MainWindow;
            if (fenetre == null) return;

            IntPtr hwnd = new WindowInteropHelper(fenetre).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);

            SetForegroundWindow(hwnd);

            if (fenetre.WindowState == WindowState.Minimized)
                fenetre.WindowState = WindowState.Normal;

            fenetre.Activate();
        }
    }
}
