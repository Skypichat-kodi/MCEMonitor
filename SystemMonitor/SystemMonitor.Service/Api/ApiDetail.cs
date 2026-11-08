using System;
using System.Collections.Generic;

namespace SystemMonitor.Service.Api
{
    public class ApiDetail
    {
        public string service { get; set; } = "";
        public string machine { get; set; } = "";
        public DateTime timestamp { get; set; } = DateTime.Now;
        public string worstSeverity { get; set; } = "ok";
        public SystemInfoDto system { get; set; } = new();
        public List<BsodDto> bsods { get; set; } = new();
        public List<AlertDto> alerts { get; set; } = new();
    }

    public class SystemInfoDto
    {
        public DateTime lastUpdate { get; set; }
        public CpuDto cpu { get; set; } = new();
        public RamDto ram { get; set; } = new();
        public List<GpuDto> gpus { get; set; } = new();
        public List<NetworkDto> networks { get; set; } = new();
        public List<HistoryPointDto> history { get; set; } = new();
    }

    public class CpuDto
    {
        public string name { get; set; } = "";
        public double usagePercent { get; set; }
        public double? temperature { get; set; }
        public double? frequencyMHz { get; set; }
        public double? maxFrequencyMHz { get; set; }
        public List<CpuCoreDto> cores { get; set; } = new();
    }

    public class CpuCoreDto
    {
        public string name { get; set; } = "";
        public double usagePercent { get; set; }
    }

    public class RamDto
    {
        public double totalGB { get; set; }
        public double usedGB { get; set; }
        public double freeGB { get; set; }
        public double usagePercent { get; set; }
    }

    public class GpuDto
    {
        public string name { get; set; } = "";
        public double usagePercent { get; set; }
        public double? temperature { get; set; }
        public double? vramUsedMB { get; set; }
        public double? vramTotalMB { get; set; }
    }

    public class NetworkDto
    {
        public string name { get; set; } = "";
        public double downloadKBps { get; set; }
        public double uploadKBps { get; set; }
    }

    public class HistoryPointDto
    {
        public DateTime timestamp { get; set; }
        public double cpuUsage { get; set; }
        public double ramUsage { get; set; }
        public double? cpuTemp { get; set; }
        public double? gpuUsage { get; set; }
        public double? gpuTemp { get; set; }
    }

    public class BsodDto
    {
        public DateTime timestamp { get; set; }
        public string bugCheckCode { get; set; } = "";
        public string bugCheckName { get; set; } = "";
        public string faultyModule { get; set; } = "";
        public bool dumpExists { get; set; }
    }

    public class AlertDto
    {
        public DateTime timestamp { get; set; }
        public string type { get; set; } = "";
        public string severity { get; set; } = "";
        public string target { get; set; } = "";
        public string message { get; set; } = "";
        public bool emailSent { get; set; }
    }
}