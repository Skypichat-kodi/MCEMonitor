using System;
using System.Collections.Generic;
using MediaMonitor.Core.Models;

namespace MediaMonitor.UI.Models
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

    public class BackupFileEntry
    {
        public string Path    { get; set; } = "";
        public string Display { get; set; } = "";
        public DateTime Date  { get; set; }
    }

    public class StatRow
    {
        public string Label { get; set; } = "";
        public int Count    { get; set; }
    }

    public class ClientStatRow
    {
        public string Client { get; set; } = "";
        public int Audio { get; set; }
        public int Serie { get; set; }
        public int Video { get; set; }
        public int Image { get; set; }
        public int Rec   { get; set; }
        public int Tv    { get; set; }
        public int Total => Audio + Serie + Video + Image + Rec + Tv;
    }
}