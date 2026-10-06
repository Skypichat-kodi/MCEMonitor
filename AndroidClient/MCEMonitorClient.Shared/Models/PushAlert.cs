using System;
using System.Collections.Generic;

namespace MCEMonitorClient.Shared.Models
{
    public class PushAlert
    {
        public string Type { get; set; } = "";
        public string ServerId { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string ServiceType { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Status { get; set; } = "ok";
        public string PreviousStatus { get; set; } = "unknown";
        public int ProblemCount { get; set; }
        public string FirstProblem { get; set; } = "";
        public List<ProblemItem> Problems { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public class PushMedia
    {
        public string Type { get; set; } = "";
        public string EventType { get; set; } = "";    // "started" / "stopped"
        public string ServerId { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string ServiceType { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Client { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string Title { get; set; } = "";
        public int Saison { get; set; }
        public int Episode { get; set; }
        public DateTime Timestamp { get; set; }
    }
}