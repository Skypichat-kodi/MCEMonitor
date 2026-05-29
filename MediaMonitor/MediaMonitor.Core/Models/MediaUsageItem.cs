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

        // ?? Icône automatique selon le type de média
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
}

