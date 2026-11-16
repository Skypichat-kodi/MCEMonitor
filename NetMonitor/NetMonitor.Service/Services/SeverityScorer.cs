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
        /// <summary>
        /// Évalue la sévérité ET l'origine d'une IP en un seul passage.
        /// Ordre de priorité identique pour les deux axes :
        ///   1. ManualSeverity (géré en amont, dans BlockedIpInfo)
        ///   2. WhitelistPatterns → Whitelisted / Whitelist
        ///   3. ToCheck demandé explicitement par un détecteur
        ///   4. Google officiel  → Bot / Bot
        ///   5. CrawlerPatterns  → Crawler / Crawler
        ///   6. SafePatterns     → Safe / Unknown (ou Microsoft si range)
        ///   7. ScannerPatterns  → Trusted / Scanner
        ///   8. CriticalPatterns → Critical / Blacklist (ou Unknown)
        ///   9. Détecteur critique → conserve
        ///  10. Défaut            → Warning / Unknown
        /// </summary>
        public static Severity Evaluate(
            string? ip,
            string? reverseDns,
            string? isp,
            string? org,
            string? detector,
            Severity detectorSeverity,
            NetMonitorSettings settings,
            GoogleIpRangeService? googleRanges,
            MicrosoftIpRangeService? microsoftRanges,
            ThreatIntelService? threatIntel,
            out IpOrigin origin)
        {
            origin = IpOrigin.Unknown;

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
            {
                origin = IpOrigin.Whitelist;
                return Severity.Whitelisted;
            }

            // 2. ToCheck explicite
            if (detectorSeverity == Severity.ToCheck)
                return Severity.ToCheck;   // origin reste Unknown (ToCheck n'est pas une origine)

            // 3. Microsoft officiel (liste) → Safe + Origin Microsoft
            if (microsoftRanges?.IsEnabled == true
                && !string.IsNullOrWhiteSpace(ip)
                && microsoftRanges.IsMicrosoftIp(ip))
            {
                origin = IpOrigin.Microsoft;
                return Severity.Safe;
            }

            // 4. Google officiel (liste) → Bot + Origin Bot
            if (googleRanges?.IsEnabled == true
                && !string.IsNullOrWhiteSpace(ip)
                && googleRanges.IsGoogleIp(ip))
            {
                origin = IpOrigin.Bot;
                return Severity.Bot;
            }

            // 5. Crawler (patterns reverse DNS / ISP / Org)
            if (MatchesAnyPattern(candidates, settings.CrawlerPatterns))
            {
                origin = IpOrigin.Crawler;
                return Severity.Crawler;
            }

            // 6. Safe (patterns)
            if (MatchesAnyPattern(candidates, settings.SafePatterns))
            {
                origin = IpOrigin.Unknown;   // safe mais pas d'origine identifiable
                return Severity.Safe;
            }

            // 7. Scanner (patterns) → Trusted + Origin Scanner
            if (MatchesAnyPattern(candidates, settings.ScannerPatterns))
            {
                origin = IpOrigin.Scanner;
                return Severity.Trusted;
            }

            // 8. Blacklist IPsum → Critical + Origin Blacklist
            if (threatIntel != null
                && !string.IsNullOrWhiteSpace(ip)
                && threatIntel.IsBlacklisted(ip))
            {
                origin = IpOrigin.Blacklist;
                return Severity.Critical;
            }

            // 9. CriticalPatterns → Critical + Origin Unknown (patterns ≠ blacklist)
            if (MatchesAnyPattern(candidates, settings.CriticalPatterns))
                return Severity.Critical;

            // 10. Détecteur critique
            if (detectorSeverity == Severity.Critical || detectorSeverity == Severity.High)
                return detectorSeverity;

            // 11. Défaut
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