using System;
using System.Collections.Generic;

namespace MediaMonitor.Service.Api
{
    public class ApiDetail
    {
        public string service { get; set; } = "";
        public string machine { get; set; } = "";
        public DateTime timestamp { get; set; } = DateTime.Now;
        public int openCount { get; set; }
        public int historyCount { get; set; }

        public List<MediaItemDto> live { get; set; } = new();
        public string? lastImage { get; set; }
        public List<MediaItemDto> history { get; set; } = new();
        public MediaStatsDto stats { get; set; } = new();
    }

    public class MediaItemDto
    {
        public string client { get; set; } = "";
        public string mediaType { get; set; } = "";      // Audio / Video / Image / Serie / rec / tv
        public string nom { get; set; } = "";
        public string fileName { get; set; } = "";
        public string channel { get; set; } = "";
        public string seriesName { get; set; } = "";
        public string episodeName { get; set; } = "";
        public int saison { get; set; }
        public int episode { get; set; }
        public DateTime timestamp { get; set; }

        // Tags ID3 (audio)
        public string title { get; set; } = "";
        public string artist { get; set; } = "";
        public string album { get; set; } = "";
    }

    public class MediaStatsDto
    {
        public int total { get; set; }
        public Dictionary<string, int> byType { get; set; } = new();
        public List<MediaStatRowDto> topSeries { get; set; } = new();
        public List<MediaStatRowDto> topArtists { get; set; } = new();
        public List<MediaStatRowDto> topClients { get; set; } = new();
    }

    public class MediaStatRowDto
    {
        public string label { get; set; } = "";
        public int count { get; set; }
    }
}