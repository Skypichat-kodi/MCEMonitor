using System;
using System.IO;

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

        public static WakeMonitorSettings Load()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "MCEMonitor");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, "WakeMonitor.config");



            var s = new WakeMonitorSettings();

            if (!File.Exists(path))
                return s;

            foreach (var line in File.ReadAllLines(path))
            {
                if (!line.Contains("=")) continue;

                var parts = line.Split('=');
                string key = parts[0].Trim();
                string val = parts[1].Trim().ToLower();

                bool b = val == "true";

                switch (key)
                {
                    case "IncludePublicIP": s.IncludePublicIP = b; break;
                    case "IncludeLocalIP": s.IncludeLocalIP = b; break;
                    case "IncludeMAC": s.IncludeMAC = b; break;
                    case "IncludeUSB": s.IncludeUSB = b; break;
                    case "IncludeCause": s.IncludeCause = b; break;
                    case "IncludeDuration": s.IncludeDuration = b; break;
                }
            }

            return s;
        }
    }
}

