using System.Diagnostics;

namespace NetMonitor.Service
{
    public static class FirewallHelper
    {
        private const string WebRuleName = "NetMonitorWebPort";
        private const string BlockPrefix = "NetMonitor-Block-";

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

            string rule = BlockPrefix + ip;

            // Supprime d'abord les règles existantes (netsh autorise les doublons sinon)
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{rule}\"");
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{rule}-out\"");

            bool okIn = ExecuteNetsh($"advfirewall firewall add rule name=\"{rule}\" dir=in action=block remoteip={ip} enable=yes profile=any");
            bool okOut = ExecuteNetsh($"advfirewall firewall add rule name=\"{rule}-out\" dir=out action=block remoteip={ip} enable=yes profile=any");

            CoreLog.Info($"Firewall : blocage {ip} ? in={okIn} out={okOut}");
            return okIn && okOut;
        }

        public static bool UnblockIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;

            string rule = BlockPrefix + ip;

            bool ok1 = ExecuteNetsh($"advfirewall firewall delete rule name=\"{rule}\"");
            bool ok2 = ExecuteNetsh($"advfirewall firewall delete rule name=\"{rule}-out\"");

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

        private static bool ExecuteNetsh(string args)
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
                if (p is null) return false;

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);

                if (p.ExitCode != 0)
                {
                    CoreLog.Warn($"netsh exit={p.ExitCode} : {stderr.Trim()} {stdout.Trim()}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                CoreLog.Error($"netsh : {ex.Message}");
                return false;
            }
        }
    }
}