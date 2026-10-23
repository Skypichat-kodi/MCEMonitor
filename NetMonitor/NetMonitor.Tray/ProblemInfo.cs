using System;

namespace NetMonitor.Tray
{
    public class ProblemInfo
    {
        public DateTime Timestamp { get; set; }
        public string Severity { get; set; } = "";
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
        public int Count { get; set; } = 1; // ? NOUVEAU
    }   
    
    internal sealed class NetAlert
    {
        public DateTime timestamp { get; set; }
        public string severity { get; set; } = "";
        public string source { get; set; } = "";
        public string message { get; set; } = "";
        public string? details { get; set; }
        public string? remoteIp { get; set; }
    }   
}