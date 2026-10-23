using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace MediaMonitor.Service
{
    public static class FirewallHelper
    {
        private const string RuleName = "MediaMonitorWebPort";

        private const int NETSH_TIMEOUT_MS = 15000;   // netsh peut être lent (GPO, antivirus…)

        /// <summary>
        /// Met à jour la règle pare-feu pour le port spécifié.
        /// Supprime l'ancienne règle puis ajoute la nouvelle.
        /// </summary>
        public static void UpdateFirewallRule(int port)
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
            ExecuteNetsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}");
        }

        /// <summary>
        /// Supprime la règle pare-feu.
        /// </summary>
        public static void RemoveRule()
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
        }

        private static void ExecuteNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "netsh",
                    Arguments              = args,
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    StandardOutputEncoding = Encoding.Default,
                    StandardErrorEncoding  = Encoding.Default
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    Debug.WriteLine($"FirewallHelper : impossible de lancer netsh '{args}'");
                    return;
                }

                // ?? CRITIQUE : lire stdout ET stderr, sinon le buffer du pipe
                // se remplit et netsh se bloque en écriture ? WaitForExit expire
                // systématiquement (5 s perdus par règle) et le process devient
                // orphelin (jamais tué).
                Task<string> stdoutTask = p.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(NETSH_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    Debug.WriteLine($"FirewallHelper : timeout netsh ({NETSH_TIMEOUT_MS} ms) '{args}'");
                    return;
                }

                string stdout = SafeResult(stdoutTask);
                string stderr = SafeResult(stderrTask);

                if (p.ExitCode != 0)
                {
                    Debug.WriteLine($"FirewallHelper : netsh '{args}' exit={p.ExitCode} : {stderr.Trim()}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FirewallHelper : netsh '{args}' : {ex.Message}");
            }
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