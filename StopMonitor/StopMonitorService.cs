using System;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Threading.Tasks;

namespace StopMonitor
{
    public class StopMonitorService
    {
        private (DateTime Time, int EventId, string Details) GetLastShutdownEvent()
        {
            string query =
                "*[System[(EventID=1074 or EventID=6006 or EventID=6008)]]";

            var logQuery = new EventLogQuery("System", PathType.LogName, query)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(logQuery);
            var record = reader.ReadEvent();

            if (record == null)
                return (DateTime.MinValue, 0, "Impossible de déterminer la cause de l'arrêt.");

            var sb = new StringBuilder();

            switch (record.Id)
            {
                case 1074:
                    sb.AppendLine("Type : Arrêt initié par un utilisateur ou une application.");
                    if (record.Properties.Count > 0)
                        sb.AppendLine($"Processus : {record.Properties[0].Value}");
                    if (record.Properties.Count > 1)
                        sb.AppendLine($"Utilisateur : {record.Properties[1].Value}");
                    if (record.Properties.Count > 4)
                        sb.AppendLine($"Raison : {record.Properties[4].Value}");
                    break;

                case 6006:
                    sb.AppendLine("Type : Arrêt propre (Event Log Service stopped).");
                    break;

                case 6008:
                    sb.AppendLine("Type : Arrêt inattendu (crash, coupure, panne).");
                    break;
            }

            return (record.TimeCreated ?? DateTime.Now, record.Id, sb.ToString());
        }

        public async Task SendShutdownEmail()
        {
            await Task.Delay(500);

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

            // ?? Email GRIS
            await EmailSender.SendAsync(cfg, subject, body, isHtml: true, style: EmailStyle.Normal);

            LogHelper.Write("Email envoyé avec succès.");
        }

        public async Task SendCrashEmail()
        {
            await Task.Delay(500);

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

            // ?? Email ROUGE (BSOD)
            await EmailSender.SendAsync(cfg, subject, body, isHtml: true, style: EmailStyle.Error);

            LogHelper.Write("Email de crash envoyé avec succès.");
        }

        private (DateTime Time, int EventId, string Details) GetLastCrashEvent()
        {
            string query =
                "*[System[(EventID=41 or EventID=6008 or EventID=1001)]]";

            var logQuery = new EventLogQuery("System", PathType.LogName, query)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(logQuery);
            var record = reader.ReadEvent();

            if (record == null)
                return (DateTime.MinValue, 0, "Impossible de déterminer la cause du crash.");

            var sb = new StringBuilder();

            switch (record.Id)
            {
                case 41:
                    sb.AppendLine("Type : Redémarrage brutal (Kernel-Power 41).");
                    break;

                case 6008:
                    sb.AppendLine("Type : Arrêt inattendu (EventID 6008).");
                    break;

                case 1001:
                    sb.AppendLine("Type : BSOD (BugCheck 1001).");
                    if (record.Properties.Count > 0)
                        sb.AppendLine($"Code : {record.Properties[0].Value}");
                    break;
            }

            return (record.TimeCreated ?? DateTime.Now, record.Id, sb.ToString());
        }
    }
}

