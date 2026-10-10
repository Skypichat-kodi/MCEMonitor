using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Globalization;
using System.Threading;
using MCEMonitor.Services;
using MCEMonitor.Utils;
using Krypton.Toolkit;

namespace MCEMonitor
{
    internal static class Program
    {
        private static Mutex _mutex;

        private const string MutexName = "Global\\MCEMonitor_MainUI";
        private const string PipeName  = "MCEMonitor_Activate";

        public static KryptonManager SharedManager { get; private set; }

        private static MainForm _mainForm;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                // Support des encodages legacy (CP850)
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                // ============================================================
                //  MODE TÂCHE PLANIFIÉE (arrêt / veille programmé)
                //  ? AVANT le mutex, sinon on signale l'instance existante
                //    et rien ne se passe.
                // ============================================================
                if (args.Length >= 2 &&
                    args[0].Equals("--run-scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    AppData.Initialize();
                    ScheduledActionRunner.Run(args[1]);
                    return;   // on sort : pas d'UI, pas d'IPC, pas de mutex
                }

                // ============================================================
                //  Mutex anti-multi-instance
                // ============================================================
                bool createdNew;
                _mutex = new Mutex(true, MutexName, out createdNew);

                if (!createdNew)
                {
                    SignalExistingInstance();
                    return;
                }

                // Première instance ? démarrer le listener IPC
                StartActivationListener();

                // ============================================================
                //  GESTION DE LA LANGUE (argument > auto-détection Windows)
                // ============================================================
                string selectedLang = null;

                if (args.Contains("-EN", StringComparer.OrdinalIgnoreCase))
                    selectedLang = "en-GB";
                else if (args.Contains("-FR", StringComparer.OrdinalIgnoreCase))
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

                // ============================================================
                //  INITIALISATION APPLICATION
                // ============================================================
                AppData.Initialize();

                // ============================================================
                //  INSTALLATION AUTOMATIQUE TRAY
                // ============================================================
                if (!ServiceInstaller.TrayTaskExists())
                    ServiceInstaller.CreateTrayTask();

                if (!ServiceInstaller.RomTrayTaskExists())
                    ServiceInstaller.CreateRomTrayTask();

                if (!ServiceInstaller.SystemTrayTaskExists())
                    ServiceInstaller.CreateSystemTrayTask();

                // ============================================================
                //  SERVICES LOCAUX
                // ============================================================
                var media = new MediaMonitorService();
                var wake  = new WakeMonitorService();

                if (args.Contains("--media-silent"))
                {
                    media.RunSilent();
                    return;
                }

                if (args.Contains("--wake"))
                {
                    wake.Run();
                    return;
                }

                // ============================================================
                //  INITIALISATION APPLICATION (une seule fois !)
                // ============================================================
                ApplicationConfiguration.Initialize();

                SharedManager = new KryptonManager();
                SharedManager.GlobalPaletteMode = ThemeSelectorForm.LoadSavedTheme();
                SharedManager.GlobalApplyToolstrips = true;

                _mainForm = new MainForm(media, wake);
                Application.Run(_mainForm);
            }
            catch (Exception ex)
            {
                try
                {
                    string logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MCEMonitor",
                        "startup_error.log"
                    );

                    File.WriteAllText(logPath, ex.ToString());
                }
                catch { }

                MessageBox.Show(
                    "Une erreur est survenue au démarrage.\n" +
                    "Le détail a été enregistré dans startup_error.log",
                    "Erreur au démarrage",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
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
        private static void StartActivationListener()
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
                            OnActivationRequested();
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
        private static void OnActivationRequested()
        {
            for (int i = 0; i < 50 && _mainForm == null; i++)
                Thread.Sleep(100);

            if (_mainForm == null || _mainForm.IsDisposed)
                return;

            if (_mainForm.InvokeRequired)
            {
                _mainForm.Invoke(new Action(OnActivationRequested));
                return;
            }

            IntPtr hwnd = _mainForm.Handle;

            if (GetForegroundWindow() == hwnd &&
                _mainForm.WindowState != FormWindowState.Minimized)
            {
                MessageBox.Show(
                    "MCEMonitor est déjà au premier plan.",
                    "Instance déjà active",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);

            SetForegroundWindow(hwnd);
            _mainForm.Activate();
        }
    }
}