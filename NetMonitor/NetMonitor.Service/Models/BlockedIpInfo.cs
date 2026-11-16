﻿using System;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Models
{
    public class BlockedIpInfo
    {
        public string Ip            { get; set; } = "";
        public DateTime BlockedAt   { get; set; } = DateTime.UtcNow;
        public string Source        { get; set; } = "";
        public string Message       { get; set; } = "";
        public string? Details      { get; set; }
        public string Severity      { get; set; } = "-";
        public bool IsProcess       { get; set; }
        public string? ProcessPath  { get; set; }
        public bool IsOutbound      { get; set; }      

        public string? Country          { get; set; }
        public string? CountryCode      { get; set; }
        public string? City             { get; set; }
        public string? Isp              { get; set; }
        public string? Org              { get; set; }
        public bool IsPrivate           { get; set; }
        public string? ReverseDns       { get; set; }
        public DateTime? GeoResolvedAt  { get; set; }

        public Severity? ManualSeverity         { get; set; }
        public DateTime? ManualSeveritySetAt    { get; set; }
        public bool IsKnownScanner              { get; set; }

        /// <summary>Origine de l'IP (Scanner / Crawler / Bot / Microsoft / Blacklist / Whitelist).</summary>
        public IpOrigin Origin                  { get; set; } = IpOrigin.Unknown;
    }
}