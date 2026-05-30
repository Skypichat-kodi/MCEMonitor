using System;
using System.Threading;
using System.Threading.Tasks;

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

            // ?? Vide le log à chaque lancement
            LogHelper.Clear();

            LogHelper.Write("WakeMonitor démarré.");

            try
            {
                // ?? Vérification du vrai réveil Power-Troubleshooter
                if (!WakeEventFilter.IsRealWakeEvent())
                {
                    LogHelper.Write("Événement ignoré : ce n'est pas un réveil Power-Troubleshooter.");
                    return;
                }

                LogHelper.Write("Réveil Power-Troubleshooter détecté.");

                var cfg = EmailConfig.Load();
                var opt = WakeMonitorSettings.Load();
                var wake = WakeEventReader.GetLastWakeInfo();

                string ipLocal   = opt.IncludeLocalIP   ? NetworkInfo.GetActiveIp()                                                                            : "Désactivé";
                string mac       = opt.IncludeMAC       ? NetworkInfo.GetActiveMac()                                                                           : "Désactivé";
                string publicIp  = opt.IncludePublicIP  ? await PublicIP.GetPublicIP()                                                                         : "Désactivé";
                string usb       = opt.IncludeUSB       ? UsbDeviceDetector.GetLastUsbDevice()                                                                 : "Désactivé";
                string cause     = opt.IncludeCause     ? (wake.Cause == "Unknown" ? WakeCauseDetector.GetProbableWakeCause() : wake.Cause)                    : "Désactivé";
                string duration  = opt.IncludeDuration  ? wake.SleepDuration.ToString(@"hh\:mm\:ss")                                                           : "Désactivé";

                // ?? Normalisation : si une option est active mais retourne vide ? "Non disponible"
                if (opt.IncludeLocalIP   && string.IsNullOrWhiteSpace(ipLocal))   ipLocal = "Non disponible";
                if (opt.IncludeMAC       && string.IsNullOrWhiteSpace(mac))       mac = "Non disponible";
                if (opt.IncludePublicIP  && string.IsNullOrWhiteSpace(publicIp))  publicIp = "Non disponible";
                if (opt.IncludeUSB       && string.IsNullOrWhiteSpace(usb))       usb = "Non disponible";
                if (opt.IncludeCause     && string.IsNullOrWhiteSpace(cause))     cause = "Non disponible";
                if (opt.IncludeDuration  && string.IsNullOrWhiteSpace(duration))  duration = "Non disponible";

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
                    $"MAC : {mac}\n" +
                    $"IP publique : {publicIp}\n"
                );

                // Diagnostics
                LogHelper.WriteBlock("Diagnostic réseau", NetworkDiagnostics.GetInfo());
                LogHelper.WriteBlock("Diagnostic USB", UsbDiagnostics.GetInfo());
                LogHelper.WriteBlock("Événements Kernel", KernelEventDiagnostics.GetInfo());
                LogHelper.WriteBlock("Drivers", DriverDiagnostics.GetInfo());
                LogHelper.WriteBlock("Timers système", TimerDiagnostics.GetInfo());
                LogHelper.WriteBlock("État système", SystemDiagnostics.GetInfo());

                string subject = $"WakeMonitor - Réveil détecté ({wake.WakeTime:HH:mm:ss})";

                // Corps du mail
                string body =
                    $"<b>Réveil détecté</b><br><br>" +
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
                    $"<b>MAC :</b> {mac}<br>" +
                    $"<b>IP publique :</b> {publicIp}<br>";

                // ?? Section "Options désactivées"
                string disabled = "";

                if (!opt.IncludeDuration)  disabled += "- Durée<br>";
                if (!opt.IncludeCause)     disabled += "- Cause<br>";
                if (!opt.IncludeUSB)       disabled += "- USB<br>";
                if (!opt.IncludeLocalIP)   disabled += "- IP locale<br>";
                if (!opt.IncludeMAC)       disabled += "- MAC<br>";
                if (!opt.IncludePublicIP)  disabled += "- IP publique<br>";

                if (!string.IsNullOrEmpty(disabled))
                {
                    body += "<br><b>Options désactivées :</b><br>" + disabled;
                }

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

