using System.Collections.Generic;

namespace MCEMonitorClient.Tray.Models
{
    public class ServersConfig
    {
        public int PollIntervalSeconds { get; set; } = 30;
        public bool NotifyOnStateChange { get; set; } = true;
        public bool PlaySoundOnCritical { get; set; } = true;
        public bool StartWithWindows { get; set; } = true;
        public List<ServerEntry> Servers { get; set; } = new();
    }
}