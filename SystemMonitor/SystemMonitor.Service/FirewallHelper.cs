using System.Diagnostics;

namespace SystemMonitor.Service
{
    public static class FirewallHelper
    {
        // Nom actuel de la règle (SystemMonitor)
        private const string RuleName = "SystemMonitorWebPort";

        // Ancien nom — RomMonitor, hérité d'un copier-coller historique.
        // Conservé pour le nettoyage lors des mises à jour.
        private const string LegacyRuleName = "RomMonitorWebPort";

        /// <summary>
        /// Met à jour la règle pare-feu pour le port spécifié.
        /// Supprime l'ancienne règle puis ajoute la nouvelle.
        /// </summary>
        public static void UpdateFirewallRule(int port)
        {
            // Nettoyage de l'ancien nom (migration depuis RomMonitor)
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{LegacyRuleName}\"");

            // Suppression de l'éventuelle règle existante avec le nouveau nom
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");

            // Ajout de la règle à jour
            ExecuteNetsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}");

            CoreLog.Write($"Firewall : règle mise à jour pour port {port}");
        }

        /// <summary>
        /// Supprime la règle pare-feu (nom actuel + ancien).
        /// </summary>
        public static void RemoveRule()
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{LegacyRuleName}\"");
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
                p?.WaitForExit(5000);
            }
            catch { }
        }
    }
}