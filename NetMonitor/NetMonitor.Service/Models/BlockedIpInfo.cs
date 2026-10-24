using System;

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
    }
}