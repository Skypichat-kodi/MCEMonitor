using System;

namespace MediaMonitor.Core.Models
{
    public class MediaUsageItem
    {
        public uint SessionId { get; set; }

        public string ClientName { get; set; } = "";
        public string ClientIP { get; set; } = "";

        public string Path { get; set; } = "";
        public string FileName { get; set; } = "";
        public string UNC { get; set; } = "";

        public string MediaType { get; set; } = "";

        public string Nom { get; set; } = "";

        public int Saison { get; set; }
        public int Episode { get; set; }

        public DateTime Timestamp { get; set; }
    }
}

