using System;
using System.Threading.Tasks;

namespace StopMonitor
{
    public class StopMonitorService
    {
        private readonly ShutdownDetector _shutdownDetector = new ShutdownDetector();
        private readonly BootDetector _bootDetector = new BootDetector();

        // ------------------------------------------------------------
        //  TRAITEMENT MODE SHUTDOWN
        // ------------------------------------------------------------
        public async Task ProcessShutdownAsync()
        {
            LogHelper.Write("Analyse des evenements d'arret...");

            var result = _shutdownDetector.DetectShutdown();

            if (!result.ShouldSendEmail)
            {
                LogHelper.Write("Aucun arret necessitant un email.");
                return;
            }

            LogHelper.Write($"Arret detecte : {result.Type}");

            var cfg = EmailConfig.Load();

            string subject = result.Type switch
            {
                ShutdownType.User => "StopMonitor – Arret utilisateur detecte",
                ShutdownType.WindowsUpdate => "StopMonitor – Arret Windows Update detecte",
                ShutdownType.WindowsUpdateRestart => "StopMonitor – Redemarrage Windows Update detecte",
                _ => "StopMonitor – Arret detecte"
            };

            string body =
                $"<b>{result.Description}</b><br><br>" +
                $"<b>Date :</b> {result.Time}<br>" +
                $"<b>EventID :</b> {result.EventId}<br>" +
                $"<b>Details :</b><br>{result.Details.Replace("\n", "<br>")}<br><br>" +
                $"<b>Machine :</b> {Environment.MachineName}<br>" +
                $"<b>Utilisateur :</b> {Environment.UserName}<br>" +
                $"<b>OS :</b> {Environment.OSVersion}<br>" +
                $"<b>Uptime :</b> {TimeSpan.FromMilliseconds(Environment.TickCount64)}<br>";

            LogHelper.WriteBlock("Infos arret",
                $"Type : {result.Type}\n" +
                $"Date : {result.Time}\n" +
                $"EventID : {result.EventId}\n" +
                $"{result.Details}\n" +
                $"Machine : {Environment.MachineName}\n" +
                $"Utilisateur : {Environment.UserName}\n" +
                $"OS : {Environment.OSVersion}\n" +
                $"Uptime : {TimeSpan.FromMilliseconds(Environment.TickCount64)}\n"
            );

            await EmailSender.SendAsync(cfg, subject, body, isHtml: true, style: EmailStyle.Normal);

            LogHelper.Write("Email d'arret envoye avec succes.");
        }

        // ------------------------------------------------------------
        //  TRAITEMENT MODE BOOT
        // ------------------------------------------------------------
        public async Task ProcessBootAsync()
        {
            LogHelper.Write("Analyse des evenements de demarrage...");

            var result = _bootDetector.DetectBoot();

            LogHelper.Write($"Demarrage detecte : {result.Type}");

            var cfg = EmailConfig.Load();

            string subject = result.Type switch
            {
                BootType.Crash => "StopMonitor – Crash detecte",
                BootType.PowerLoss => "StopMonitor – Coupure electrique detectee",
                BootType.Normal => "StopMonitor – Demarrage normal",
                _ => "StopMonitor – Demarrage de la machine"
            };

            EmailStyle style = result.Type switch
            {
                BootType.Crash => EmailStyle.Error,
                BootType.PowerLoss => EmailStyle.Error,
                BootType.Normal => EmailStyle.Success,
                _ => EmailStyle.Normal
            };

            string body =
                $"<b>{result.Description}</b><br><br>" +
                $"<b>Date :</b> {result.Time}<br>" +
                $"<b>EventID :</b> {result.EventId}<br>" +
                $"<b>Details :</b><br>{result.Details.Replace("\n", "<br>")}<br><br>" +
                $"<b>Machine :</b> {Environment.MachineName}<br>" +
                $"<b>Utilisateur :</b> {Environment.UserName}<br>" +
                $"<b>OS :</b> {Environment.OSVersion}<br>" +
                $"<b>Uptime :</b> {TimeSpan.FromMilliseconds(Environment.TickCount64)}<br>";

            LogHelper.WriteBlock("Infos demarrage",
                $"Type : {result.Type}\n" +
                $"Date : {result.Time}\n" +
                $"EventID : {result.EventId}\n" +
                $"{result.Details}\n" +
                $"Machine : {Environment.MachineName}\n" +
                $"Utilisateur : {Environment.UserName}\n" +
                $"OS : {Environment.OSVersion}\n" +
                $"Uptime : {TimeSpan.FromMilliseconds(Environment.TickCount64)}\n"
            );

            await EmailSender.SendAsync(cfg, subject, body, isHtml: true, style: style);

            LogHelper.Write("Email de demarrage envoye avec succes.");
        }
    }
}
