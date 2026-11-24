﻿using System;

namespace NetMonitor.Service.Models
{
    public sealed class AuditGroupedEntry
    {
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int Count { get; set; }

        public Severity Severity    { get; set; }
        public string Source        { get; set; } = "";
        public string? RemoteIp     { get; set; }
        public int RemotePort       { get; set; }
        public int LocalPort        { get; set; }
        public int Pid              { get; set; }
        public string? ProcessName  { get; set; }
        public string? ProcessPath  { get; set; }
        public string? TrustReason  { get; set; }
        public string? Details      { get; set; }
        public bool IsKnownScanner  { get; set; }
        public string? Country      { get; set; }
        public string? CountryCode  { get; set; }
        public string? Isp          { get; set; }
        public string? Org          { get; set; }        
        public string? ReverseDns   { get; set; }            
        public bool IsOutbound      { get; set; }

        /// <summary>Origine de l'IP (Scanner / Crawler / Bot / Microsoft / Blacklist / Whitelist).</summary>
        public IpOrigin Origin      { get; set; } = IpOrigin.Unknown;

        /// <summary>Liste de confiance qui a fait ignorer l'alerte : Microsoft, Google, ou vide.</summary>
        public string? TrustedList  { get; set; }

        /// <summary>
        /// Liste à afficher. Les entrées antérieures au champ n'ont que leur origine :
        /// on la traduit en nom de liste plutôt que d'afficher un tiret à côté d'une
        /// IP dont on sait pertinemment d'où elle vient.
        /// </summary>
        public string? TrustedListEffective =>
            !string.IsNullOrWhiteSpace(TrustedList) ? TrustedList
            : Origin == IpOrigin.Microsoft ? "Microsoft"
            : Origin == IpOrigin.Bot       ? "Google"
            : null;
        
        public string GroupKey =>
            $"{Pid}|{RemoteIp ?? ""}|{LocalPort}|{RemotePort}|{ProcessName ?? ""}";                
    }
}