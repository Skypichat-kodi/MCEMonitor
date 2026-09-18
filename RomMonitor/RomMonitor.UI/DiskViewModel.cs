using System.Windows.Media;

namespace RomMonitor.UI
{
    public class DiskViewModel
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public string DriveType { get; set; } = "";
        public double TotalGo { get; set; }
        public double FreeGo { get; set; }
        public double FreePercent { get; set; }

        // Calculs
        public double UsedPercent => 100 - FreePercent;

        public string TotalGoText => $"{TotalGo:F1} Go";
        public string FreeGoText => $"{FreeGo:F1} Go";
        public string FreePercentText => $"{FreePercent:F1} %";

        // Brushes (mises à jour par UpdateBrushes)
        public Brush StatusBrush { get; set; } = new SolidColorBrush(Color.FromRgb(76, 194, 255));
        public Brush SmartBadgeBrush { get; set; } = new SolidColorBrush(Color.FromRgb(120, 120, 120));

        // SMART
        public string SmartModel { get; set; } = "";
        public string SmartSerial { get; set; } = "";
        public string SmartStatus { get; set; } = "N/A";
        public string SmartReason { get; set; } = "";
        public int? SmartTemperature { get; set; }
        public int? SmartPowerOnHours { get; set; }
        public bool SmartAvailable { get; set; }

        public string SmartStatusText => SmartAvailable ? SmartStatus : "N/A";

        // ------------------------------------------------------------
        //  Mise à jour des couleurs
        // ------------------------------------------------------------
        public void UpdateBrushes()
        {
            // Couleur de la barre espace
            StatusBrush = FreePercent switch
            {
                < 5 => new SolidColorBrush(Color.FromRgb(255, 99, 71)),    // rouge
                < 15 => new SolidColorBrush(Color.FromRgb(255, 185, 0)),   // orange
                _ => new SolidColorBrush(Color.FromRgb(108, 203, 95))      // vert
            };

            // Badge SMART
            SmartBadgeBrush = SmartStatus switch
            {
                "OK" => new SolidColorBrush(Color.FromRgb(108, 203, 95)),
                "Warning" => new SolidColorBrush(Color.FromRgb(255, 185, 0)),
                "Critical" => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
                _ => new SolidColorBrush(Color.FromRgb(120, 120, 120))
            };
        }
    }
}