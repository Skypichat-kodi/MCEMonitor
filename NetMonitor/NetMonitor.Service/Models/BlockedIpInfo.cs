using System;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Models
{
    public class BlockedIpInfo
    {
        public string Ip { get; set; } = "";
        public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
        public string Severity { get; set; } = "-";
        public bool IsProcess { get; set; }
        public string? ProcessPath { get; set; }

        // ============================================================
        //  Informations GeoIP — résolues au moment du blocage
        // ============================================================
        public string? Country { get; set; }
        public string? CountryCode { get; set; }
        public string? City { get; set; }
        public string? Isp { get; set; }
        public string? Org { get; set; }
        public bool IsPrivate { get; set; }
        public string? ReverseDns { get; set; }
        public DateTime? GeoResolvedAt { get; set; }

        // ============================================================
        //  Override manuel de la sévérité
        //  Si défini, écrase toute évaluation automatique (SeverityScorer).
        //  ⚠ Perdu au déblocage : si l'IP revient et est re-bloquée, elle
        //  sera ré-évaluée automatiquement.
        // ============================================================
        public Severity? ManualSeverity { get; set; }
        public DateTime? ManualSeveritySetAt { get; set; }
        public bool IsKnownScanner { get; set; }
    }
}