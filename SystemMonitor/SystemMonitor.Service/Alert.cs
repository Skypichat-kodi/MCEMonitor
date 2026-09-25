using System;

namespace SystemMonitor.Service
{
    public enum AlertType
    {
        CpuHigh,
        RamHigh,
        TempHigh
    }

    public class Alert
    {
        public DateTime Timestamp { get; set; }
        public AlertType Type { get; set; }
        public string Severity { get; set; } = "";
        public string Target { get; set; } = "";
        public string Message { get; set; } = "";
        public bool EmailSent { get; set; }
    }
}