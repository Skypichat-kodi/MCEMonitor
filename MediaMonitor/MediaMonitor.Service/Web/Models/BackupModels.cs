using System;
using System.Collections.Generic;

namespace MediaMonitor.Service.Web.Models
{
    public class BackupFileModel
    {
        public int RetentionDays { get; set; }
        public List<DailyReport> Reports { get; set; } = new();
    }

    public class DailyReport
    {
        public DateTime Date { get; set; }
        public List<BackupItem> Items { get; set; } = new();
    }

    public class BackupItem
    {
        public string? ClientDisplay { get; set; }
        public string? MediaType { get; set; }
        public string? Nom { get; set; }
        public string? FileName { get; set; }
        public string? Path { get; set; }
        public int Saison { get; set; }
        public int Episode { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Channel { get; set; }
    }
}
