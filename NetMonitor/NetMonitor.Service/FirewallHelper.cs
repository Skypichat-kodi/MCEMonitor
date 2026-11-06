using System;
using System.IO;
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

        private const int NETSH_TIMEOUT_MS = 15000;   // netsh peut étre lent (règles nombreuses, GPOé)

        // Verrou pour éviter les race conditions sur SyncFirewallRules
        private static readonly object _syncLock = new();

        // ============================================================
        //  Plages d'IP critiques (é ne JAMAIS bloquer)
        // ============================================================
        // Plages qui IDENTIFIENT un candidat Microsoft (heuristique large, sera raffinée par le JSON)
        private static readonly string[] MicrosoftNetworks = new[]
        {
            "13.107.0.0/16",
            "20.0.0.0/8",       // ← large, but volontaire : le JSON filtrera les vrais MS
            "40.0.0.0/8",
            "52.0.0.0/8",
            "204.79.197.0/24",
            "65.52.0.0/14",
            "157.56.0.0/14"
        };

        // Plages critiques NON-Microsoft (Google, Cloudflare, privé)
        private static readonly string[] OtherCriticalNetworks = new[]
        {
            "127.0.0.0/8", 
            "10.0.0.0/8", 
            "172.16.0.0/12", 
            "192.168.0.0/16", 
            "169.254.0.0/16",
            "142.250.0.0/15", 
            "172.217.0.0/16", 
            "216.58.192.0/19", 
            "74.125.0.0/16", 
            "173.194.0.0/16",
            "104.16.0.0/12", 
            "172.64.0.0/13", 
            "1.1.1.0/24", 
            "1.0.0.0/24"
        };

        /// <summary>
        /// L'IP ressemble-t-elle à Microsoft (via plages hardcodées) ?
        /// Utilisé pour déclencher la vérification JSON. Ne retourne PAS true pour
        /// Google/Cloudflare/privé.
        /// </summary>
        public static bool IsMicrosoftLike(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return false;
            foreach (var cidr in MicrosoftNetworks)
                if (IsInCidr(addr, cidr)) return true;
            return false;
        }

        /// <summary>
        /// L'IP est-elle critique (Microsoft + Google + Cloudflare + privé) ?
        /// Comportement inchangé côté API, mais basé sur deux tableaux séparés.
        /// </summary>
        public static bool IsCriticalIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return false;

            foreach (var cidr in MicrosoftNetworks)
                if (IsInCidr(addr, cidr)) return true;

            foreach (var cidr in OtherCriticalNetworks)
                if (IsInCidr(addr, cidr)) return true;

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
        /// liste compléte des IPs bloquées. Répartit les IPs en blocs de 200.
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
        //  Suppression de toutes les règles NetMonitor-Block-* du pare-feu
        //  Utilise PowerShell (Get-NetFirewallRule) car netsh est limité
        //  à 4096 caractères de sortie → les règles en fin de liste
        //  ne sont pas détectées quand il y en a beaucoup.
        // ============================================================
        public static void DeleteAllBlockRules()
        {
            try
            {
                // 1. Listing via PowerShell (fiable même avec beaucoup de règles)
                var psScript = @"
                    Get-NetFirewallRule -DisplayName 'NetMonitor-Block-*' -ErrorAction SilentlyContinue |
                    Select-Object -ExpandProperty DisplayName
                ";

                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    CoreLog.Warn("DeleteAllBlockRules : impossible de lancer PowerShell, fallback netsh");
                    DeleteAllBlockRulesNetsh();
                    return;
                }

                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(NETSH_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    CoreLog.Warn($"DeleteAllBlockRules : timeout PowerShell ({NETSH_TIMEOUT_MS} ms)");
                    return;
                }

                string output = SafeResult(outTask);
                string error  = SafeResult(errTask);

                if (!string.IsNullOrWhiteSpace(error))
                    CoreLog.Debug($"DeleteAllBlockRules PowerShell stderr : {error}");

                // 2. Parse : un nom de règle par ligne
                var ruleNames = output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.StartsWith("NetMonitor-Block-", StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .ToList();

                CoreLog.Info($"DeleteAllBlockRules : {ruleNames.Count} règle(s) trouvée(s) via PowerShell");

                if (ruleNames.Count == 0) return;

                // 3. Suppression via netsh (rapide pour chaque règle)
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
        //  Fallback : ancienne méthode via netsh (limitée à 4096 caractères)
        //  Utilisée uniquement si PowerShell est indisponible
        // ============================================================
        private static void DeleteAllBlockRulesNetsh()
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "advfirewall firewall show rule name=all")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = System.Text.Encoding.Default,
                };

                using var p = Process.Start(psi);
                if (p == null) return;

                var outputBuilder = new System.Text.StringBuilder();
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) outputBuilder.AppendLine(e.Data);
                };
                p.BeginOutputReadLine();

                if (!p.WaitForExit(NETSH_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    CoreLog.Warn($"DeleteAllBlockRulesNetsh : timeout ({NETSH_TIMEOUT_MS} ms)");
                    return;
                }

                p.WaitForExit();

                string output = outputBuilder.ToString();
                var regex = new Regex(@"(NetMonitor-Block-[\w\.\-]+)", RegexOptions.IgnoreCase);
                var ruleNames = regex.Matches(output)
                                     .Select(m => m.Groups[1].Value)
                                     .Where(n => !string.IsNullOrWhiteSpace(n))
                                     .Distinct()
                                     .ToList();

                CoreLog.Info($"DeleteAllBlockRulesNetsh : {ruleNames.Count} règle(s) trouvée(s)");

                int deleted = 0;
                foreach (var ruleName in ruleNames)
                {
                    if (ExecuteNetsh($"advfirewall firewall delete rule name=\"{ruleName}\""))
                        deleted++;
                }

                CoreLog.Info($"Firewall : {deleted}/{ruleNames.Count} ancienne(s) règle(s) supprimée(s) (fallback)");
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"DeleteAllBlockRulesNetsh : {ex.Message}");
            }
        }

        // ============================================================
        //  Vérifie si une IP est présente dans AU MOINS une règle
        //  (utilisé pour diagnostic uniquement é pas critique)
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

                // Lecture asynchrone + timeout dur (évite le deadlock de ReadToEnd() synchrone)
                var outTask = p.StandardOutput.ReadToEndAsync();

                if (!p.WaitForExit(3000))
                {
                    try { p.Kill(true); } catch { }
                    return false;
                }

                string output = SafeResult(outTask);

                // Cherche l'IP exacte (avec délimiteurs pour éviter les sous-chaînes)
                return Regex.IsMatch(output, $@"(^|[\s,]){Regex.Escape(ip)}([\s,]|$)",
                                     RegexOptions.IgnoreCase);
            }
            catch { return false; }
        }

        // ============================================================
        //  règle Web (inchangée)
        // ============================================================
        public static void UpdateFirewallRule(int port)
        {
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{WebRuleName}\"");
            ExecuteNetsh($"advfirewall firewall add rule name=\"{WebRuleName}\" dir=in action=allow protocol=TCP localport={port}");
            CoreLog.Info($"Firewall : règle Web mise é jour pour port {port}");
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
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.Default,
                    StandardErrorEncoding  = System.Text.Encoding.Default
                };

                using var p = Process.Start(psi);
                if (p is null) return false;

                // Lire stdout ET stderr en paralléle pour éviter le deadlock
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();

                // ?? Timeout dur : jamais de WaitForExit() infini
                if (!p.WaitForExit(NETSH_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    CoreLog.Warn($"netsh '{args}' : timeout ({NETSH_TIMEOUT_MS} ms)");
                    return false;
                }

                // Lecture sére (ne bloque pas méme si les tâches async tardent)
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
        //  Utilitaire : lecture sûre d'une Task<string> déjà terminée
        // ============================================================
        private static string SafeResult(Task<string> t)
        {
            try { return t.Wait(1000) ? (t.Result ?? "") : ""; }
            catch { return ""; }
        }
        
        // ============================================================
        //  Blocage de processus (règle firewall par programme)
        // ============================================================
        private const string ProcessBlockPrefix = "NetMonitor-Block-Process-";

        public static bool BlockProcess(string processPath, out string errorMessage)
        {
            errorMessage = "";
            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                errorMessage = $"Fichier introuvable : {processPath}";
                return false;
            }

            // Nom de règle déterministe basé sur le hash du chemin (stable, unique)
            string hash = ComputePathHash(processPath);
            string ruleName = $"{ProcessBlockPrefix}{hash}";

            // Supprime une éventuelle ancienne règle portant le même nom
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");

            bool ok = ExecuteNetsh(
                $"advfirewall firewall add rule name=\"{ruleName}\" " +
                $"dir=out action=block program=\"{processPath}\" enable=yes profile=any");

            if (ok)
                CoreLog.Info($"Firewall : processus bloqué ({processPath})");
            else
                errorMessage = "Échec de la création de la règle pare-feu (netsh).";

            return ok;
        }

        public static bool UnblockProcess(string processPath)
        {
            if (string.IsNullOrWhiteSpace(processPath)) return false;

            string hash = ComputePathHash(processPath);
            string ruleName = $"{ProcessBlockPrefix}{hash}";
            bool ok = ExecuteNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");

            if (ok)
                CoreLog.Info($"Firewall : règle de blocage supprimée ({processPath})");

            return ok;
        }

        private static string ComputePathHash(string path)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(
                path.ToLowerInvariant().Replace('/', '\\').TrimEnd('\\')));
            // 8 premiers octets = 16 caractères hex, largement suffisant
            return Convert.ToHexString(hash).Substring(0, 16);
        }        
    }
}