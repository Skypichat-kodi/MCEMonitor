using System;

namespace SystemMonitor.Service
{
    public class ProblemItem
    {
        public DateTime Timestamp { get; set; }
        public string Severity { get; set; } = "";     // "critical" / "warning"
        public string Category { get; set; } = "";     // "CpuHigh", "RamHigh", "TempHigh", "GpuTempHigh", "BSOD"
        public string Target   { get; set; } = "";     // "CPU", "RAM", "CPU Temp", nom GPU, code BSOD
        public string Message  { get; set; } = "";
    }
}