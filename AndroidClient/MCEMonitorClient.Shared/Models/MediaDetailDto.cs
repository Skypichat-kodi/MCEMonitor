using System;
using System.Collections.Generic;

namespace MCEMonitorClient.Shared.Models
{
    public class MediaDetailDto
    {
        public string Service { get; set; } = "";
        public string Machine { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public int OpenCount { get; set; }
        public int HistoryCount { get; set; }
        public List<MediaItemDto> Live { get; set; } = new();
        public string? LastImage { get; set; }
        public List<MediaItemDto> History { get; set; } = new();
        public MediaStatsDto Stats { get; set; } = new();
    }

    public class MediaItemDto
    {
        public string Client { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string Nom { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Channel { get; set; } = "";
        public string SeriesName { get; set; } = "";
        public string EpisodeName { get; set; } = "";
        public int Saison { get; set; }
        public int Episode { get; set; }
        public DateTime Timestamp { get; set; }
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
    }

    public class MediaStatsDto
    {
        public int Total { get; set; }
        public Dictionary<string, int> ByType { get; set; } = new();
        public List<MediaStatRowDto> TopSeries { get; set; } = new();
        public List<MediaStatRowDto> TopArtists { get; set; } = new();
        public List<MediaStatRowDto> TopClients { get; set; } = new();
    }

    public class MediaStatRowDto
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }
}