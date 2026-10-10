using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MCEMonitor
{
    /// <summary>
    /// Exécuté par la tâche planifiée Windows "MCEMonitor_Shutdown".
    /// Envoie le mail, puis déclenche l'arrêt ou la veille.
    /// </summary>
    public static class ScheduledActionRunner
    {
        public static void Run(string mode)
        {
            bool isSleep = mode.Equals("sleep", StringComparison.OrdinalIgnoreCase);

            // -------- Lecture de l'heure réelle dans Shutdown.config --------
            int hour = -1, minute = -1;
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor", "Shutdown.config");

                if (File.Exists(configPath))
                {
                    foreach (var line in File.ReadAllLines(configPath))
                    {
                        if (line.StartsWith("Hour="))   int.TryParse(line.Substring(5), out hour);
                        if (line.StartsWith("Minute=")) int.TryParse(line.Substring(7), out minute);
                    }
                }
            }
            catch { }

            string heureProg = (hour >= 0 && minute >= 0)
                ? $"{hour:00}:{minute:00}"
                : "inconnue";

            // -------- Mail d'ANNONCE (5 minutes avant) --------
            try
            {
                var cfg = EmailConfig.Load();

                string actionLabel = isSleep ? "veille" : "arrêt";
                string actionVerb  = isSleep ? "Une veille" : "Un arrêt";

                string subject = isSleep
                    ? "MCEMonitor – Mise en veille programmée par MCEMonitor"
                    : "MCEMonitor – Arrêt programmé par MCEMonitor";

                string body =
                    $"<b>{actionVerb} est programmé{(isSleep ? "e" : "")} à : {heureProg}</b><br><br>" +
                    (isSleep
                        ? "Un autre email vous sera envoyé par <b>WakeMonitor</b> au moment de la mise en veille."
                        : "Un autre email vous sera envoyé par <b>StopMonitor</b> au moment de l'arrêt.") +
                    "<br><br>" +
                    $"<b>Machine :</b> {Environment.MachineName}<br>" +
                    $"<b>Utilisateur :</b> {Environment.UserName}<br>" +
                    $"<b>OS :</b> {Environment.OSVersion}<br>";

                // Envoi synchrone pour être sûr que le mail part avant la coupure
                EmailSender.SendAsync(cfg, subject, body, isHtml: true)
                           .GetAwaiter()
                           .GetResult();
            }
            catch (Exception ex)
            {
                LogError("Envoi mail annonce", ex);
            }

            // -------- Attente jusqu'à l'heure réelle --------
            try
            {
                System.Threading.Thread.Sleep(TimeSpan.FromMinutes(5));
            }
            catch { /* ignore */ }

            // -------- Action système --------
            try
            {
                ProcessStartInfo psi = isSleep
                    ? new ProcessStartInfo
                    {
                        FileName = "rundll32.exe",
                        Arguments = "powrprof.dll,SetSuspendState 0,1,0",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                    : new ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        Arguments = "/s /f /t 0",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                LogError("Action système", ex);
            }
        }

        private static void LogError(string context, Exception ex)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor", "Logs");

                Directory.CreateDirectory(dir);

                File.AppendAllText(
                    Path.Combine(dir, "scheduled_action_error.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [{context}] {ex.Message}{Environment.NewLine}");
            }
            catch { /* ne jamais planter */ }
        }
    }
}