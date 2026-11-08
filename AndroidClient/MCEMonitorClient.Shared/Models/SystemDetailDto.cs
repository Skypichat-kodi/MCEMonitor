using System;
using System.Collections.Generic;

namespace MCEMonitorClient.Shared.Models
{
    public class SystemDetailDto
    {
        public string Service { get; set; } = "";
        public string Machine { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string WorstSeverity { get; set; } = "ok";
        public SystemInfoDto System { get; set; } = new();
        public List<BsodDto> Bsods { get; set; } = new();
        public List<AlertDto> Alerts { get; set; } = new();
    }

    public class SystemInfoDto
    {
        public DateTime LastUpdate { get; set; }
        public CpuDto Cpu { get; set; } = new();
        public RamDto Ram { get; set; } = new();
        public List<GpuDto> Gpus { get; set; } = new();
        public List<NetworkDto> Networks { get; set; } = new();
        public List<HistoryPointDto> History { get; set; } = new();
    }

    public class CpuDto
    {
        public string Name { get; set; } = "";
        public double UsagePercent { get; set; }
        public double? Temperature { get; set; }
        public double? FrequencyMHz { get; set; }
        public double? MaxFrequencyMHz { get; set; }
        public List<CpuCoreDto> Cores { get; set; } = new();
    }

    public class CpuCoreDto
    {
        public string Name { get; set; } = "";
        public double UsagePercent { get; set; }
    }

    public class RamDto
    {
        public double TotalGB { get; set; }
        public double UsedGB { get; set; }
        public double FreeGB { get; set; }
        public double UsagePercent { get; set; }
    }

    public class GpuDto
    {
        public string Name { get; set; } = "";
        public double UsagePercent { get; set; }
        public double? Temperature { get; set; }
        public double? VramUsedMB { get; set; }
        public double? VramTotalMB { get; set; }
    }

    public class NetworkDto
    {
        public string Name { get; set; } = "";
        public double DownloadKBps { get; set; }
        public double UploadKBps { get; set; }
    }

    public class HistoryPointDto
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double RamUsage { get; set; }
        public double? CpuTemp { get; set; }
        public double? GpuUsage { get; set; }
        public double? GpuTemp { get; set; }
    }

    public class BsodDto
    {
        public DateTime Timestamp { get; set; }
        public string BugCheckCode { get; set; } = "";
        public string BugCheckName { get; set; } = "";
        public string FaultyModule { get; set; } = "";
        public bool DumpExists { get; set; }
    }

    public class AlertDto
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Target { get; set; } = "";
        public string Message { get; set; } = "";
        public bool EmailSent { get; set; }
    }
}