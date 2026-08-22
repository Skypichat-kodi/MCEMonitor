using System;
using System.Diagnostics.Eventing.Reader;
using System.Text;

namespace StopMonitor
{
    public enum ShutdownType
    {
        None,
        User,
        WindowsUpdate,
        WindowsUpdateRestart
    }

    public class ShutdownResult
    {
        public bool ShouldSendEmail { get; set; }
        public ShutdownType Type { get; set; }
        public DateTime Time { get; set; }
        public int EventId { get; set; }
        public string Details { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class ShutdownDetector
    {
        // ------------------------------------------------------------
        //  DÉTECTION DES ARRÊTS (1074 / 6006)
        // ------------------------------------------------------------
        public ShutdownResult DetectShutdown()
        {
            var result = new ShutdownResult();

            string query = "*[System[(EventID=1074 or EventID=6006)]]";

            var logQuery = new EventLogQuery("System", PathType.LogName, query);
            using var reader = new EventLogReader(logQuery);

            EventRecord latest = null;

            // On lit les 20 derniers événements pertinents
            for (EventRecord rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
            {
                if (latest == null || rec.TimeCreated > latest.TimeCreated)
                    latest = rec;
            }

            if (latest == null)
            {
                result.ShouldSendEmail = false;
                result.Type = ShutdownType.None;
                result.Description = "Aucun événement d'arrêt trouvé.";
                return result;
            }

            result.Time = latest.TimeCreated ?? DateTime.Now;
            result.EventId = latest.Id;

            var sb = new StringBuilder();

            // ------------------------------------------------------------
            //  EVENT 1074 — arrêt initié par utilisateur ou Windows Update
            // ------------------------------------------------------------
            if (latest.Id == 1074)
            {
                string process = latest.Properties.Count > 0 ? latest.Properties[0].Value?.ToString() : "";
                string user = latest.Properties.Count > 1 ? latest.Properties[1].Value?.ToString() : "";
                string reason = latest.Properties.Count > 4 ? latest.Properties[4].Value?.ToString() : "";

                sb.AppendLine($"Processus : {process}");
                sb.AppendLine($"Utilisateur : {user}");
                sb.AppendLine($"Raison : {reason}");

                result.Details = sb.ToString();

                if (reason != null && reason.Contains("Windows Update", StringComparison.OrdinalIgnoreCase))
                {
                    if (reason.Contains("restart", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Type = ShutdownType.WindowsUpdateRestart;
                        result.Description = "Redémarrage Windows Update détecté.";
                    }
                    else
                    {
                        result.Type = ShutdownType.WindowsUpdate;
                        result.Description = "Arrêt Windows Update détecté.";
                    }
                }
                else
                {
                    result.Type = ShutdownType.User;
                    result.Description = "Arrêt utilisateur détecté.";
                }

                result.ShouldSendEmail = true;
                return result;
            }

            // ------------------------------------------------------------
            //  EVENT 6006 — arrêt propre (ne déclenche pas d'email)
            // ------------------------------------------------------------
            if (latest.Id == 6006)
            {
                result.Type = ShutdownType.None;
                result.ShouldSendEmail = false;
                result.Description = "Arrêt propre détecté (6006). Aucun email envoyé.";
                result.Details = "Event Log Service stopped.";
                return result;
            }

            // ------------------------------------------------------------
            //  Cas par défaut
            // ------------------------------------------------------------
            result.Type = ShutdownType.None;
            result.ShouldSendEmail = false;
            result.Description = "Aucun arrêt nécessitant un email.";
            result.Details = sb.ToString();

            return result;
        }
    }
}
