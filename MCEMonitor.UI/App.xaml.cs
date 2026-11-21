using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Application WPF. La fenêtre est créée ici (App.xaml n'a plus de StartupUri) :
    /// il faut pouvoir traiter le mode « tâche planifiée » avant toute ouverture d'UI.
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _mutex;

        private const string PipeName = "MCEMonitor_UI_Activate";

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const string MutexName =
#if VERIF
            // Compilation de contrôle : mutex distinct pour pouvoir tourner en même
            // temps que l'exécutable livrable, qui est élevé.
            "Global\\MCEMonitor_UI_Verif";
#else
            "Global\\MCEMonitor_UI";
#endif

        protected override void OnStartup(StartupEventArgs e)
        {
            // Indispensable avant tout appel à TaskSchedulerHelper : schtasks écrit en
            // page de codes 850, et .NET n'expose cet encodage qu'une fois ce
            // fournisseur enregistré. Sans lui, la lecture et la création des tâches
            // échouent silencieusement.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // ============================================================
            //  MODE TÂCHE PLANIFIÉE (arrêt ou veille programmés)
            //  Traité AVANT le mutex : l'application est peut-être déjà ouverte,
            //  et cela ne doit pas empêcher l'action de s'exécuter.
            // ============================================================
            int idxPlanifie = Array.IndexOf(e.Args, "--run-scheduled");

            if (idxPlanifie >= 0 && idxPlanifie < e.Args.Length - 1)
            {
                MCEMonitor.ScheduledActionRunner.Run(e.Args[idxPlanifie + 1]);
                Shutdown();
                return;
            }

            // Une seule fenêtre de configuration à la fois : un second lancement
            // depuis le menu Démarrer s'arrête silencieusement.
            _mutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // Une instance tourne déjà : on lui demande de s'afficher.
                SignalExistingInstance();
                Shutdown();
                return;
            }

            StartActivationListener();

            // Langue : argument explicite, sinon celle de Windows (français ou anglais).
            string selectedLang = "en-GB";

            int idx = Array.IndexOf(e.Args, "-lang");
            if (idx >= 0 && idx < e.Args.Length - 1)
            {
                selectedLang = e.Args[idx + 1];
            }
            else
            {
                string windowsLang = CultureInfo.InstalledUICulture.Name;
                selectedLang = windowsLang.StartsWith("fr", StringComparison.OrdinalIgnoreCase)
                    ? "fr-FR"
                    : "en-GB";
            }

            Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture   = new CultureInfo(selectedLang);

            MCEMonitor.Languages.LanguageManager.Load(selectedLang);

            base.OnStartup(e);

            var fenetre = new FenetrePrincipale();
            MainWindow  = fenetre;
            fenetre.Show();
        }

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
                        string? commande = reader.ReadLine();

                        if (commande == "ACTIVATE")
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
                Name = "ActivationListener"
            };

            thread.Start();
        }

        private void ActiverFenetre()
        {
            var fenetre = Current?.MainWindow;
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
