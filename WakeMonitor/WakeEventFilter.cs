using System;
using System.Diagnostics.Eventing.Reader;

namespace WakeMonitor
{
    public static class WakeEventFilter
    {
        // Vérifie si l'événement est un vrai réveil Power-Troubleshooter
        public static bool IsRealWakeEvent()
        {
            try
            {
                var query = new EventLogQuery(
                    "System",
                    PathType.LogName,
                    "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and (EventID=1)]]"
                );

                using var reader = new EventLogReader(query);

                var evt = reader.ReadEvent();
                if (evt == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Détection d'une tentative WOL (suspecte ou autorisée)
        public static bool IsWolAttempt()
        {
            try
            {
                var wake = WakeEventReader.GetLastWakeInfo();

                // MAC locale
                string localMac = NetworkInfo.GetActiveMac();

                // IP source via ARP
                string wolIp = WolIpDetector.GetWolSourceIp(localMac);

                // MAC source via ARP
                string wolMac = ArpResolver.GetMacFromIp(wolIp);

                if (!string.IsNullOrWhiteSpace(wolMac))
                    wolMac = wolMac.Replace("-", ":").ToUpper();

                // Vérifie whitelist
                var opt = WakeMonitorSettings.Load();
                bool isAllowed = opt.AllowedWolMacs.Contains(wolMac);

                // Si MAC autorisée ? ce n'est PAS suspect
                if (isAllowed)
                    return true; // C'est bien un WOL, mais autorisé

                // Détection WOL classique
                bool networkSource =
                    wake.Cause.Contains("Ethernet", StringComparison.OrdinalIgnoreCase) ||
                    wake.Cause.Contains("Network", StringComparison.OrdinalIgnoreCase) ||
                    wake.Cause.Contains("PCI", StringComparison.OrdinalIgnoreCase) ||
                    wake.Cause.Contains("Inconnu", StringComparison.OrdinalIgnoreCase) ||
                    wake.Cause.Contains("Unknown", StringComparison.OrdinalIgnoreCase);

                bool uptimeTooShort =
                    TimeSpan.FromMilliseconds(Environment.TickCount64) < TimeSpan.FromSeconds(30);

                bool invalidSleep =
                    wake.SleepTime > DateTime.Now ||
                    wake.SleepTime == DateTime.MinValue;

                bool notRealWake = !IsRealWakeEvent();

                return networkSource && (uptimeTooShort || invalidSleep || notRealWake);
            }
            catch
            {
                return false;
            }
        }
    }

    // Récupère la MAC depuis une IP via ARP
    public static class ArpResolver
    {
        public static string GetMacFromIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return null;

            try
            {
                var p = new System.Diagnostics.Process();
                p.StartInfo.FileName = "arp";
                p.StartInfo.Arguments = "-a " + ip;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                foreach (var line in output.Split('\n'))
                {
                    if (line.Contains(ip))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                            return parts[1].Replace("-", ":").ToUpper();
                    }
                }
            }
            catch { }

            return null;
        }
    }
}

