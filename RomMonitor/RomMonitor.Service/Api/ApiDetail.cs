using System;
using System.Collections.Generic;

namespace RomMonitor.Service.Api
{
    public class ApiDetail
    {
        public string service { get; set; } = "";
        public string machine { get; set; } = "";
        public DateTime timestamp { get; set; } = DateTime.Now;
        public string worstSeverity { get; set; } = "ok";
        public DateTime lastCheckTime { get; set; }
        public List<ApiProblem> problems { get; set; } = new();
        public List<DiskDetailDto> disks { get; set; } = new();
        public List<SmartDetailDto> smart { get; set; } = new();
    }

    public class DiskDetailDto
    {
        public string name { get; set; } = "";
        public string label { get; set; } = "";
        public string driveType { get; set; } = "";

        public double totalGo { get; set; }
        public double freeGo { get; set; }
        public double freePercent { get; set; }

        public int? physicalDiskNumber { get; set; }
        public string physicalSerial { get; set; } = "";
        public double physicalSizeGo { get; set; }

        public bool smartAvailable { get; set; }
        public bool smartPredictFailure { get; set; }
        public string smartStatus { get; set; } = "";
        public string model { get; set; } = "";
        public string serialNumber { get; set; } = "";
    }

    public class SmartDetailDto
    {
        public string device { get; set; } = "";
        public string type { get; set; } = "";
        public string model { get; set; } = "";
        public string serial { get; set; } = "";
        public string firmware { get; set; } = "";

        public double capacityGo { get; set; }

        public bool available { get; set; }
        public bool passed { get; set; }

        public int? temperature { get; set; }
        public int? powerOnHours { get; set; }
        public long? mediaErrors { get; set; }

        public int? criticalWarning { get; set; }
        public int? percentageUsed { get; set; }
        public int? availableSpare { get; set; }

        public long? reallocatedSectors { get; set; }
        public long? pendingSectors { get; set; }
        public long? uncorrectableSectors { get; set; }
        public long? spinRetryCount { get; set; }

        public string status { get; set; } = "N/A";
        public string statusReason { get; set; } = "";
    }
}