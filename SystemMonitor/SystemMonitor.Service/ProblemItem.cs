using System;

namespace SystemMonitor.Service
{
    public class ProblemItem
    {
        public DateTime Timestamp { get; set; }
        public string Severity { get; set; } = "";     // "critical" / "warning"
        public string Category { get; set; } = "";     // "CPU", "RAM", "Temp", "BSOD"
        public string Message { get; set; } = "";
    }
}