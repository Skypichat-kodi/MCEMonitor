using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace NetMonitor.Service
{
    public static class FirewallHelper
    {
        private const string WebRuleName = "NetMonitorWebPort";
        private const string BlockPrefix = "NetMonitor-Block-";
        private const int IpsPerRule = 200;   // Limite conservative (netsh ~4096 chars)

        // Verrou pour éviter les race conditions sur SyncFirewallRules
        private static readonly object _syncLock = new();

        // ============================================================
        //  Plages d'IP critiques (à ne JAMAIS bloquer)
        // ============================================================
        private static readonly string[] CriticalNetworks = new[]
        {
            // Loopback & privé (réseau local)
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
        //  Synchronisation des règles de blocage (groupées par blocs)
        // ============================================================
        /// <summary>
        /// Reconstruit TOUTES les règles de blocage NetMonitor à partir de la
        /// liste complète des IPs bloquées. Répartit les IPs en blocs de 200.
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

                    // 1. Supprimer toutes les anciennes règles NetMonitor-Block-*
                    DeleteAllBlockRules();

                    if (ips.Count == 0)
                    {
                        CoreLog.Info("Firewall : aucune IP bloquée, toutes les règles ont été supprimées");
                        return true;
                    }

                    // 2. Recréer les règles par blocs de 200 IPs
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
                            CoreLog.Info($"Firewall : bloc {suffix} créé ({batch.Count} IPs)");
                        }
                        else
                        {
                            CoreLog.Warn($"Firewall : échec création bloc {suffix} " +
                                         $"(in={okIn}, out={okOut}, {batch.Count} IPs)");
                        }
                    }

                    CoreLog.Info($"Firewall : synchronisation terminée ({successCount}/{blockCount} bloc(s), {ips.Count} IPs)");
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
        //  Suppression de toutes les règles NetMonitor-Block-*
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

                // Lire stdout AVANT WaitForExit pour éviter les deadlocks
                var outputBuilder = new System.Text.StringBuilder();
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) outputBuilder.AppendLine(e.Data);
                };
                p.BeginOutputReadLine();
                p.WaitForExit();

                string output = outputBuilder.ToString();
                CoreLog.Debug($"DeleteAllBlockRules : sortie netsh = {output.Length} caractères");

                // Cherche tous les noms de règles NetMonitor-Block-*
                var regex = new Regex(@"(NetMonitor-Block-[\w\.\-]+)", RegexOptions.IgnoreCase);
                var ruleNames = regex.Matches(output)
                                     .Select(m => m.Groups[1].Value)
                                     .Where(n => !string.IsNullOrWhiteSpace(n))
                                     .Distinct()
                                     .ToList();

                CoreLog.Info($"DeleteAllBlockRules : {ruleNames.Count} règle(s) trouvée(s) à supprimer");

                int deleted = 0;
                foreach (var ruleName in ruleNames)
                {
                    if (ExecuteNetsh($"advfirewall firewall delete rule name=\"{ruleName}\""))
                        deleted++;
                    else
                        CoreLog.Warn($"DeleteAllBlockRules : échec suppression '{ruleName}'");
                }

                CoreLog.Info($"Firewall : {deleted}/{ruleNames.Count} ancienne(s) règle(s) supprimée(s)");
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"DeleteAllBlockRules : {ex.Message}");
            }
        }

        // ============================================================
        //  Vérifie si une IP est présente dans AU MOINS une règle
        //  (utilisé pour diagnostic uniquement — pas critique)
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

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);

                // Cherche l'IP exacte (avec délimiteurs pour éviter les sous-chaînes)
                return Regex.IsMatch(output, $@"(^|[\s,]){Regex.Escape(ip)}([\s,]|$)",
                                     RegexOptions.IgnoreCase);
            }
            catch { return false; }
        }

        // ============================================================
        //  Règle Web (inchangée)
        // ============================================================
        public static void UpdateFirewallRule(int port)
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{WebRuleName}\"");
            ExecuteNetsh($"advfirewall firewall add rule name=\"{WebRuleName}\" dir=in action=allow protocol=TCP localport={port}");
            CoreLog.Info($"Firewall : règle Web mise à jour pour port {port}");
        }

        public static void RemoveRule()
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{WebRuleName}\"");
            CoreLog.Info("Firewall : règle Web supprimée");
        }

        // ============================================================
        //  Exécution netsh
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
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p is null) return false;

                // Lire stdout ET stderr en parallèle pour éviter le deadlock
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();
                p.WaitForExit();

                string stdout = stdoutTask.Result;
                string stderr = stderrTask.Result;

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
    }
}