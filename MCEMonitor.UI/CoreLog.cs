using System;
using System.IO;
using System.Text;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Journal de l'interface MCEMonitor, écrit avec ceux des autres modules dans
    /// %ProgramData%\MCEMonitor\Logs\MCEMonitor.log.
    ///
    /// À chaque OUVERTURE de l'interface, le fichier précédent est archivé en
    /// MCEMonitor.1.log, et un nouveau commence : la session courante se lit seule,
    /// et la précédente — où figurent les actions des tâches planifiées — reste
    /// consultable. Un fichier qui ne se termine pas par « Fermeture » signale que
    /// la session a été interrompue.
    ///
    /// La tâche planifiée (--run-scheduled) écrit dans le fichier courant sans rien
    /// archiver : ses actions restent visibles après coup.
    /// </summary>
    public static class CoreLog
    {
        private static readonly object Verrou = new object();

        /// <summary>Taille au-delà de laquelle le fichier est renommé en .1.log.</summary>
        private const long TailleMaxOctets = 1_000_000;

        private static readonly string Dossier = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs");

        private static readonly string Fichier = Path.Combine(Dossier, "MCEMonitor.log");

        public static string CheminFichier => Fichier;

        /// <summary>
        /// Vide le journal puis écrit le ticket d'ouverture. À n'appeler qu'en mode
        /// interface : la tâche planifiée ne doit pas effacer la session en cours.
        /// </summary>
        public static void DemarrerSession(string ticket)
        {
            lock (Verrou)
            {
                try
                {
                    Directory.CreateDirectory(Dossier);

                    // Archiver plutôt que vider : les tâches planifiées — passerelle,
                    // renouvellement, mise à jour DNS — écrivent dans ce fichier avant
                    // l ouverture de l interface. Les effacer rendait leur trace
                    // introuvable au moment précis où on la cherche.
                    if (File.Exists(Fichier) && new FileInfo(Fichier).Length > 0)
                    {
                        string ancien = Path.Combine(Dossier, "MCEMonitor.1.log");

                        if (File.Exists(ancien)) File.Delete(ancien);
                        File.Move(Fichier, ancien);
                    }

                    File.WriteAllText(Fichier, string.Empty, Encoding.UTF8);
                }
                catch
                {
                    // Un journal ne doit jamais empêcher l'application de démarrer.
                }
            }

            Info("UI", ticket);
        }

        public static void Info(string domaine, string message)  => Ecrire("INFO ", domaine, message);
        public static void Warn(string domaine, string message)  => Ecrire("WARN ", domaine, message);
        public static void Error(string domaine, string message) => Ecrire("ERROR", domaine, message);

        private static void Ecrire(string niveau, string domaine, string message)
        {
            lock (Verrou)
            {
                try
                {
                    Directory.CreateDirectory(Dossier);
                    RoterSiNecessaire();

                    string ligne =
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {niveau} | {domaine,-13} | {message}";

                    File.AppendAllText(Fichier, ligne + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Silence : une erreur d'écriture ne doit jamais casser l'action.
                }
            }
        }

        private static void RoterSiNecessaire()
        {
            try
            {
                var info = new FileInfo(Fichier);
                if (!info.Exists || info.Length < TailleMaxOctets)
                    return;

                string ancien = Path.Combine(Dossier, "MCEMonitor.1.log");

                if (File.Exists(ancien))
                    File.Delete(ancien);

                File.Move(Fichier, ancien);
            }
            catch
            {
                // Rotation best-effort.
            }
        }
    }
}
