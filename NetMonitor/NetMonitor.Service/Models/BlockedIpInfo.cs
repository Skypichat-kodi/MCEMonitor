using System;

namespace NetMonitor.Service.Models
{
    public class BlockedIpInfo
    {
        public string Ip { get; set; } = "";
        public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "";      // Détecteur
        public string Message { get; set; } = "";     // Message de l'alerte originale
        public string? Details { get; set; }
    }
}