using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaMonitor.Core.DvbViewer;
using MediaMonitor.Core.Models;

namespace MediaMonitor.Core.Services.Engine
{
    /// <summary>
    /// Construit des MediaUsageItem à partir de sources SMB ou DVBViewer.
    /// </summary>
    public static class MediaItemFactory
    {
        // ---------------------------------------------------------------
        //  Depuis une entrée SMB
        // ---------------------------------------------------------------
        public static MediaUsageItem BuildItem(SmbOpenFile f, SmbSession? match)
        {
            string clientName = match?.ClientComputerName;
            if (string.IsNullOrWhiteSpace(clientName))
                clientName = match?.Username;
            if (string.IsNullOrWhiteSpace(clientName))
                clientName = "Inconnu";

            string ext = Path.GetExtension(f.Path).ToLower();

            int saison = 0;
            int episode = 0;
            string mediaType;

            if (ext is ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".m4a")
            {
                mediaType = "Audio";
            }
            else if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp")
            {
                mediaType = "Image";
            }
            else
            {
                MediaClassifier.ExtractEpisodeInfo(f.Path, out saison, out episode);

                mediaType = saison > 0 && episode > 0
                    ? "Serie"
                    : MediaClassifier.GetMediaType(f.Path);
            }

            string file = Path.GetFileName(f.Path);

            return new MediaUsageItem
            {
                SessionId = (uint)f.SessionId,
                ClientName = clientName,
                ClientDisplay = clientName,
                Path = f.Path,
                FileName = file,
                UNC = PathTools.ToUNC(f.Path),
                Timestamp = DateTime.Now,
                MediaType = mediaType,
                Nom = MediaNaming.CleanEpisodeName(file),
                Saison = saison,
                Episode = episode,
                Channel = ""
            };
        }

        // ---------------------------------------------------------------
        //  Depuis un flux DVBViewer
        // ---------------------------------------------------------------
        public static MediaUsageItem BuildDvbItem(
            DvbViewerClientStream s,
            string dvbBaseUrl,
            IReadOnlyList<MediaUsageItem> currentOpen)
        {
            // Type
            string mediaType = s.Type.StartsWith("REC", StringComparison.OrdinalIgnoreCase)
                ? "rec"
                : "tv";

            // Canal
            string channel = s.Type.StartsWith("REC", StringComparison.OrdinalIgnoreCase)
                ? s.Type.Substring(3).Trim()
                : s.Type.Trim();

            // Flag logo
            bool ifChannelLogo = mediaType == "rec" || mediaType == "tv";

            // URL du logo
            string channelLogo = BuildChannelLogoUrl(dvbBaseUrl, channel);

            // Titre brut
            string titreBrut = !string.IsNullOrWhiteSpace(s.Nom) ? s.Nom : channel;

            // Parsing
            var parsed = TvTitleParser.Parse(titreBrut);

            string titrePropre = parsed.CleanTitle;
            int saison = parsed.Saison;
            int episode = parsed.Episode;
            string seriesName = parsed.SeriesName;
            string episodeName = parsed.EpisodeName;

            string nomFinal = titrePropre;

            // Résolution client (avec cache DNS)
            string clientRaw = s.Client;
            string resolvedIp = clientRaw;
            string display = clientRaw;

            var (hostName, ipResolved, success) = DnsResolver.Resolve(clientRaw);

            if (success)
            {
                if (!string.IsNullOrWhiteSpace(ipResolved))
                    resolvedIp = ipResolved;

                display = hostName;
            }
            else
            {
                if (System.Net.IPAddress.TryParse(clientRaw, out _))
                {
                    resolvedIp = clientRaw;
                    display = clientRaw;
                }
                else
                {
                    var smb = currentOpen.FirstOrDefault(x =>
                        x.ClientDisplay.Contains(clientRaw, StringComparison.OrdinalIgnoreCase));

                    if (smb != null)
                        resolvedIp = smb.ClientName;

                    display = clientRaw;
                }
            }

            display = display
                .Replace(".home", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".local", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".lan", "", StringComparison.OrdinalIgnoreCase)
                .ToUpperInvariant();

            return new MediaUsageItem
            {
                SessionId = 0,
                ClientName = resolvedIp,
                ClientDisplay = display,
                Path = nomFinal,
                FileName = nomFinal,
                UNC = "",
                Timestamp = DateTime.Now,
                MediaType = mediaType,
                Nom = nomFinal,
                Saison = saison,
                Episode = episode,
                Channel = channel,
                SeriesName = seriesName,
                EpisodeName = episodeName,
                IfChannelLogo = ifChannelLogo,
                ChannelLogo = channelLogo
            };
        }

        // ---------------------------------------------------------------
        //  Helper : URL du logo canal
        // ---------------------------------------------------------------
        private static string BuildChannelLogoUrl(string dvbBaseUrl, string channel)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dvbBaseUrl))
                    return "";

                string baseLogoUrl = dvbBaseUrl;
                int idx = baseLogoUrl.IndexOf("/status.html", StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                    baseLogoUrl = baseLogoUrl.Substring(0, idx);

                string encodedChannel = Uri.EscapeDataString(channel);
                return $"{baseLogoUrl}/Logos/{encodedChannel}.png?height=200";
            }
            catch
            {
                return "";
            }
        }
    }
}