using System.Diagnostics;

namespace NetMonitor.Service
{
    public static class FirewallHelper
    {
        private const string RuleName = "NetMonitorWebPort";

        public static void UpdateFirewallRule(int port)
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
            ExecuteNetsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}");
            CoreLog.Write($"Firewall : règle mise à jour pour port {port}");
        }

        public static void RemoveRule()
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
            CoreLog.Write("Firewall : règle supprimée");
        }

        public static bool BlockIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (IsAlreadyBlocked(ip)) return true;

            var ruleName = "NetMonitor-Block-" + ip;
            return RunNetsh(
                $"advfirewall firewall add rule name=\"{ruleName}\" " +
                $"dir=in action=block remoteip={ip} enable=yes profile=any") &&
                   RunNetsh(
                $"advfirewall firewall add rule name=\"{ruleName}-out\" " +
                $"dir=out action=block remoteip={ip} enable=yes profile=any");
        }

        public static bool UnblockIp(string ip)
        {
            var ruleName = "NetMonitor-Block-" + ip;
            var ok1 = RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");
            var ok2 = RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}-out\"");
            return ok1 || ok2;
        }

        public static bool IsAlreadyBlocked(string ip)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh",
                    $"advfirewall firewall show rule name=\"NetMonitor-Block-{ip}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is null) return false;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return output.Contains("Règle", System.StringComparison.OrdinalIgnoreCase)
                    || output.Contains("Rule", System.StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool RunNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is null) return false;
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch (System.Exception ex)
            {
                CoreLog.Write($"netsh : {ex.Message}");
                return false;
            }
        }

        private static void ExecuteNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { }
        }
    }
}