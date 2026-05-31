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
                // Vérification du vrai réveil Power-Troubleshooter
                if (!WakeEventFilter.IsRealWakeEvent())
                {
                    // ?? Détection tentative WOL
                    if (WakeEventFilter.IsWolAttempt())
                    {
                        string localMac = NetworkInfo.GetActiveMac();
                        string wolIp = WolIpDetector.GetWolSourceIp(localMac);
                        string wolMac = ArpResolver.GetMacFromIp(wolIp);

                        LogHelper.Write($"? Tentative WOL détectée depuis IP : {wolIp} (MAC : {wolMac})");
                        WOLLogHelper.WriteBlock("Tentative WOL détectée",
                            $"IP source : {wolIp}\n" +
                            $"MAC source : {wolMac}\n" +
                            $"Machine : {Environment.MachineName}\n" +
                            $"Utilisateur : {Environment.UserName}\n" +
                            $"Uptime : {TimeSpan.FromMilliseconds(Environment.TickCount64)}");
                        var cfg = EmailConfig.Load();
                        string subject = "WakeMonitor - Tentative Wake-on-LAN détectée";

                        string body =
                            "<div style='color:red;font-weight:bold;font-size:20px;'>? Tentative Wake-on-LAN détectée</div>" +
                            "<br><b>IP source probable :</b> " + wolIp +
                            "<br><b>MAC source probable :</b> " + (wolMac ?? "Inconnue") +
                            "<br><br><b>Machine :</b> " + Environment.MachineName +
                            "<br><b>Utilisateur :</b> " + Environment.UserName +
                            "<br><b>OS :</b> " + Environment.OSVersion +
                            "<br><b>Uptime :</b> " + TimeSpan.FromMilliseconds(Environment.TickCount64);

                        await EmailSender.SendAsync(cfg, subject, body, isHtml: true);

                        return;
                    }

                    LogHelper.Write("Événement ignoré : ce n'est pas un réveil Power-Troubleshooter.");
                    return;
                }

                LogHelper.Write("Réveil Power-Troubleshooter détecté.");

                var cfg2 = EmailConfig.Load();
                var opt = WakeMonitorSettings.Load();
                var wake = WakeEventReader.GetLastWakeInfo();

                string ipLocal   = opt.IncludeLocalIP   ? NetworkInfo.GetActiveIp()                                                                            : "Désactivé";
                string mac2      = opt.IncludeMAC       ? NetworkInfo.GetActiveMac()                                                                           : "Désactivé";
                string publicIp  = opt.IncludePublicIP  ? await PublicIP.GetPublicIP()                                                                         : "Désactivé";
                string usb       = opt.IncludeUSB       ? UsbDeviceDetector.GetLastUsbDevice()                                                                 : "Désactivé";
                string cause     = opt.IncludeCause     ? (wake.Cause == "Unknown" ? WakeCauseDetector.GetProbableWakeCause() : wake.Cause)                    : "Désactivé";
                string duration  = opt.IncludeDuration  ? wake.SleepDuration.ToString(@"hh\:mm\:ss")                                                           : "Désactivé";

                if (opt.IncludeLocalIP   && string.IsNullOrWhiteSpace(ipLocal))   ipLocal = "Non disponible";
                if (opt.IncludeMAC       && string.IsNullOrWhiteSpace(mac2))      mac2 = "Non disponible";
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
                    $"MAC : {mac2}\n" +
                    $"IP publique : {publicIp}\n"
                );

                LogHelper.WriteBlock("Diagnostic réseau", NetworkDiagnostics.GetInfo());
                LogHelper.WriteBlock("Diagnostic USB", UsbDiagnostics.GetInfo());
                LogHelper.WriteBlock("Événements Kernel", KernelEventDiagnostics.GetInfo());
                LogHelper.WriteBlock("Drivers", DriverDiagnostics.GetInfo());
                LogHelper.WriteBlock("Timers système", TimerDiagnostics.GetInfo());
                LogHelper.WriteBlock("État système", SystemDiagnostics.GetInfo());

                string subject2 = $"WakeMonitor - Réveil détecté ({wake.WakeTime:HH:mm:ss})";

                string body2 =
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
                    $"<b>MAC :</b> {mac2}<br>" +
                    $"<b>IP publique :</b> {publicIp}<br>";

                string disabled = "";

                if (!opt.IncludeDuration)  disabled += "- Durée<br>";
                if (!opt.IncludeCause)     disabled += "- Cause<br>";
                if (!opt.IncludeUSB)       disabled += "- USB<br>";
                if (!opt.IncludeLocalIP)   disabled += "- IP locale<br>";
                if (!opt.IncludeMAC)       disabled += "- MAC<br>";
                if (!opt.IncludePublicIP)  disabled += "- IP publique<br>";

                if (!string.IsNullOrEmpty(disabled))
                {
                    body2 += "<br><b>Options désactivées :</b><br>" + disabled;
                }

                await EmailSender.SendAsync(cfg2, subject2, body2, isHtml: true);

                LogHelper.Write("Email envoyé avec succès.");
            }
            catch (Exception ex)
            {
                LogHelper.Write("ERREUR : " + ex.Message);
            }

            LogHelper.Write("WakeMonitor terminé.");
        }
    }

    // ?? Détecteur IP source WOL via ARP
    public static class WolIpDetector
    {
        public static string GetWolSourceIp(string targetMac)
        {
            try
            {
                var p = new Process();
                p.StartInfo.FileName = "arp";
                p.StartInfo.Arguments = "-a";
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                targetMac = targetMac.Replace("-", ":").ToLower();

                foreach (var line in output.Split('\n'))
                {
                    if (line.ToLower().Contains(targetMac))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                            return parts[0]; // IP trouvée
                    }
                }
            }
            catch { }

            return "Inconnue";
        }
    }
}

