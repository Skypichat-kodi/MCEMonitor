using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;

namespace NetMonitor.Service
{
    public static class FirewallHelper
    {
        private const string WebRuleName = "NetMonitorWebPort";
        private const string BlockPrefix = "NetMonitor-Block-";

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

            // Microsoft / Azure (Windows Update, Télémétrie, CrossDevice, Office...)
            "13.107.0.0/16",
            "20.0.0.0/8",
            "40.0.0.0/8",
            "52.0.0.0/8",
            "204.79.197.0/24",
            "65.52.0.0/14",
            "157.56.0.0/14",

            // Google (Recherche, YouTube, Play...)
            "142.250.0.0/15",
            "172.217.0.0/16",
            "216.58.192.0/19",
            "74.125.0.0/16",
            "173.194.0.0/16",

            // Cloudflare (DNS, CDN)
            "104.16.0.0/12",
            "172.64.0.0/13",
            "1.1.1.0/24",
            "1.0.0.0/24"
        };

        /// <summary>
        /// Vérifie si une IP appartient à une plage critique (Microsoft, Google, Local...).
        /// </summary>
        public static bool IsCriticalIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return false;

            foreach (var cidr in CriticalNetworks)
            {
                if (IsInCidr(addr, cidr)) return true;
            }

            return false;
        }

        /// <summary>
        /// Vérifie si une adresse IP est dans un bloc CIDR donné.
        /// </summary>
        private static bool IsInCidr(IPAddress ip, string cidr)
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;

            if (!IPAddress.TryParse(parts[0], out var network)) return false;
            if (!int.TryParse(parts[1], out int prefixLength)) return false;

            var ipBytes = ip.GetAddressBytes();
            var netBytes = network.GetAddressBytes();

            if (ipBytes.Length != netBytes.Length) return false; // Mélange IPv4/IPv6

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
        //  Blocage d'IP
        // ============================================================
        public static bool BlockIp(string ip, out string errorMessage)
        {
            errorMessage = "";

            if (string.IsNullOrWhiteSpace(ip))
            {
                errorMessage = "IP vide.";
                return false;
            }

            if (IsCriticalIp(ip))
            {
                errorMessage = $"L'adresse {ip} fait partie d'une plage critique (Microsoft, Google, Cloudflare ou réseau local) et ne peut pas être bloquée.";
                CoreLog.Warn($"Blocage refusé : {ip} (IP critique)");
                return false;
            }

            string rule = BlockPrefix + ip;

            ExecuteNetsh($"advfirewall firewall delete rule name=\"{rule}\"");
            ExecuteNetsh($"advfirewall firewall delete rule name=\"{rule}-out\"");

            bool okIn = ExecuteNetsh($"advfirewall firewall add rule name=\"{rule}\" dir=in action=block remoteip={ip} enable=yes profile=any");
            bool okOut = ExecuteNetsh($"advfirewall firewall add rule name=\"{rule}-out\" dir=out action=block remoteip={ip} enable=yes profile=any");

            CoreLog.Info($"Firewall : blocage {ip} ? in={okIn} out={okOut}");

            if (!okIn || !okOut)
            {
                errorMessage = "Erreur lors de la création des règles pare-feu (netsh).";
                return false;
            }

            return true;
        }

        // Surcharge pour compatibilité
        public static bool BlockIp(string ip) => BlockIp(ip, out _);

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
                    $"advfirewall firewall show rule name=\"{BlockPrefix}{ip}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p is null) return false;

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);

                return output.Contains("Règle", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("Rule", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ============================================================
        //  Web
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

                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);

                if (p.ExitCode != 0)
                {
                    CoreLog.Warn($"netsh exit={p.ExitCode} : {stderr.Trim()}");
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