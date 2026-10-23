using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NetMonitor.Service
{
    public static class FirewallHelper
    {
        private const string WebRuleName = "NetMonitorWebPort";
        private const string BlockPrefix = "NetMonitor-Block-";
        private const int IpsPerRule = 200;   // Limite conservative (netsh ~4096 chars)

        private const int NETSH_TIMEOUT_MS = 15000;   // netsh peut �tre lent (r�gles nombreuses, GPO�)

        // Verrou pour �viter les race conditions sur SyncFirewallRules
        private static readonly object _syncLock = new();

        // ============================================================
        //  Plages d'IP critiques (� ne JAMAIS bloquer)
        // ============================================================
        private static readonly string[] CriticalNetworks = new[]
        {
            // Loopback & priv� (r�seau local)
            "127.0.0.0/8",
            "10.0.0.0/8",
            "172.16.0.0/12",
            "192.168.0.0/16",
            "169.254.0.0/16",

            // Microsoft / Azure
            "13.107.0.0/16",
            "20.0.0.0/8",
            "40.0.0.0/8",
            "52.0.0.0/8",
            "204.79.197.0/24",
            "65.52.0.0/14",
            "157.56.0.0/14",

            // Google
            "142.250.0.0/15",
            "172.217.0.0/16",
            "216.58.192.0/19",
            "74.125.0.0/16",
            "173.194.0.0/16",

            // Cloudflare
            "104.16.0.0/12",
            "172.64.0.0/13",
            "1.1.1.0/24",
            "1.0.0.0/24"
        };

        public static bool IsCriticalIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return false;

            foreach (var cidr in CriticalNetworks)
            {
                if (IsInCidr(addr, cidr)) return true;
            }

            return false;
        }

        private static bool IsInCidr(IPAddress ip, string cidr)
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;

            if (!IPAddress.TryParse(parts[0], out var network)) return false;
            if (!int.TryParse(parts[1], out int prefixLength)) return false;

            var ipBytes = ip.GetAddressBytes();
            var netBytes = network.GetAddressBytes();

            if (ipBytes.Length != netBytes.Length) return false;

            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            for (int i = 0; i < fullBytes; i++)
            {
                if (ipBytes[i] != netBytes[i]) return false;
            }

            if (remainingBits > 0)
            {
                int mask = 0xFF << (8 - remainingBits);
                if ((ipBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask))
                    return false;
            }

            return true;
        }

        // ============================================================
        //  Synchronisation des r�gles de blocage (group�es par blocs)
        // ============================================================
        /// <summary>
        /// Reconstruit TOUTES les r�gles de blocage NetMonitor � partir de la
        /// liste compl�te des IPs bloqu�es. R�partit les IPs en blocs de 200.
        /// </summary>
        public static bool SyncFirewallRules(IEnumerable<string> blockedIps)
        {
            lock (_syncLock)
            {
                try
                {
                    var ips = blockedIps
                        .Where(ip => !string.IsNullOrWhiteSpace(ip))
                        .Distinct()
                        .ToList();

                    // 1. Supprimer toutes les anciennes r�gles NetMonitor-Block-*
                    DeleteAllBlockRules();

                    if (ips.Count == 0)
                    {
                        CoreLog.Info("Firewall : aucune IP bloqu�e, toutes les r�gles ont �t� supprim�es");
                        return true;
                    }

                    // 2. Recr�er les r�gles par blocs de 200 IPs
                    int blockCount = (int)Math.Ceiling(ips.Count / (double)IpsPerRule);
                    int successCount = 0;

                    for (int i = 0; i < blockCount; i++)
                    {
                        var batch = ips.Skip(i * IpsPerRule).Take(IpsPerRule).ToList();
                        if (batch.Count == 0) continue;

                        string remoteIps = string.Join(",", batch);
                        string suffix = $"{i + 1:00}";   // 01, 02, 03...

                        string ruleInName  = $"{BlockPrefix}{suffix}-In";
                        string ruleOutName = $"{BlockPrefix}{suffix}-Out";

                        bool okIn  = ExecuteNetsh($"advfirewall firewall add rule name=\"{ruleInName}\" " +
                                                  $"dir=in action=block remoteip={remoteIps} enable=yes profile=any");
                        bool okOut = ExecuteNetsh($"advfirewall firewall add rule name=\"{ruleOutName}\" " +
                                                  $"dir=out action=block remoteip={remoteIps} enable=yes profile=any");

                        if (okIn && okOut)
                        {
                            successCount++;
                            CoreLog.Info($"Firewall : bloc {suffix} cr�� ({batch.Count} IPs)");
                        }
                        else
                        {
                            CoreLog.Warn($"Firewall : �chec cr�ation bloc {suffix} " +
                                         $"(in={okIn}, out={okOut}, {batch.Count} IPs)");
                        }
                    }

                    CoreLog.Info($"Firewall : synchronisation termin�e ({successCount}/{blockCount} bloc(s), {ips.Count} IPs)");
                    return successCount == blockCount;
                }
                catch (Exception ex)
                {
                    CoreLog.Error($"SyncFirewallRules : {ex.Message}");
                    return false;
                }
            }
        }

        // ============================================================
        //  Suppression de toutes les r�gles NetMonitor-Block-*
        // ============================================================
        private static void DeleteAllBlockRules()
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "advfirewall firewall show rule name=all")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.Default,
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    CoreLog.Warn("DeleteAllBlockRules : impossible de lancer netsh");
                    return;
                }

                // Lire stdout AVANT WaitForExit pour �viter les deadlocks
                var outputBuilder = new System.Text.StringBuilder();
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) outputBuilder.AppendLine(e.Data);
                };
                p.BeginOutputReadLine();

                // ?? Timeout dur : netsh peut se bloquer si le pare-feu est verrouill� (GPO, antivirus�)
                if (!p.WaitForExit(NETSH_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    CoreLog.Warn($"DeleteAllBlockRules : timeout netsh ({NETSH_TIMEOUT_MS} ms) � suppression ignor�e");
                    return;
                }

                // ?? OBLIGATOIRE avec BeginOutputReadLine : garantit que tous les
                // handlers OutputDataReceived ont fini de vider le buffer.
                // S�r ici : le process a d�j� quitt� (WaitForExit(ms) a retourn� true).
                p.WaitForExit();

                string output = outputBuilder.ToString();
                CoreLog.Debug($"DeleteAllBlockRules : sortie netsh = {output.Length} caract�res");

                // Cherche tous les noms de r�gles NetMonitor-Block-*
                var regex = new Regex(@"(NetMonitor-Block-[\w\.\-]+)", RegexOptions.IgnoreCase);
                var ruleNames = regex.Matches(output)
                                     .Select(m => m.Groups[1].Value)
                                     .Where(n => !string.IsNullOrWhiteSpace(n))
                                     .Distinct()
                                     .ToList();

                CoreLog.Info($"DeleteAllBlockRules : {ruleNames.Count} r�gle(s) trouv�e(s) � supprimer");

                int deleted = 0;
                foreach (var ruleName in ruleNames)
                {
                    if (ExecuteNetsh($"advfirewall firewall delete rule name=\"{ruleName}\""))
                        deleted++;
                    else
                        CoreLog.Warn($"DeleteAllBlockRules : �chec suppression '{ruleName}'");
                }

                CoreLog.Info($"Firewall : {deleted}/{ruleNames.Count} ancienne(s) r�gle(s) supprim�e(s)");
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"DeleteAllBlockRules : {ex.Message}");
            }
        }

        // ============================================================
        //  V�rifie si une IP est pr�sente dans AU MOINS une r�gle
        //  (utilis� pour diagnostic uniquement � pas critique)
        // ============================================================
        public static bool IsAlreadyBlocked(string ip)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "advfirewall firewall show rule name=all")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.Default,
                };

                using var p = Process.Start(psi);
                if (p == null) return false;

                // Lecture asynchrone + timeout dur (�vite le deadlock de ReadToEnd() synchrone)
                var outTask = p.StandardOutput.ReadToEndAsync();

                if (!p.WaitForExit(3000))
                {
                    try { p.Kill(true); } catch { }
                    return false;
                }

                string output = SafeResult(outTask);

                // Cherche l'IP exacte (avec d�limiteurs pour �viter les sous-cha�nes)
                return Regex.IsMatch(output, $@"(^|[\s,]){Regex.Escape(ip)}([\s,]|$)",
                                     RegexOptions.IgnoreCase);
            }
            catch { return false; }
        }

        // ============================================================
        //  R�gle Web (inchang�e)
        // ============================================================
        public static void UpdateFirewallRule(int port)
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{WebRuleName}\"");
            ExecuteNetsh($"advfirewall firewall add rule name=\"{WebRuleName}\" dir=in action=allow protocol=TCP localport={port}");
            CoreLog.Info($"Firewall : r�gle Web mise � jour pour port {port}");
        }

        public static void RemoveRule()
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{WebRuleName}\"");
            CoreLog.Info("Firewall : r�gle Web supprim�e");
        }

        // ============================================================
        //  Ex�cution netsh
        // ============================================================
        private static bool ExecuteNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.Default,
                    StandardErrorEncoding  = System.Text.Encoding.Default
                };

                using var p = Process.Start(psi);
                if (p is null) return false;

                // Lire stdout ET stderr en parall�le pour �viter le deadlock
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();

                // ?? Timeout dur : jamais de WaitForExit() infini
                if (!p.WaitForExit(NETSH_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    CoreLog.Warn($"netsh '{args}' : timeout ({NETSH_TIMEOUT_MS} ms)");
                    return false;
                }

                // Lecture s�re (ne bloque pas m�me si les t�ches async tardent)
                string stdout = SafeResult(stdoutTask);
                string stderr = SafeResult(stderrTask);

                if (p.ExitCode != 0)
                {
                    CoreLog.Warn($"netsh '{args}' exit={p.ExitCode} : {stderr.Trim()}");
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

        // ============================================================
        //  Utilitaire : lecture s�re d'une Task<string> d�j� termin�e
        // ============================================================
        private static string SafeResult(Task<string> t)
        {
            try { return t.Wait(1000) ? (t.Result ?? "") : ""; }
            catch { return ""; }
        }
    }
}