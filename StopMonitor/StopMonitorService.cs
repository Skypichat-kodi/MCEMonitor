using System;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Threading.Tasks;

namespace StopMonitor
{
    public class StopMonitorService
    {
        // ------------------------------------------------------------
        //  LECTURE DES ÉVÉNEMENTS D'ARRÊT (1074 / 6006 / 6008)
        // ------------------------------------------------------------
        private (DateTime Time, int EventId, string Details) GetLastShutdownEvent()
        {
            string query =
                "*[System[(EventID=1074 or EventID=6006 or EventID=6008)]]";

            var logQuery = new EventLogQuery("System", PathType.LogName, query);

            using var reader = new EventLogReader(logQuery);

            EventRecord latest = null;

            for (EventRecord rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
            {
                if (latest == null || rec.TimeCreated > latest.TimeCreated)
                    latest = rec;
            }

            if (latest == null)
                return (DateTime.MinValue, 0, "Impossible de determiner la cause de l'arret.");

            var sb = new StringBuilder();

            switch (latest.Id)
            {
                case 1074:
                    sb.AppendLine("Type : Arret initie par un utilisateur ou une application.");
                    if (latest.Properties.Count > 0)
                        sb.AppendLine($"Processus : {latest.Properties[0].Value}");
                    if (latest.Properties.Count > 1)
                        sb.AppendLine($"Utilisateur : {latest.Properties[1].Value}");
                    if (latest.Properties.Count > 4)
                        sb.AppendLine($"Raison : {latest.Properties[4].Value}");
                    break;

                case 6006:
                    sb.AppendLine("Type : Arret propre (Event Log Service stopped).");
                    break;

                case 6008:
                    sb.AppendLine("Type : Arret inattendu (crash, coupure, panne).");
                    break;
            }

            return (latest.TimeCreated ?? DateTime.Now, latest.Id, sb.ToString());
        }


                // ------------------------------------------------------------
                //  LECTURE DES ÉVÉNEMENTS DE CRASH (41 / 6008 / 1001)
                // ------------------------------------------------------------
        private (DateTime Time, int EventId, string Details) GetLastCrashEvent()
        {
            EventRecord latest = null;

            // --- 1) Kernel-Power 41
            try
            {
                var kpQuery = new EventLogQuery(
                    "Microsoft-Windows-Kernel-Power/Operational",
                    PathType.LogName,
                    "*[System/EventID=41]"
                );

                using var reader = new EventLogReader(kpQuery);

                for (EventRecord rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
                {
                    if (latest == null || rec.TimeCreated > latest.TimeCreated)
                        latest = rec;
                }
            }
            catch { }

            // --- 2) 6008 et 1001
            try
            {
                string sysQuery =
                    "*[System[(EventID=6008 or EventID=1001)]]";

                var sysLogQuery = new EventLogQuery("System", PathType.LogName, sysQuery);

                using var reader = new EventLogReader(sysLogQuery);

                for (EventRecord rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
                {
                    if (latest == null || rec.TimeCreated > latest.TimeCreated)
                        latest = rec;
                }
            }
            catch { }

            if (latest == null)
                return (DateTime.MinValue, 0, "Impossible de determiner la cause du crash.");

            var sb = new StringBuilder();

            switch (latest.Id)
            {
                case 41:
                    sb.AppendLine("Type : Redemarrage brutal (Kernel-Power 41).");
                    break;

                case 6008:
                    sb.AppendLine("Type : Arret inattendu (EventID 6008).");
                    break;

                case 1001:
                    sb.AppendLine("Type : BSOD (BugCheck 1001).");
                    if (latest.Properties.Count > 0)
                        sb.AppendLine($"Code : {latest.Properties[0].Value}");
                    break;
            }

            return (latest.TimeCreated ?? DateTime.Now, latest.Id, sb.ToString());
        }

        // ------------------------------------------------------------
        //  ENVOI EMAIL ARRÊT
        // ------------------------------------------------------------
        public async Task SendShutdownEmail()
        {
            await Task.Delay(5000);

            var cfg = EmailConfig.Load();
            var evt = GetLastShutdownEvent();

            LogHelper.WriteBlock("Infos arrêt",
                $"Date : {evt.Time}\n" +
                $"EventID : {evt.EventId}\n" +
                $"{evt.Details}\n" +
                $"Machine : {Environment.MachineName}\n" +
                $"Utilisateur : {Environment.UserName}\n" +
                $"OS : {Environment.OSVersion}\n" +
                $"Uptime : {TimeSpan.FromMilliseconds(Environment.TickCount64)}\n"
            );

            string subject = $"StopMonitor - Arrêt détecté ({evt.Time:HH:mm:ss})";

            string body =
                $"<b>Arrêt détecté</b><br><br>" +
                $"<b>Date :</b> {evt.Time}<br>" +
                $"<b>EventID :</b> {evt.EventId}<br>" +
                $"<b>Détails :</b><br>{evt.Details.Replace("\n", "<br>")}<br><br>" +
                $"<b>Machine :</b> {Environment.MachineName}<br>" +
                $"<b>Utilisateur :</b> {Environment.UserName}<br>" +
                $"<b>OS :</b> {Environment.OSVersion}<br>" +
                $"<b>Uptime :</b> {TimeSpan.FromMilliseconds(Environment.TickCount64)}<br>";

            await EmailSender.SendAsync(cfg, subject, body, isHtml: true, style: EmailStyle.Normal);

            LogHelper.Write("Email envoyé avec succès.");
        }

        // ------------------------------------------------------------
        //  ENVOI EMAIL CRASH
        // ------------------------------------------------------------
        public async Task SendCrashEmail()
        {
            await Task.Delay(5000);

            var evt = GetLastCrashEvent();
            var cfg = EmailConfig.Load();

            LogHelper.WriteBlock("Infos crash",
                $"Date : {evt.Time}\n" +
                $"EventID : {evt.EventId}\n" +
                $"{evt.Details}\n" +
                $"Machine : {Environment.MachineName}\n" +
                $"Utilisateur : {Environment.UserName}\n" +
                $"OS : {Environment.OSVersion}\n" +
                $"Uptime : {TimeSpan.FromMilliseconds(Environment.TickCount64)}\n"
            );

            string subject = $"StopMonitor - Crash détecté ({evt.Time:HH:mm:ss})";

            string body =
                $"<b>Crash détecté</b><br><br>" +
                $"<b>Date :</b> {evt.Time}<br>" +
                $"<b>EventID :</b> {evt.EventId}<br>" +
                $"<b>Détails :</b><br>{evt.Details.Replace("\n", "<br>")}<br><br>" +
                $"<b>Machine :</b> {Environment.MachineName}<br>" +
                $"<b>Utilisateur :</b> {Environment.UserName}<br>" +
                $"<b>OS :</b> {Environment.OSVersion}<br>" +
                $"<b>Uptime :</b> {TimeSpan.FromMilliseconds(Environment.TickCount64)}<br>";

            await EmailSender.SendAsync(cfg, subject, body, isHtml: true, style: EmailStyle.Error);

            LogHelper.Write("Email de crash envoyé avec succès.");
        }
    }
}

