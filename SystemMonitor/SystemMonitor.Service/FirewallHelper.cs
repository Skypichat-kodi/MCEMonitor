using System.Diagnostics;

namespace SystemMonitor.Service
{
    public static class FirewallHelper
    {
        private const string RuleName = "RomMonitorWebPort";

        /// <summary>
        /// Met à jour la règle pare-feu pour le port spécifié.
        /// Supprime l'ancienne règle puis ajoute la nouvelle.
        /// </summary>
        public static void UpdateFirewallRule(int port)
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
            ExecuteNetsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}");
            CoreLog.Write($"Firewall : règle mise à jour pour port {port}");
        }

        /// <summary>
        /// Supprime la règle pare-feu.
        /// </summary>
        public static void RemoveRule()
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
            CoreLog.Write("Firewall : règle supprimée");
        }

        private static void ExecuteNetsh(string args)
        {
            try
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

                using var p = Process.Start(psi);
                p?.WaitForExit(5000);   // ?? attendre la fin
            }
            catch { }
        }
    }
}