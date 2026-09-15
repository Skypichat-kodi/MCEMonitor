using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaMonitor.Core.Services.Engine
{
    /// <summary>
    /// Résultat du parsing d'un titre TV DVBViewer.
    /// </summary>
    public class TvTitleInfo
    {
        public string CleanTitle { get; set; } = "";
        public int Saison { get; set; }
        public int Episode { get; set; }
        public string SeriesName { get; set; } = "";
        public string EpisodeName { get; set; } = "";
    }

    /// <summary>
    /// Parse les titres TV DVBViewer (Saison/Episode, SeriesName, EpisodeName).
    /// </summary>
    public static class TvTitleParser
    {
        /// <summary>
        /// Parse un titre DVBViewer.
        /// Règles :
        ///  1. "Saison X / Episode Y" ? Saison + Episode + SeriesName
        ///  2. "Série : Épisode" (sans S/E) ? SeriesName + EpisodeName
        ///  3. Film / Doc / Mag ? nettoyage caractères spéciaux
        /// </summary>
        public static TvTitleInfo Parse(string nom)
        {
            var info = new TvTitleInfo();

            if (string.IsNullOrWhiteSpace(nom))
                return info;

            string work = nom.Trim();

            // --- RÈGLE 1 : Saison / Episode ---
            var mClassic = Regex.Match(work, @"Saison\s+(\d+)\s*/\s*Episode\s+(\d+)", RegexOptions.IgnoreCase);
            if (mClassic.Success)
            {
                info.Saison = int.Parse(mClassic.Groups[1].Value);
                info.Episode = int.Parse(mClassic.Groups[2].Value);

                int idxSeason = work.IndexOf(" - Saison", StringComparison.OrdinalIgnoreCase);
                info.SeriesName = idxSeason > 0
                    ? work.Substring(0, idxSeason).Trim()
                    : work;

                int idxColon = work.LastIndexOf(" : ");
                if (idxColon > 0)
                {
                    info.EpisodeName = work.Substring(idxColon + 3).Trim();

                    int idxDashEp = info.EpisodeName.IndexOf(" - ");
                    if (idxDashEp > 0)
                        info.EpisodeName = info.EpisodeName.Substring(0, idxDashEp).Trim();
                }

                info.CleanTitle = info.SeriesName;
                return info;
            }

            // --- RÈGLE 2 : Série sans Saison/Episode mais avec " : " ---
            int idxColonSimple = work.IndexOf(" : ");
            if (idxColonSimple > 0)
            {
                info.SeriesName = work.Substring(0, idxColonSimple).Trim();
                info.EpisodeName = work.Substring(idxColonSimple + 3).Trim();

                int idxDashEp = info.EpisodeName.IndexOf(" - ");
                if (idxDashEp > 0)
                    info.EpisodeName = info.EpisodeName.Substring(0, idxDashEp).Trim();

                info.CleanTitle = info.SeriesName;
                return info;
            }

            // --- RÈGLE 3 : Film / Doc / Mag / autres ---
            int idxDash = work.IndexOf(" - ");
            string titre = idxDash > 0 ? work.Substring(0, idxDash) : work;

            var sb = new StringBuilder();
            foreach (char c in titre)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '&' || c == '\'' || c == ':')
                    sb.Append(c);
                else
                    sb.Append(", ");
            }

            string cleaned = sb.ToString();
            cleaned = Regex.Replace(cleaned, @"\s*,\s*,\s*", ", ");
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
            cleaned = cleaned.Trim(' ', ',');

            info.CleanTitle = cleaned;
            info.SeriesName = "";
            info.EpisodeName = "";

            return info;
        }
    }
}