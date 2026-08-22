using System;
using System.Diagnostics.Eventing.Reader;
using System.Text;

namespace StopMonitor
{
    public enum BootType
    {
        None,
        Crash,
        PowerLoss,
        Normal
    }

    public class BootResult
    {
        public BootType Type { get; set; }
        public DateTime Time { get; set; }
        public int EventId { get; set; }
        public string Details { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class BootDetector
    {
        public BootResult DetectBoot()
        {
            var result = new BootResult();

            string query =
                "*[System[(EventID=41 or EventID=6008 or EventID=1001 or EventID=12 or EventID=6005 or EventID=6009 or EventID=1074 or EventID=6006)]]";

            var logQuery = new EventLogQuery("System", PathType.LogName, query);
            using var reader = new EventLogReader(logQuery);

            EventRecord latest = null;
            EventRecord lastShutdown = null;
            EventRecord lastUnexpected = null;

            // Lecture des 20 derniers événements pertinents
            for (EventRecord rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
            {
                if (latest == null || rec.TimeCreated > latest.TimeCreated)
                    latest = rec;

                if (rec.Id == 1074 || rec.Id == 6006)
                    lastShutdown = rec;

                if (rec.Id == 6008)
                    lastUnexpected = rec;
            }

            if (latest == null)
            {
                result.Type = BootType.Normal;
                result.Description = "Démarrage normal (aucun événement trouvé).";
                return result;
            }

            result.Time = latest.TimeCreated ?? DateTime.Now;
            result.EventId = latest.Id;

            var sb = new StringBuilder();

            // ------------------------------------------------------------
            //  CRASH / BSOD (1001)
            // ------------------------------------------------------------
            if (latest.Id == 1001)
            {
                sb.AppendLine("Type : BSOD (BugCheck 1001).");
                if (latest.Properties.Count > 0)
                    sb.AppendLine($"Code : {latest.Properties[0].Value}");

                result.Type = BootType.Crash;
                result.Description = "Crash détecté (BSOD).";
                result.Details = sb.ToString();
                return result;
            }

            // ------------------------------------------------------------
            //  CRASH (6008)
            // ------------------------------------------------------------
            if (latest.Id == 6008)
            {
                sb.AppendLine("Type : Arrêt inattendu (EventID 6008).");

                result.Type = BootType.Crash;
                result.Description = "Crash détecté (arrêt inattendu).";
                result.Details = sb.ToString();
                return result;
            }

            // ------------------------------------------------------------
            //  KERNEL-POWER 41
            // ------------------------------------------------------------
            if (latest.Id == 41)
            {
                sb.AppendLine("Type : Redémarrage brutal (Kernel-Power 41).");

                // ---- Correction : calcul TimeSpan propre ----
                if (lastUnexpected != null)
                {
                    var delta = latest.TimeCreated.Value - lastUnexpected.TimeCreated.Value;

                    if (delta.TotalMinutes < 2)
                    {
                        result.Type = BootType.Crash;
                        result.Description = "Crash détecté (Kernel-Power 41 + arrêt inattendu).";
                        result.Details = sb.ToString();
                        return result;
                    }
                }

                // ---- Démarrage normal si 1074 juste avant ----
                if (lastShutdown != null &&
                    lastShutdown.Id == 1074 &&
                    lastShutdown.TimeCreated < latest.TimeCreated)
                {
                    var delta = latest.TimeCreated.Value - lastShutdown.TimeCreated.Value;

                    if (delta.TotalMinutes < 2)
                    {
                        result.Type = BootType.Normal;
                        result.Description = "Démarrage normal (arrêt utilisateur).";
                        result.Details = sb.ToString();
                        return result;
                    }
                }

                // ---- Sinon : coupure électrique ----
                result.Type = BootType.PowerLoss;
                result.Description = "Coupure électrique ou perte d'alimentation détectée.";
                result.Details = sb.ToString();
                return result;
            }

            // ------------------------------------------------------------
            //  DÉMARRAGE NORMAL (12 / 6005 / 6009)
            // ------------------------------------------------------------
            if (latest.Id == 12 || latest.Id == 6005 || latest.Id == 6009)
            {
                sb.AppendLine("Type : Démarrage normal (Kernel-General / EventLog).");

                result.Type = BootType.Normal;
                result.Description = "Démarrage de la machine normal.";
                result.Details = sb.ToString();
                return result;
            }

            // ------------------------------------------------------------
            //  PAR DÉFAUT : NORMAL
            // ------------------------------------------------------------
            result.Type = BootType.Normal;
            result.Description = "Démarrage normal (aucun événement critique).";
            result.Details = sb.ToString();

            return result;
        }
    }
}
