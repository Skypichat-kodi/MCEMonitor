using System;
using System.IO;
using System.Collections.Generic;

namespace WakeMonitor
{
    public class WakeMonitorSettings
    {
        public bool IncludePublicIP { get; set; } = true;
        public bool IncludeLocalIP { get; set; } = true;
        public bool IncludeMAC { get; set; } = true;
        public bool IncludeUSB { get; set; } = true;
        public bool IncludeCause { get; set; } = true;
        public bool IncludeDuration { get; set; } = true;

        // ?? Liste blanche des MAC autorisées pour WOL
        public List<string> AllowedWolMacs { get; set; } = new();

        public static WakeMonitorSettings Load()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "MCEMonitor");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, "WakeMonitor.config");

            var settings = new WakeMonitorSettings();

            if (!File.Exists(path))
                return settings;

            foreach (var line in File.ReadAllLines(path))
            {
                if (!line.Contains("=")) continue;

                var parts = line.Split('=');
                string key = parts[0].Trim();
                string value = parts[1].Trim();

                bool enabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);

                switch (key)
                {
                    case "IncludePublicIP": settings.IncludePublicIP = enabled; break;
                    case "IncludeLocalIP": settings.IncludeLocalIP = enabled; break;
                    case "IncludeMAC": settings.IncludeMAC = enabled; break;
                    case "IncludeUSB": settings.IncludeUSB = enabled; break;
                    case "IncludeCause": settings.IncludeCause = enabled; break;
                    case "IncludeDuration": settings.IncludeDuration = enabled; break;

                    // ?? Lecture de la whitelist MAC
                    case "AllowedWolMacs":
                        foreach (var mac in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                            settings.AllowedWolMacs.Add(mac.Trim().ToUpper());
                        break;
                }
            }

            return settings;
        }
    }
}

