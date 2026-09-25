using System;
using System.Collections.Generic;

namespace SystemMonitor.Service
{
    public class SystemSnapshot
    {
        public DateTime Timestamp { get; set; }
        public CpuInfo Cpu { get; set; } = new();
        public List<GpuInfo> Gpus { get; set; } = new();
        public RamInfo Ram { get; set; } = new();
        public List<NetworkInfo> Networks { get; set; } = new();
    }

    public class CpuInfo
    {
        public string Name { get; set; } = "";
        public double UsagePercent { get; set; }
        public double? Temperature { get; set; }
        public double? FrequencyMHz { get; set; }
        public double? MaxFrequencyMHz { get; set; }
        public List<CpuCoreInfo> Cores { get; set; } = new();
    }

    public class CpuCoreInfo
    {
        public string Name { get; set; } = "";    // "Core #1", "Core #2", ...
        public double UsagePercent { get; set; }
    }

    public class GpuInfo
    {
        public string Name { get; set; } = "";
        public double UsagePercent { get; set; }
        public double? Temperature { get; set; }
        public double? VramUsedMB { get; set; }
        public double? VramTotalMB { get; set; }
    }

    public class RamInfo
    {
        public double TotalGB { get; set; }
        public double UsedGB { get; set; }
        public double FreeGB { get; set; }
        public double UsagePercent { get; set; }
    }

    public class NetworkInfo
    {
        public string Name { get; set; } = "";
        public double DownloadKBps { get; set; }
        public double UploadKBps { get; set; }
    }
}