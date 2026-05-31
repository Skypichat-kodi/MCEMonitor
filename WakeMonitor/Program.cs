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
                    // Détection tentative WOL
                    if (WakeEventFilter.IsWolAttempt())
                    {
                        string localMac = NetworkInfo.GetActiveMac();
                        string wolIp = WolIpDetector.GetWolSourceIp(localMac);
                        string wolMac = ArpResolver.GetMacFromIp(wolIp);

                        if (!string.IsNullOrWhiteSpace(wolMac))
                            wolMac = wolMac.Replace("-", ":").ToUpper();

                        var opt = WakeMonitorSettings.Load();
                        bool isAllowed = opt.AllowedWolMacs.Contains(wolMac);

                        if (isAllowed)
                        {
                            // --- WOL autorisé ---
                            LogHelper.Write($"WOL autorisé depuis {wolMac}");

                            var cfg = EmailConfig.Load();
                            string subject = "WakeMonitor - Wake-on-LAN autorisé";

                            string body =
                                "<b>Wake-on-LAN autorisé</b><br><br>" +
                                $"<b>MAC source :</b> {wolMac}<br>" +
                                $"<b>IP source :</b> {wolIp}<br>" +
                                $"<b>Machine :</b> {Environment.MachineName}<br>" +
                                $"<b>Utilisateur :</b> {Environment.UserName}<br>";

                            await EmailSender.SendAsync(cfg, subject, body, isHtml: true);
                            return;
                        }
                        else
                        {
                            // --- Tentative WOL suspecte ---
                            LogHelper.Write($"? Tentative WOL détectée depuis IP : {wolIp} (MAC : {wolMac})");

                            WOLSuspectLogHelper.WriteBlock("Tentative WOL suspecte",
                                $"IP source : {wolIp}\n" +
                                $"MAC source : {wolMac}\n" +
                                $"Machine : {Environment.MachineName}\n" +
                                $"Utilisateur : {Environment.UserName}\n" +
                                $"Uptime : {TimeSpan.FromMilliseconds(Environment.TickCount64)}");

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
                    }

                    LogHelper.Write("Événement ignoré : ce n'est pas un réveil Power-Troubleshooter.");
                    return;
                }

                // --- Réveil normal ---
                LogHelper.Write("Réveil Power-Troubleshooter détecté.");

                var cfg2 = EmailConfig.Load();
                var opt2 = WakeMonitorSettings.Load();
                var wake = WakeEventReader.GetLastWakeInfo();

                string ipLocal   = NetworkInfo.GetActiveIp();
                string mac2      = NetworkInfo.GetActiveMac();
                string publicIp  = await PublicIP.GetPublicIP();
                string usb       = UsbDeviceDetector.GetLastUsbDevice();
                string cause     = wake.Cause == "Unknown" ? WakeCauseDetector.GetProbableWakeCause() : wake.Cause;
                string duration  = wake.SleepDuration.ToString(@"hh\:mm\:ss");

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

    // --- Détecteur IP source WOL via ARP ---
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

