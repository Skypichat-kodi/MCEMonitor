using System.Collections.Generic;

namespace MediaMonitor.Service.Web.Models
{
    public static class ViewHelpers
    {
        /// <summary>
        /// Convertit un objet MediaUsageItem / BackupItem en dictionnaire
        /// prêt pour le TemplateEngine (utilisé dans les boucles {{#for:...}}).
        /// </summary>
        public static Dictionary<string, object?> ToViewDict(
            string client, string mediaType, string channel,
            string nom, string fileName, string path,
            int saison, int episode)
        {
            return new Dictionary<string, object?>
            {
                ["ClientDisplay"] = client,
                ["MediaType"]     = mediaType,
                ["BadgeClass"]    = GetTypeBadgeClass(mediaType),
                ["Channel"]       = channel,
                ["Nom"]           = nom,
                ["FileName"]      = fileName,
                ["Path"]          = path,
                ["Saison"]        = saison > 0 ? saison.ToString() : "",
                ["Episode"]       = episode > 0 ? episode.ToString() : ""
            };
        }

        public static string GetTypeBadgeClass(string? mediaType)
        {
            return (mediaType ?? "").ToLowerInvariant() switch
            {
                "audio" => "type-audio",
                "serie" => "type-serie",
                "video" => "type-video",
                "rec"   => "type-rec",
                "tv"    => "type-tv",
                _       => ""
            };
        }
    }
}