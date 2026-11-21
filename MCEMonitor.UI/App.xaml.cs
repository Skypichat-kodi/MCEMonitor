using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Application WPF. La fenêtre est créée ici (App.xaml n'a plus de StartupUri) :
    /// il faut pouvoir traiter le mode « tâche planifiée » avant toute ouverture d'UI.
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _mutex;

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
                Shutdown();
                return;
            }

            // Langue transmise par l'appelant, comme dans les quatre UI des modules :
            // -lang fr-FR (ou en-GB, de-DE, es-ES).
            string selectedLang = "fr-FR";

            int idx = Array.IndexOf(e.Args, "-lang");
            if (idx >= 0 && idx < e.Args.Length - 1)
                selectedLang = e.Args[idx + 1];

            Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture   = new CultureInfo(selectedLang);

            MCEMonitor.Languages.LanguageManager.Load(selectedLang);

            base.OnStartup(e);

            var fenetre = new FenetrePrincipale();
            MainWindow  = fenetre;
            fenetre.Show();
        }
    }
}
