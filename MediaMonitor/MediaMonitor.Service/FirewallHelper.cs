using System.Diagnostics;

namespace MediaMonitor.Service
{
    public static class FirewallHelper
    {
        public static void UpdateFirewallRule(int port)
        {
            string ruleName = "MediaMonitorWebPort";

            ExecuteNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");
            ExecuteNetsh($"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port}");
        }

        private static void ExecuteNetsh(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Process.Start(psi);
        }
    }
}

