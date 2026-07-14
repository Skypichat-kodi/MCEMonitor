public class MediaUsageItem
{
    public uint SessionId { get; set; }

    public string ClientName { get; set; } = "";
    public string ClientDisplay { get; set; } = "";

    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public string UNC { get; set; } = "";

    public string MediaType { get; set; } = "";
    public string Nom { get; set; } = "";

    // Séries
    public int Saison { get; set; }
    public int Episode { get; set; }

    // Ajout pour les séries
    public string SeriesName { get; set; } = "";
    public string EpisodeName { get; set; } = "";

    public DateTime Timestamp { get; set; }

    // Durée en secondes
    public double Duration { get; set; }

    // Tags ID3 (musique)
    public string Title { get; set; } = "";
    public string TitleTag { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public int Year { get; set; }
    public int Track { get; set; }
    public string Genre { get; set; } = "";

    // Miniature (cover ou thumbnail vidéo)
    public byte[]? AlbumArt { get; set; }

    // Codecs vidéo/audio
    public string VideoCodec { get; set; } = "";
    public string AudioCodec { get; set; } = "";

    public string IconPath
    {
        get
        {
            return MediaType switch
            {
                "Image" => "pack://application:,,,/Resources/Icons/icon_image.png",
                "Video" => "pack://application:,,,/Resources/Icons/icon_video.png",
                "Audio" => "pack://application:,,,/Resources/Icons/icon_audio.png",
                "Serie" => "pack://application:,,,/Resources/Icons/icon_serie.png",
                _ => "pack://application:,,,/Resources/Icons/icon_video.png"
            };
        }
    }

    // ============================================================
    // ?? AJOUTS POUR LA VERSION WEB (HTML) — SANS IMPACT WPF ??
    // ============================================================

    // Taille en Mo (calculée dans BuildInfoPageFromPost)
    public double SizeMB { get; set; }

    // Durée formatée mm:ss
    public string DurationText =>
        Duration > 0 ? TimeSpan.FromSeconds(Duration).ToString(@"mm\:ss") : "";

    // Miniature encodée en Base64 pour HTML
    public string AlbumArtBase64 =>
        AlbumArt != null
            ? "data:image/jpeg;base64," + Convert.ToBase64String(AlbumArt)
            : "/default-cover.png";

    // Flags pour les blocs conditionnels HTML
    public bool IsVideo => MediaType == "Video";
    public bool IsAudio => MediaType == "Audio";

    public bool HasDuration => Duration > 0;
    public bool HasSeries => !string.IsNullOrWhiteSpace(SeriesName);
    public bool HasSeasonEpisode => Saison > 0 && Episode > 0;
    public bool HasEpisodeName => !string.IsNullOrWhiteSpace(EpisodeName);
    public bool HasVideoCodec => !string.IsNullOrWhiteSpace(VideoCodec);
    public bool HasAudioCodec => !string.IsNullOrWhiteSpace(AudioCodec);
}

