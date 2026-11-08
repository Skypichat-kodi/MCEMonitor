using System;
using System.Collections.Generic;

namespace MCEMonitorClient.Shared.Models
{
    public class RomDetailDto
    {
        public string Service { get; set; } = "";
        public string Machine { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string WorstSeverity { get; set; } = "ok";
        public DateTime LastCheckTime { get; set; }
        public List<RomProblemDto> Problems { get; set; } = new();
        public List<RomDiskDto> Disks { get; set; } = new();
        public List<RomSmartDto> Smart { get; set; } = new();
    }

    public class RomProblemDto
    {
        public string Severity { get; set; } = "";
        public string Category { get; set; } = "";
        public string Target { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class RomDiskDto
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public string DriveType { get; set; } = "";

        public double TotalGo { get; set; }
        public double FreeGo { get; set; }
        public double FreePercent { get; set; }

        public int? PhysicalDiskNumber { get; set; }
        public string PhysicalSerial { get; set; } = "";
        public double PhysicalSizeGo { get; set; }

        public bool SmartAvailable { get; set; }
        public bool SmartPredictFailure { get; set; }
        public string SmartStatus { get; set; } = "";
        public string Model { get; set; } = "";
        public string SerialNumber { get; set; } = "";
    }

    public class RomSmartDto
    {
        public string Device { get; set; } = "";
        public string Type { get; set; } = "";
        public string Model { get; set; } = "";
        public string Serial { get; set; } = "";
        public string Firmware { get; set; } = "";

        public double CapacityGo { get; set; }

        public bool Available { get; set; }
        public bool Passed { get; set; }

        public int? Temperature { get; set; }
        public int? PowerOnHours { get; set; }
        public long? MediaErrors { get; set; }

        public int? CriticalWarning { get; set; }
        public int? PercentageUsed { get; set; }
        public int? AvailableSpare { get; set; }

        public long? ReallocatedSectors { get; set; }
        public long? PendingSectors { get; set; }
        public long? UncorrectableSectors { get; set; }
        public long? SpinRetryCount { get; set; }

        public string Status { get; set; } = "N/A";
        public string StatusReason { get; set; } = "";
    }
}