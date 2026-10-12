using System.Windows.Media;
using MCEMonitor.Languages;

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
        // Seuils chargés depuis RomMonitor.config (mis à jour par MainWindow à chaque refresh)
        public static int WarnPercent { get; set; } = 15;
        public static int CriticalPercent { get; set; } = 5;
        public static int WarnGo { get; set; } = 10;
        public static int CriticalGo { get; set; } = 5;
        
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
        public string SmartReason { get; set; } = "";    // ? AJOUTER CETTE LIGNE

        public string SmartReasonText => 
            string.IsNullOrWhiteSpace(SmartReason) ? LanguageManager.Get("Aucun problème détecté") ?? "Aucun problème détecté" : SmartReason;
        
        public int? SmartTemperature { get; set; }
        public int? SmartPowerOnHours { get; set; }
        public bool SmartAvailable { get; set; }

        public string SmartStatusText => SmartAvailable ? SmartStatus : "N/A";

        // ------------------------------------------------------------
        //  Mise à jour des couleurs
        // ------------------------------------------------------------
        public void UpdateBrushes()
        {
            // Priorité : Critical d'abord (le plus grave), puis Warning
            bool isCritical = FreePercent < CriticalPercent || FreeGo < CriticalGo;
            bool isWarning  = FreePercent < WarnPercent     || FreeGo < WarnGo;

            if (isCritical)
                StatusBrush = new SolidColorBrush(Color.FromRgb(255, 99, 71));   // rouge
            else if (isWarning)
                StatusBrush = new SolidColorBrush(Color.FromRgb(255, 185, 0));   // orange
            else
                StatusBrush = new SolidColorBrush(Color.FromRgb(108, 203, 95));  // vert

            // Badge SMART (inchangé)
            SmartBadgeBrush = SmartStatus switch
            {
                "OK"       => new SolidColorBrush(Color.FromRgb(108, 203, 95)),
                "Warning"  => new SolidColorBrush(Color.FromRgb(255, 185, 0)),
                "Critical" => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
                _          => new SolidColorBrush(Color.FromRgb(120, 120, 120))
            };
        }
        
        // Texte de la température
        public string TemperatureText => SmartTemperature.HasValue 
            ? $"{SmartTemperature}°C" 
            : "N/A";

        // Texte des heures de fonctionnement
        public string PowerOnHoursText => SmartPowerOnHours.HasValue
            ? SmartPowerOnHours.Value.ToString("N0") + " h"
            : "N/A";
            
        // Couleur selon la température
        public Brush TemperatureBrush
        {
            get
            {
                if (!SmartTemperature.HasValue)
                    return new SolidColorBrush(Color.FromRgb(120, 120, 120)); // gris

                int t = SmartTemperature.Value;

                if (t >= 70)
                    return new SolidColorBrush(Color.FromRgb(255, 99, 71));  // rouge

                if (t >= 55)
                    return new SolidColorBrush(Color.FromRgb(255, 185, 0));  // orange

                return new SolidColorBrush(Color.FromRgb(108, 203, 95));      // vert
            }
        }
    }
}