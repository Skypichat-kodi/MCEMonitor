using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TagLib;
using System.Text.RegularExpressions;

namespace MediaMonitor.Core.Services
{
    public static class FileAnalyzer
    {
        private static readonly string FfmpegDir =
            Path.Combine(AppContext.BaseDirectory, "ffmpeg");

        private static string FfprobePath => Path.Combine(FfmpegDir, "ffprobe.exe");
        private static string FfmpegPath  => Path.Combine(FfmpegDir, "ffmpeg.exe");

        public static MediaUsageItem Analyze(string path)
        {
            var fi = new FileInfo(path);

            var item = new MediaUsageItem
            {
                Path = path,
                FileName = fi.Name,
                MediaType = DetectType(path),
                Timestamp = DateTime.Now,
                Nom = fi.Name
            };

            if (item.MediaType == "Audio")
                AnalyzeAudio(path, item);
            else if (item.MediaType == "Video")
                AnalyzeVideo(path, item);

            return item;
        }

        // ---------------------------
        // AUDIO (TagLibSharp) — NE TOUCHE PAS
        // ---------------------------
        private static void AnalyzeAudio(string path, MediaUsageItem item)
        {
            try
            {
                var tagFile = TagLib.File.Create(path);

                item.Duration = tagFile.Properties.Duration.TotalSeconds;
                item.Title = tagFile.Tag.Title ?? item.FileName;
                item.Artist = tagFile.Tag.FirstPerformer ?? "";
                item.Album = tagFile.Tag.Album ?? "";
                item.Year = (int)tagFile.Tag.Year;
                item.Track = (int)tagFile.Tag.Track;
                item.Genre = tagFile.Tag.FirstGenre ?? "";

                if (tagFile.Tag.Pictures?.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    if (pic.Data != null && pic.Data.Count > 0)
                        item.AlbumArt = pic.Data.Data;
                }
            }
            catch
            {
                // On laisse la musique tranquille : si TagLibSharp plante, on n'affiche rien.
            }
        }

        // ---------------------------
        // VIDÉO (ffprobe + ffmpeg)
        // ---------------------------
        private static void AnalyzeVideo(string path, MediaUsageItem item)
        {
            // Séries : "Série - 01x06 - Épisode.ts"
            ParseSeriesInfo(item.FileName, item);

            // Durée + codecs via ffprobe
            try
            {
                var info = RunFfprobe(path);

                if (info.format != null &&
                    double.TryParse(info.format.duration,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double dur))
                {
                    item.Duration = dur;
                }

                if (info.streams != null)
                {
                    foreach (var s in info.streams)
                    {
                        if (s.codec_type == "video" && string.IsNullOrEmpty(item.VideoCodec))
                            item.VideoCodec = s.codec_name ?? "";

                        if (s.codec_type == "audio" && string.IsNullOrEmpty(item.AudioCodec))
                            item.AudioCodec = s.codec_name ?? "";
                    }
                }
            }
            catch
            {
                // Si ffprobe plante, on laisse durée/codec vides.
            }

            // Miniature JPEG à 20 secondes via ffmpeg
            try
            {
                string tempThumb = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");

                RunFfmpegThumbnail(path, tempThumb, 20);

                if (System.IO.File.Exists(tempThumb))
                {
                    item.AlbumArt = System.IO.File.ReadAllBytes(tempThumb);
                    System.IO.File.Delete(tempThumb);
                }
            }
            catch
            {
                // Si ffmpeg plante, pas de miniature.
            }
            
            // Définition du titre pour la popup Webserver
            if (!string.IsNullOrEmpty(item.EpisodeName))
            {
                // Série
                item.Title = item.EpisodeName;
            }
            else
            {
                // Film ou vidéo simple
                item.Title = Path.GetFileNameWithoutExtension(item.FileName);
            }
        }

        // ---------------------------
        // ffprobe : durée + codecs
        // ---------------------------
        // ffprobe -v quiet -print_format json -show_streams -show_format "path"
        private static (Format format, StreamInfo[] streams) RunFfprobe(string path)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfprobePath,
                Arguments = $"-v quiet -print_format json -show_streams -show_format \"{path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            string json = proc!.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            var doc = JsonSerializer.Deserialize<FfprobeResult>(json);
            return (doc!.format, doc.streams ?? Array.Empty<StreamInfo>());
        }

        // ---------------------------
        // ffmpeg : miniature JPEG à N secondes
        // ---------------------------
        // ffmpeg -y -ss 20 -i "path" -frames:v 1 -q:v 2 "thumb.jpg"
        private static void RunFfmpegThumbnail(string input, string output, int seconds)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = $"-y -ss {seconds} -i \"{input}\" -frames:v 1 -q:v 2 \"{output}\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            proc!.WaitForExit();
        }

        // ---------------------------
        // Modèles JSON ffprobe
        // ---------------------------
        private class FfprobeResult
        {
            public Format format { get; set; }
            public StreamInfo[] streams { get; set; }
        }

        private class Format
        {
            public string duration { get; set; }
        }

        private class StreamInfo
        {
            public string codec_type { get; set; }
            public string codec_name { get; set; }
        }

        // ---------------------------
        // Parsing séries
        // ---------------------------
        private static void ParseSeriesInfo(string fileName, MediaUsageItem item)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);

            // On cherche directement "NNxMM" dans tout le nom
            var match = Regex.Match(
                name,
                @"(?<saison>\d{1,2})x(?<episode>\d{1,2})",
                RegexOptions.IgnoreCase
            );

            if (!match.Success)
            {
                // Pas de motif saison/épisode ? on garde le nom complet comme titre
                item.EpisodeName = name;
                return;
            }

            // Saison / épisode
            item.Saison = int.Parse(match.Groups["saison"].Value);
            item.Episode = int.Parse(match.Groups["episode"].Value);

            // On découpe sur " - " pour récupérer série et titre,
            // mais en se basant sur la position du motif trouvé.
            // Exemple : "Série - 01x06 - Titre"
            var parts = name.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 3)
            {
                item.SeriesName = parts[0].Trim();      // "Knight Rider le retour de K2000"
                item.EpisodeName = parts[2].Trim();     // "Le robot tueur"
            }
            else
            {
                // Si le split ne correspond pas exactement, on met au moins le titre
                item.EpisodeName = name;
            }
        }

        // ---------------------------
        // Détection type
        // ---------------------------
        private static string DetectType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            return ext switch
            {
                ".mp3" => "Audio",
                ".wav" => "Audio",
                ".flac" => "Audio",
                ".aac" => "Audio",

                ".jpg" => "Image",
                ".jpeg" => "Image",
                ".png" => "Image",
                ".bmp" => "Image",
                ".gif" => "Image",

                ".mp4" => "Video",
                ".mkv" => "Video",
                ".avi" => "Video",
                ".ts" => "Video",
                ".mov" => "Video",

                _ => "Unknown"
            };
        }
    }
}
