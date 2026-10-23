using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace WakeMonitor
{
    public static class ArpResolver
    {
        private const int ARP_TIMEOUT_MS = 5000;   // arp -a est rapide (<200ms typiquement)

        public static string GetMacFromIp(string ip)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "arp",
                    Arguments              = "-a",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.GetEncoding(850),
                    StandardErrorEncoding  = Encoding.GetEncoding(850)
                };

                using var p = Process.Start(psi);
                if (p == null) return "00-00-00-00-00-00";

                // Lecture parallèle stdout + stderr (évite le deadlock du ReadToEnd synchrone)
                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                // Timeout dur : jamais de blocage infini
                if (!p.WaitForExit(ARP_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    return "00-00-00-00-00-00";
                }

                string output = SafeResult(outTask);

                foreach (string line in output.Split('\n'))
                {
                    if (line.Contains(ip))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                            return parts[1].Trim();
                    }
                }
            }
            catch { }

            return "00-00-00-00-00-00";
        }

        public static string GetMostRecentIp()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "arp",
                    Arguments              = "-a",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.GetEncoding(850),
                    StandardErrorEncoding  = Encoding.GetEncoding(850)
                };

                using var p = Process.Start(psi);
                if (p == null) return "0.0.0.0";

                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(ARP_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    return "0.0.0.0";
                }

                string output = SafeResult(outTask);
                string lastIp = null;

                foreach (string line in output.Split('\n'))
                {
                    if (line.Contains("dynamic"))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 1)
                            lastIp = parts[0].Trim();
                    }
                }

                return lastIp ?? "0.0.0.0";
            }
            catch { }

            return "0.0.0.0";
        }

        // ============================================================
        //  Utilitaire : lecture sûre d'une Task<string> déjà terminée
        // ============================================================
        private static string SafeResult(Task<string> t)
        {
            try { return t.Wait(1000) ? (t.Result ?? "") : ""; }
            catch { return ""; }
        }
    }
}