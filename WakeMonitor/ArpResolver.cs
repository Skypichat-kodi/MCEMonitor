using System;
using System.Diagnostics;

namespace WakeMonitor
{
    public static class ArpResolver
    {
        public static string GetMacFromIp(string ip)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                string output = p.StandardOutput.ReadToEnd();

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
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                string output = p.StandardOutput.ReadToEnd();
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
    }
}

