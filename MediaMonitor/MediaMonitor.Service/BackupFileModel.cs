using System;
using System.Collections.Generic;
using MediaMonitor.Core.Models;

public class BackupFileModel
{
    public int RetentionDays { get; set; }
    public List<DailyReport> Reports { get; set; }
}

public class DailyReport
{
    public DateTime Date { get; set; }
    public List<MediaUsageItem> Items { get; set; }
}

