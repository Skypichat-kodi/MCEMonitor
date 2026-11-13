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
            Severity detectorSeverity,
            NetMonitorSettings settings,
            GoogleIpRangeService? googleRanges = null)
        {
            if (settings == null) return detectorSeverity;

            string[] candidates = new[]
            {
                reverseDns?.Trim().ToLowerInvariant(),
                isp?.Trim().ToLowerInvariant(),
                org?.Trim().ToLowerInvariant()
            }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray()!;

            // 1. Whitelist utilisateur (priorité absolue)
            if (MatchesAnyPattern(candidates, settings.WhitelistPatterns))
                return Severity.Whitelisted;

            // 1bis. Si un détecteur a explicitement demandé une vérification manuelle
            if (detectorSeverity == Severity.ToCheck)
                return Severity.ToCheck;

            // 2. Robots officiels (Google/Bing) → Bot
            if (googleRanges?.IsEnabled == true
                && !string.IsNullOrWhiteSpace(ip)
                && googleRanges.IsGoogleIp(ip))
                return Severity.Bot;

            // 3. Robots génériques (Yandex, Baidu...) → Crawler
            if (MatchesAnyPattern(candidates, settings.CrawlerPatterns))
                return Severity.Crawler;

            // 4. Safe (Microsoft, Google pattern...)
            if (MatchesAnyPattern(candidates, settings.SafePatterns))
                return Severity.Safe;

            // ═══════════════════════════════════════════════════════════════
            // 5. NOUVEAU : Scanners (Palo Alto, Shodan, Censys...) → Trusted
            //    Doit passer AVANT le Critical du détecteur Blacklist
            // ═══════════════════════════════════════════════════════════════
            if (MatchesAnyPattern(candidates, settings.ScannerPatterns))
                return Severity.Trusted;

            // 6. Domaines malveillants connus → Critical
            if (MatchesAnyPattern(candidates, settings.CriticalPatterns))
                return Severity.Critical;

            // 7. Détecteur critique → conserver (seulement si pas scanner/bot)
            if (detectorSeverity == Severity.Critical || detectorSeverity == Severity.High)
                return detectorSeverity;

            // 8. Défaut
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