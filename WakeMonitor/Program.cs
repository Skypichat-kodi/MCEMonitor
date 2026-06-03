using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;

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

                // --- NOUVEAU : cause corrigée intelligente ---
                // wake.Cause pointe maintenant vers wake.WakeCause (compatibilité)
                string causeBrute = wake.WakeCause;

                string cause = WakeEventFilter.IsWolAttempt()
                    ? WakeCauseDetector.GetProbableWakeCause(wake, macLocal, opt)
                    : causeBrute;

                // Détection WOL depuis hibernation S4
                if (cause == "Inconnue" && IsWolFromHibernate())
                {
                    cause = "Wake-on-LAN (depuis hibernation S4)";
                }

                // 2) Analyse WOL via stratégie hybride
                var wol = WolDetectionStrategy.Detect(wake, macLocal, opt);

                // --- NOUVEAU : bloc visuel si Windows a mal classé le réveil ---
                string wolCorrectionBlock = "";
                if (wake.IsLikelyWol)
                {
                    wolCorrectionBlock = @"
<div style='background:#e8f4ff;border-left:4px solid #3498db;padding:10px;margin-bottom:15px'>
    <b>Cause réelle probable :</b> Wake-on-LAN (Magic Packet)<br>
    <span style='color:#555'>Windows a mal classé la source du réveil.</span>
</div>";
                }

                // 3) Log interne
                string duration = wake.SleepDuration.ToString(@"hh\:mm\:ss");

                LogHelper.WriteBlock("Infos réveil",
                    $"Réveil : {wake.WakeTime}\n" +
                    $"Sommeil : {wake.SleepTime}\n" +
                    $"Durée : {duration}\n" +
                    $"État précédent : {wake.SleepState}\n" +   // <-- AJOUT
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
                    wolCorrectionBlock +
                    "<b>=== Détails du réveil ===</b><br>" +
                    $"<b>Heure de réveil :</b> {wake.WakeTime}<br>" +
                    $"<b>Heure d'endormissement :</b> {wake.SleepTime}<br>" +
                    $"<b>Durée :</b> {duration}<br>" +
                    $"<b>État précédent :</b> {wake.SleepState}<br>" +   // <-- AJOUT
                    $"<b>Cause du réveil :</b> {cause}<br>" +            // <-- AJOUT (remplace ton ancien "Cause")
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

        // ---------------------------------------------------------
        // Détection WOL depuis hibernation S4
        // ---------------------------------------------------------

        private static bool IsWolFromHibernate()
        {
            bool armed = WasNetworkWakeArmedBeforeSleep();
            bool bootNet = IsBootTriggeredByNetwork();
            return armed && bootNet;
        }

        private static bool WasNetworkWakeArmedBeforeSleep()
        {
            try
            {
                var log = new EventLog("System");
                foreach (EventLogEntry entry in log.Entries.Cast<EventLogEntry>().Reverse())
                {
                    if (entry.Source == "Microsoft-Windows-Kernel-Power" &&
                        entry.Message.Contains("Wake Armed"))
                    {
                        if (entry.Message.Contains("Network"))
                            return true;

                        return false;
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool IsBootTriggeredByNetwork()
        {
            try
            {
                var log = new EventLog("System");
                foreach (EventLogEntry entry in log.Entries.Cast<EventLogEntry>().Reverse())
                {
                    if (entry.InstanceId == 27 || entry.InstanceId == 29 || entry.InstanceId == 30)
                    {
                        string msg = entry.Message.ToLower();

                        bool noLocalInput = !msg.Contains("power button") &&
                                            !msg.Contains("keyboard") &&
                                            !msg.Contains("mouse") &&
                                            !msg.Contains("hid");

                        bool networkDriverLoaded = msg.Contains("ndis") ||
                                                   msg.Contains("realtek") ||
                                                   msg.Contains("e1r") ||
                                                   msg.Contains("amdpcie") ||
                                                   msg.Contains("pci-e");

                        return noLocalInput && networkDriverLoaded;
                    }
                }
            }
            catch { }

            return false;
        }
    }
}

