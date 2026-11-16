﻿using System;

namespace NetMonitor.Service.Models
{
    public sealed class BlockedIpRetryInfo
    {
        public string Ip                { get; set; } = "";
        public string OriginalDetector  { get; set; } = "-";
        public DateTime BlockedAt       { get; set; }
        public DateTime FirstRetryAt    { get; set; }
        public DateTime LastRetryAt     { get; set; }
        public int RetryCount           { get; set; }

        /// <summary>Origine de l'IP (Scanner / Crawler / Bot / Microsoft / Blacklist / Whitelist).</summary>
        public IpOrigin Origin          { get; set; } = IpOrigin.Unknown;
    }
}