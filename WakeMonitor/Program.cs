using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace WakeMonitor
{
    internal class Program
    {
        private static Mutex _mutex;

        static async Task Main()
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_WakeMonitor", out createdNew);
            if (!createdNew)
                return;

            LogHelper.Clear();
            LogHelper.Write("WakeMonitor démarré.");

            try
            {
                // 1) Vérification du vrai réveil Power-Troubleshooter
                if (!WakeEventFilter.IsRealWakeEvent())
                {
                    LogHelper.Write("Événement ignoré : ce n'est pas un réveil Power-Troubleshooter.");
                    return;
                }

                LogHelper.Write("Réveil Power-Troubleshooter détecté.");

                var cfg  = EmailConfig.Load();
                var opt  = WakeMonitorSettings.Load();
                var wake = WakeEventReader.GetLastWakeInfo();

                string ipLocal  = NetworkInfo.GetActiveIp();
                string macLocal = NetworkInfo.GetActiveMac();
                string publicIp = await PublicIP.GetPublicIP();
                string usb      = UsbDeviceDetector.GetLastUsbDevice();

                // SUPPRESSION DU DOUBLON :
                // string localMac = NetworkInfo.GetActiveMac();
                // var opt = WakeMonitorSettings.Load();

                // On utilise macLocal comme MAC locale
                string cause = WakeEventFilter.IsWolAttempt()
                    ? WakeCauseDetector.GetProbableWakeCause(wake, macLocal, opt)
                    : wake.Cause;

                string duration = wake.SleepDuration.ToString(@"hh\:mm\:ss");

                // 2) Analyse WOL via stratégie hybride
                var wol = WolDetectionStrategy.Detect(wake, macLocal, opt);

                // 3) Log interne
                LogHelper.WriteBlock("Infos réveil",
                    $"Réveil : {wake.WakeTime}\n" +
                    $"Sommeil : {wake.SleepTime}\n" +
                    $"Durée : {duration}\n" +
                    $"Cause : {cause}\n" +
                    $"USB : {usb}\n" +
                    $"Machine : {Environment.MachineName}\n" +
                    $"Utilisateur : {Environment.UserName}\n" +
                    $"OS : {Environment.OSVersion}\n" +
                    $"Uptime : {TimeSpan.FromMilliseconds(Environment.TickCount64)}\n" +
                    $"IP locale : {ipLocal}\n" +
                    $"MAC : {macLocal}\n" +
                    $"IP publique : {publicIp}\n"
                );

                // 4) Sujet du mail
                string subject = $"WakeMonitor - {wol.Tag} ({wake.WakeTime:HH:mm:ss})";

                // 5) Corps du mail
                string body =
                    wol.HtmlBlock +
                    "<b>=== Détails du réveil ===</b><br>" +
                    $"<b>Heure de réveil :</b> {wake.WakeTime}<br>" +
                    $"<b>Heure d'endormissement :</b> {wake.SleepTime}<br>" +
                    $"<b>Durée :</b> {duration}<br>" +
                    $"<b>Cause :</b> {cause}<br>" +
                    $"<b>USB :</b> {usb}<br>" +
                    $"<b>Machine :</b> {Environment.MachineName}<br>" +
                    $"<b>Utilisateur :</b> {Environment.UserName}<br>" +
                    $"<b>OS :</b> {Environment.OSVersion}<br>" +
                    $"<b>Uptime :</b> {TimeSpan.FromMilliseconds(Environment.TickCount64)}<br>" +
                    $"<b>IP locale :</b> {ipLocal}<br>" +
                    $"<b>MAC locale :</b> {macLocal}<br>" +
                    $"<b>IP publique :</b> {publicIp}<br>";

                await EmailSender.SendAsync(cfg, subject, body, isHtml: true);

                LogHelper.Write("Email envoyé avec succès.");
            }
            catch (Exception ex)
            {
                LogHelper.Write("ERREUR : " + ex.Message);
            }

            LogHelper.Write("WakeMonitor terminé.");
        }
    }
}

