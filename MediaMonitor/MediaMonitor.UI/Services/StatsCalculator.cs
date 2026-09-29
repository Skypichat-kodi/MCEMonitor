using System.Collections.Generic;
using System.Linq;
using MediaMonitor.Core.Models;
using MediaMonitor.UI.Models;

namespace MediaMonitor.UI.Services
{
    public static class StatsCalculator
    {
        public static (int total, int audio, int serie, int video, int image, int rec, int tv)
            CountByType(IEnumerable<MediaUsageItem> items)
        {
            int total = 0, audio = 0, serie = 0, video = 0, image = 0, rec = 0, tv = 0;
            foreach (var i in items)
            {
                total++;
                switch ((i.MediaType ?? "").ToLowerInvariant())
                {
                    case "audio": audio++; break;
                    case "serie": serie++; break;
                    case "video": video++; break;
                    case "image": image++; break;
                    case "rec":   rec++;   break;
                    case "tv":    tv++;    break;
                }
            }
            return (total, audio, serie, video, image, rec, tv);
        }

        public static List<StatRow> TopSeries(IEnumerable<MediaUsageItem> items, int take = 10) =>
            items.Where(i => (i.MediaType ?? "").Equals("serie", System.StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(i.Nom))
                 .Select(i => ExtractSerie(i.Nom).Serie)
                 .Where(s => !string.IsNullOrWhiteSpace(s))
                 .GroupBy(s => s)
                 .Select(g => new StatRow { Label = g.Key, Count = g.Count() })
                 .OrderByDescending(x => x.Count)
                 .Take(take).ToList();

        public static List<StatRow> TopArtistes(IEnumerable<MediaUsageItem> items, int take = 10) =>
            items.Where(i => (i.MediaType ?? "").Equals("audio", System.StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(i.Nom))
                 .Select(i => ExtractAudio(i.Nom).Artiste)
                 .Where(a => !string.IsNullOrWhiteSpace(a))
                 .GroupBy(a => a)
                 .Select(g => new StatRow { Label = g.Key, Count = g.Count() })
                 .OrderByDescending(x => x.Count)
                 .Take(take).ToList();

        public static List<StatRow> TopClients(IEnumerable<MediaUsageItem> items, int take = 10) =>
            items.Where(i => !string.IsNullOrWhiteSpace(i.ClientDisplay))
                 .GroupBy(i => i.ClientDisplay)
                 .Select(g => new StatRow { Label = g.Key, Count = g.Count() })
                 .OrderByDescending(x => x.Count)
                 .Take(take).ToList();

        public static List<ClientStatRow> PerClient(IEnumerable<MediaUsageItem> items) =>
            items.Where(i => !string.IsNullOrWhiteSpace(i.ClientDisplay)
                             && !string.IsNullOrWhiteSpace(i.MediaType))
                 .GroupBy(i => i.ClientDisplay)
                 .Select(g => new ClientStatRow
                 {
                     Client = g.Key,
                     Audio  = g.Count(x => x.MediaType.Equals("audio", System.StringComparison.OrdinalIgnoreCase)),
                     Serie  = g.Count(x => x.MediaType.Equals("serie", System.StringComparison.OrdinalIgnoreCase)),
                     Video  = g.Count(x => x.MediaType.Equals("video", System.StringComparison.OrdinalIgnoreCase)),
                     Image  = g.Count(x => x.MediaType.Equals("image", System.StringComparison.OrdinalIgnoreCase)),
                     Rec    = g.Count(x => x.MediaType.Equals("rec",   System.StringComparison.OrdinalIgnoreCase)),
                     Tv     = g.Count(x => x.MediaType.Equals("tv",    System.StringComparison.OrdinalIgnoreCase))
                 })
                 .OrderByDescending(x => x.Total)
                 .ToList();

        private static (string Serie, string Episode) ExtractSerie(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return ("", "");
            var parts = nom.Split(" - ", 2, System.StringSplitOptions.TrimEntries);
            return parts.Length == 2 ? (parts[0], parts[1]) : (nom, "");
        }

        private static (string Track, string Artiste, string Titre) ExtractAudio(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return ("", "", "");
            var parts = nom.Split(" - ", System.StringSplitOptions.TrimEntries);
            if (parts.Length >= 3 && int.TryParse(parts[0], out _))
                return (parts[0], parts[1], string.Join(" - ", parts.Skip(2)));
            if (parts.Length >= 2)
                return ("", parts[0], string.Join(" - ", parts.Skip(1)));
            return ("", "", nom);
        }
    }
}