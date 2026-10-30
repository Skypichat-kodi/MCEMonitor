using System;

namespace NetMonitor.Service.Models
{
    public sealed class AuditGroupedEntry
    {
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int Count { get; set; }

        public Severity Severity { get; set; }
        public string Source { get; set; } = "";
        public string? RemoteIp { get; set; }
        public int RemotePort { get; set; }
        public int LocalPort { get; set; }
        public int Pid { get; set; }
        public string? ProcessName { get; set; }
        public string? ProcessPath { get; set; }
        public string? TrustReason { get; set; }
        public string? Details { get; set; }
        public bool IsKnownScanner { get; set; }
    }
}