using System.Collections.Generic;

namespace RomMonitor.Service
{
    /// <summary>
    /// Informations SMART d'un disque physique.
    /// </summary>
    public class SmartInfo
    {
        // Identité
        public string Device { get; set; } = "";         // /dev/sda
        public string Type { get; set; } = "";           // "nvme", "ata", "scsi"
        public string Model { get; set; } = "";
        public string Serial { get; set; } = "";
        public string Firmware { get; set; } = "";

        // État global
        public bool Available { get; set; }
        public bool Passed { get; set; }

        // Commun
        public int? Temperature { get; set; }
        public int? PowerOnHours { get; set; }
        public long? MediaErrors { get; set; }

        // Spécifique NVMe
        public int? CriticalWarning { get; set; }
        public int? PercentageUsed { get; set; }
        public int? AvailableSpare { get; set; }

        // Spécifique ATA/HDD
        public long? ReallocatedSectors { get; set; }
        public long? PendingSectors { get; set; }
        public long? UncorrectableSectors { get; set; }
        public long? SpinRetryCount { get; set; }

        // Statut calculé
        public string Status { get; set; } = "N/A";        // "OK", "Warning", "Critical", "N/A"
        public string StatusReason { get; set; } = "";

        // Détails supplémentaires (pour affichage/debug)
        public Dictionary<string, string> Details { get; set; } = new();
    }
}