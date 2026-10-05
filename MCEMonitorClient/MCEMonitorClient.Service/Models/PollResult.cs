using System;
using System.Collections.Generic;

namespace MCEMonitorClient.Service.Models
{
    /// <summary>
    /// Résultat d'un poll sur un serveur.
    /// </summary>
    public class PollResult
    {
        public string ServerId { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string ServiceType { get; set; } = "";
        public string BaseUrl { get; set; } = "";

        public bool Online { get; set; }
        public string Status { get; set; } = "ok";
        public string WorstSeverity { get; set; } = "ok";

        // ? Liste complète des problèmes
        public List<ProblemItem> Problems { get; set; } = new();

        // ? Liste des médias en cours (pour MediaMonitor)
        public List<MediaItem> Media { get; set; } = new();

        public int ProblemCount { get; set; }
        public string FirstProblem { get; set; } = "";

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Un problème individuel remonté par un serveur.
    /// </summary>
    public class ProblemItem
    {
        public string Severity { get; set; } = "";     // "critical" / "warning"
        public string Category { get; set; } = "";     // "CpuHigh", "DiskSpace", "Smart", ...
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Un média en cours (pour MediaMonitor).
    /// </summary>
    public class MediaItem
    {
        public string Client { get; set; } = "";
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public int Saison { get; set; }
        public int Episode { get; set; }

        /// <summary>Clé unique pour identifier un média (client + titre + saison + épisode).</summary>
        public string Key => $"{Client}|{Type}|{Title}|{Saison}|{Episode}";
    }
}