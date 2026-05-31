using System;
using System.IO;
using System.Collections.Generic;

namespace WakeMonitor
{
    public class WakeMonitorSettings
    {
        // Options toujours actives (plus de config)
        public bool IncludePublicIP { get; set; } = true;
        public bool IncludeLocalIP { get; set; } = true;
        public bool IncludeMAC { get; set; } = true;
        public bool IncludeUSB { get; set; } = true;
        public bool IncludeCause { get; set; } = true;
        public bool IncludeDuration { get; set; } = true;

        // Liste blanche WOL
        public List<string> AllowedWolMacs { get; set; } = new();

        public static WakeMonitorSettings Load()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "MCEMonitor");
            Directory.CreateDirectory(dir);

            var settings = new WakeMonitorSettings();

            // Lecture whitelist
            string macFile = Path.Combine(dir, "AllowedWolMacs.txt");

            if (File.Exists(macFile))
            {
                foreach (var mac in File.ReadAllLines(macFile))
                {
                    string clean = mac.Trim().ToUpper();
                    if (clean.Length > 0)
                        settings.AllowedWolMacs.Add(clean);
                }
            }

            return settings;
        }
    }
}

