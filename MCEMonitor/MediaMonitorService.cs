namespace MCEMonitor.Services
{
    public class MediaMonitorService
    {
        public void RunSilent()
        {
            // TODO : logique MediaMonitor silencieuse
        }

        public void SendReport()
        {
            // TODO : envoi du rapport
        }

        public LiveStatus GetLiveStatus()
        {
            return new LiveStatus
            {
                CpuUsage = 0,
                GpuUsage = 0,
                CpuTemp = 0,
                NetworkUsage = 0,
                DiskUsage = 0
            };
        }
    }

    public class LiveStatus
    {
        public int CpuUsage { get; set; }
        public int GpuUsage { get; set; }
        public int CpuTemp { get; set; }
        public double NetworkUsage { get; set; }
        public int DiskUsage { get; set; }
    }
}

