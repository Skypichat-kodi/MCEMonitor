using System;
using System.Linq;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Services
{
    /// <summary>
    /// Évalue la sévérité d'une IP en fonction de son reverse DNS, de son ISP
    /// et des patterns configurés dans NetMonitorSettings.
    ///
    /// ⚠ Cette sévérité est PUREMENT COSMÉTIQUE : elle n'interfère JAMAIS
    /// dans la décision de bloquer. Toute IP qui déclenche une alerte est
    /// bloquée si AutoBlock est activé, indépendamment de son niveau.
    ///
    /// Ordre de priorité (du plus fort au plus faible) :
    ///   1. ManualSeverity (override utilisateur, géré dans BlockedIpInfo)
    ///   2. WhitelistPatterns → Whitelisted (vert foncé)
    ///   3. CriticalPatterns  → Critical    (rouge)
    ///   4. ScannerPatterns   → Trusted     (vert moyen)
    ///   5. SafePatterns      → Safe        (vert clair)
    ///   6. Aucun match       → Warning     (orange, défaut)
    /// </summary>
    public static class SeverityScorer
    {
        public static Severity Evaluate(
            string? ip,
            string? reverseDns,
            string? isp,
            string? org,
            string? detector,
            NetMonitorSettings settings)
        {
            if (settings == null)
                return Severity.Warning;

            // On teste d'abord le reverse DNS (plus fiable), puis l'ISP et l'Org
            // en fallback (certains reverse DNS sont null mais l'ISP est connu).
            string[] candidates = new[]
            {
                reverseDns?.Trim().ToLowerInvariant(),
                isp?.Trim().ToLowerInvariant(),
                org?.Trim().ToLowerInvariant()
            }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray()!;

            // 1. Whitelist utilisateur (priorité max, avant tout le reste)
            if (MatchesAnyPattern(candidates, settings.WhitelistPatterns))
                return Severity.Whitelisted;

            // 2. Vraiment malveillant (bulletproof hosting, domaines connus)
            if (MatchesAnyPattern(candidates, settings.CriticalPatterns))
                return Severity.Critical;

            // 3. Scanners de recherche connus (Shodan, Censys, ONYPHE…)
            if (MatchesAnyPattern(candidates, settings.ScannerPatterns))
                return Severity.Trusted;

            // 4. Domaines légitimes connus (Microsoft, Google, etc.)
            if (MatchesAnyPattern(candidates, settings.SafePatterns))
                return Severity.Safe;

            // 5. Défaut : à vérifier (orange)
            return Severity.Warning;
        }

        /// <summary>
        /// Vérifie si un des hostnames/ISP/Org matche un des patterns.
        ///
        /// Supporte :
        ///   - Exact    : "infrawat.ch"      → infrawat.ch OU *.infrawat.ch
        ///   - Wildcard : "*.infrawat.ch"    → *.infrawat.ch uniquement
        /// </summary>
        private static bool MatchesAnyPattern(string[] candidates, string[]? patterns)
        {
            if (candidates == null || candidates.Length == 0) return false;
            if (patterns == null || patterns.Length == 0) return false;

            foreach (var candidate in candidates)
            {
                foreach (var pattern in patterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern)) continue;
                    string p = pattern.Trim().ToLowerInvariant();

                    if (p.StartsWith("*."))
                    {
                        string suffix = p.Substring(2);
                        if (candidate == suffix || candidate.EndsWith("." + suffix))
                            return true;
                    }
                    else
                    {
                        if (candidate == p || candidate.EndsWith("." + p))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Extrait le domaine racine d'un hostname (pour affichage ou logs).
        /// Ex : "31-14-254-18.infrawat.ch" → "infrawat.ch"
        ///      "a.b.co.uk"                → "co.uk" (simple, pas de PSL)
        /// </summary>
        public static string? ExtractRootDomain(string? hostname)
        {
            if (string.IsNullOrWhiteSpace(hostname)) return null;
            string host = hostname.Trim().ToLowerInvariant();
            var parts = host.Split('.');
            if (parts.Length <= 2) return host;
            return string.Join('.', parts.Skip(parts.Length - 2));
        }
    }
}