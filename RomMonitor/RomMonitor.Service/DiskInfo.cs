namespace RomMonitor.Service
{
    /// <summary>
    /// Informations sur un disque (espace + santé).
    /// </summary>
    public class DiskInfo
    {
        // Identifiant
        public string Name { get; set; } = "";           // "C:\", "D:\"
        public string Label { get; set; } = "";          // "Système", "Data"
        public string DriveType { get; set; } = "";      // "SSD", "HDD", "NVMe"

        // Espace
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }

        public double TotalGo => TotalBytes / 1024.0 / 1024.0 / 1024.0;
        public double FreeGo => FreeBytes / 1024.0 / 1024.0 / 1024.0;
        public double FreePercent => TotalBytes > 0
            ? (FreeBytes * 100.0 / TotalBytes)
            : 0;

        // Santé SMART
        public bool SmartAvailable { get; set; }
        public bool SmartPredictFailure { get; set; }
        public string SmartStatus { get; set; } = "";    // "OK", "Warning", "Critical", "N/A"

        // Modèle physique (si dispo)
        public string Model { get; set; } = "";
        public string SerialNumber { get; set; } = "";
    }
}