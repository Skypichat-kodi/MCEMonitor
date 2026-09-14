using System;
using System.Collections.Generic;
using MediaMonitor.Core.Models;

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
        public List<MediaUsageItem> Items { get; set; } = new();
    }
}