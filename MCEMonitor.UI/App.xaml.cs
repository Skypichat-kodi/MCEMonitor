using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
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

        /// <summary>
        /// Libère le mutex d'instance unique.
        ///
        /// Indispensable avant de relancer l'application depuis elle-même : sans cela,
        /// le successeur voit le mutex encore tenu, croit à une seconde instance et
        /// se ferme aussitôt.
        /// </summary>
        internal static void LibererInstanceUnique()
        {
            try
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch
            {
                // Mutex déjà libéré ou détenu par un autre thread : sans conséquence.
            }

            _mutex = null;
        }

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

            // Toute exception non gérée laisse une trace dans MCEMonitor.log avant
            // que l'application ne meure : l'interface n'a aucun autre journal.
            DispatcherUnhandledException += (_, ex) =>
                CoreLog.Error("UI", "Exception non gérée : " + ex.Exception);

            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
                CoreLog.Error("UI", "Exception fatale : " + ex.ExceptionObject);

            // ============================================================
            //  MODE TÂCHE PLANIFIÉE (arrêt ou veille programmés)
            //  Traité AVANT le mutex : l'application est peut-être déjà ouverte,
            //  et cela ne doit pas empêcher l'action de s'exécuter.
            // ============================================================
            int idxPlanifie = Array.IndexOf(e.Args, "--run-scheduled");

            if (idxPlanifie >= 0 && idxPlanifie < e.Args.Length - 1)
            {
                CoreLog.Info("Planifié", "Action demandée : " + e.Args[idxPlanifie + 1]);
                MCEMonitor.ScheduledActionRunner.Run(e.Args[idxPlanifie + 1]);
                Shutdown();
                return;
            }

            // ============================================================
            //  MODE RENOUVELLEMENT DU CERTIFICAT
            //  Également avant le mutex : MCEMonitor peut être ouvert, et le
            //  renouvellement doit se faire quand même.
            // ============================================================
            if (e.Args.Contains("--renew-cert"))
            {
                CoreLog.Info("HTTPS", "Renouvellement du certificat demandé par la tâche planifiée.");

                try
                {
                    Partage.GestionnaireCertificat.RenouvelerAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    CoreLog.Error("HTTPS", "Renouvellement : " + ex.Message);
                }

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

            // Le journal est vidé à chaque OUVERTURE : il ne contient donc que la
            // session courante, et son ticket en première ligne. La fermeture, elle,
            // n'efface rien : un fichier qui ne finit pas par « Fermeture » signale
            // une session interrompue.
            CoreLog.DemarrerSession(
                $"Démarrage - version {VersionAffichable()}, langue {selectedLang}, "
                + $"élevé : {(EstEleve() ? "oui" : "non")}, utilisateur {Environment.UserName}, "
                + $"machine {Environment.MachineName}, OS {Environment.OSVersion.VersionString}");

            base.OnStartup(e);

            // Thème choisi, puis styles : dans cet ordre, les styles référencent les
            // pinceaux du thème en StaticResource.
            GestionnaireTheme.Appliquer(this);

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

        protected override void OnExit(ExitEventArgs e)
        {
            CoreLog.Info("UI", "Fermeture");
            base.OnExit(e);
        }

        private static string VersionAffichable()
        {
            Version? v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "inconnue" : $"{v.Major}.{v.Minor}.{v.Build}";
        }

        private static bool EstEleve()
        {
            try
            {
                using var identite = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identite)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
