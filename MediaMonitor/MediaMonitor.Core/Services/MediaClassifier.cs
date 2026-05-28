using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public static class MediaClassifier
{
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv",
        ".flv", ".mpeg", ".mpg", ".m4v", ".ts"
    };

    private static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff"
    };

    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma"
    };

    public static bool IsMedia(string path)
    {
        string ext = Path.GetExtension(path).ToLower();

        if (VideoExtensions.Contains(ext)) return true;
        if (ImageExtensions.Contains(ext)) return true;
        if (AudioExtensions.Contains(ext)) return true;

        return false;
    }

    public static string GetMediaType(string path)
    {
        string ext = Path.GetExtension(path).ToLower();

        if (VideoExtensions.Contains(ext)) return "Video";
        if (ImageExtensions.Contains(ext)) return "Image";
        if (AudioExtensions.Contains(ext)) return "Audio";

        return "Unknown";
    }

    public static void ExtractEpisodeInfo(string path, out int saison, out int episode)
    {
        saison = 0;
        episode = 0;

        string file = Path.GetFileNameWithoutExtension(path);

        // S06E03 / s6e3
        var match = Regex.Match(file, @"S(\d{1,2})E(\d{1,2})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            saison = int.Parse(match.Groups[1].Value);
            episode = int.Parse(match.Groups[2].Value);
            return;
        }

        // 06x03 / 6x3 (avec garde pour éviter Station 19, 1920x1080, etc.)
        match = Regex.Match(file,
            @"(?<!\d)(\d{1,2})x(\d{1,2})(?!\d)",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            saison = int.Parse(match.Groups[1].Value);
            episode = int.Parse(match.Groups[2].Value);
            return;
        }

        // S06 E03 (avec espace)
        match = Regex.Match(file, @"S(\d{1,2})\s*E(\d{1,2})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            saison = int.Parse(match.Groups[1].Value);
            episode = int.Parse(match.Groups[2].Value);
            return;
        }

        // Format compact 603 (saison 6 épisode 3)
        match = Regex.Match(file, @"(?<!\d)(\d)(\d{2})(?!\d)");
        if (match.Success)
        {
            saison = int.Parse(match.Groups[1].Value);
            episode = int.Parse(match.Groups[2].Value);
        }
    }
}

