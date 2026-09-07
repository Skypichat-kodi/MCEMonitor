namespace MediaMonitor.Service.Web.Helpers
{
    public static class TypeBadgeHelper
    {
        public static string GetTypeBadgeClass(string mediaType)
        {
            return mediaType.ToLowerInvariant() switch
            {
                "audio" => "type-audio",
                "video" => "type-video",
                "serie" => "type-serie",
                "rec"   => "type-rec",
                "tv"    => "type-tv",
                _ => ""
            };
        }
    }
}
