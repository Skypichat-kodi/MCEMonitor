using System;
using System.Collections.Generic;

namespace RomMonitor.Service.Api
{
    public class ApiSummary
    {
        public string service { get; set; } = "";
        public string machine { get; set; } = "";
        public string status { get; set; } = "ok";
        public string worstSeverity { get; set; } = "ok";
        public DateTime timestamp { get; set; } = DateTime.Now;
        public List<ApiProblem> problems { get; set; } = new();
        public List<ApiMedia> media { get; set; } = new();
    }

    public class ApiProblem
    {
        public string severity { get; set; } = "";
        public string category { get; set; } = "";
        public string message { get; set; } = "";
    }

    public class ApiMedia
    {
        public string client { get; set; } = "";
        public string type { get; set; } = "";
        public string title { get; set; } = "";
        public int saison { get; set; }
        public int episode { get; set; }
    }
}