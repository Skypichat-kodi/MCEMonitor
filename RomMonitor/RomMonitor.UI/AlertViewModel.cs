using System;
using System.Windows.Media;

namespace RomMonitor.UI
{
    public class AlertViewModel
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Target { get; set; } = "";
        public string Message { get; set; } = "";
        public bool EmailSent { get; set; }

        public string TimeText => Timestamp.ToString("dd/MM HH:mm:ss");

        public Brush SeverityBrush { get; set; } = new SolidColorBrush(Color.FromRgb(76, 194, 255));

        public string EmailText => EmailSent ? "[M]" : "";

        // ------------------------------------------------------------
        //  Mise à jour des couleurs
        // ------------------------------------------------------------
        public void UpdateBrushes()
        {
            SeverityBrush = Severity switch
            {
                "Critical" => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
                "Warning" => new SolidColorBrush(Color.FromRgb(255, 185, 0)),
                _ => new SolidColorBrush(Color.FromRgb(76, 194, 255))
            };
        }
    }
}