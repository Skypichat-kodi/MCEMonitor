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

    // Programme TV
    public string Channel { get; set; } = "";
    public string Country { get; set; } = "";
    public string AgeRating { get; set; } = "";
    
    public DateTime Timestamp { get; set; }

    // Durée en secondes
    public double Duration { get; set; }

    // Tags ID3 (musique)
    public string Title { get; set; } = "";
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
}

