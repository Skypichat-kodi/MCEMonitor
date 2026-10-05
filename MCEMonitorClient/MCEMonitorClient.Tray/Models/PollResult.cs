using System;

namespace MCEMonitorClient.Tray.Models
{
    public class PollResult
    {
        public string ServerId { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string ServiceType { get; set; } = "";
        public string BaseUrl { get; set; } = "";

        public bool Online { get; set; }
        public string Status { get; set; } = "ok";
        public string WorstSeverity { get; set; } = "ok";
        public int ProblemCount { get; set; }
        public string FirstProblem { get; set; } = "";

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}