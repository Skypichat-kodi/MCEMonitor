using System;
using System.Collections.Generic;

namespace MCEMonitorClient.Shared.Models
{
    public class NetDetailDto
    {
        public string Service { get; set; } = "";
        public string Machine { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string WorstSeverity { get; set; } = "ok";
        public int ActiveCount { get; set; }
        public int BlockedCount { get; set; }
        public List<NetIntrusionDto> Active { get; set; } = new();
        public List<NetBlockedIpDto> Blocked { get; set; } = new();
        public List<NetRetryDto> Retries { get; set; } = new();
        public List<NetPendingProcessDto> Pending { get; set; } = new();
        public List<NetWhitelistProcessDto> Whitelist { get; set; } = new();
        public List<NetProblemDto> Problems { get; set; } = new();
        public NetStatsSummaryDto Stats { get; set; } = new();
        public NetLastBlockDto? LastBlock { get; set; }
    }

    public class NetIntrusionDto
    {
        public DateTime Timestamp { get; set; }
        public string Source { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public string? RemoteIp { get; set; }
        public int RemotePort { get; set; }
        public int LocalPort { get; set; }
        public int Pid { get; set; }
        public string? ProcessName { get; set; }
        public string? Country { get; set; }
        public string? CountryCode { get; set; }
        public string? Isp { get; set; }
        public bool IsKnownScanner { get; set; }
    }

    public class NetBlockedIpDto
    {
        public string Ip { get; set; } = "";
        public DateTime BlockedAt { get; set; }
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "-";
        public bool IsProcess { get; set; }
        public string? ProcessPath { get; set; }
        public string? Country { get; set; }
        public string? CountryCode { get; set; }
        public string? City { get; set; }
        public string? Isp { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsKnownScanner { get; set; }
    }

    public class NetRetryDto
    {
        public string Ip { get; set; } = "";
        public string OriginalDetector { get; set; } = "-";
        public DateTime BlockedAt { get; set; }
        public DateTime FirstRetryAt { get; set; }
        public DateTime LastRetryAt { get; set; }
        public int RetryCount { get; set; }
    }

    public class NetPendingProcessDto
    {
        public string ProcessPath { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public string? TrustReason { get; set; }
        public string? RemoteIp { get; set; }
        public int RemotePort { get; set; }
        public int Pid { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int Count { get; set; }
        public string Severity { get; set; } = "Info";
        public string? Detector { get; set; }
    }

    public class NetWhitelistProcessDto
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool Enabled { get; set; }
    }

    public class NetProblemDto
    {
        public DateTime Timestamp { get; set; }
        public string Severity { get; set; } = "";
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class NetStatsSummaryDto
    {
        public int TotalAlerts { get; set; }
        public int DistinctIps { get; set; }
        public int DistinctCountries { get; set; }
        public List<NetTopIpDto> TopIps { get; set; } = new();
        public List<NetTopCountryDto> TopCountries { get; set; } = new();
        public List<NetTopDetectorDto> TopDetectors { get; set; } = new();
    }

    public class NetTopIpDto
    {
        public string Ip { get; set; } = "";
        public int Count { get; set; }
        public string MaxSeverity { get; set; } = "";
        public string? Country { get; set; }
        public string? CountryCode { get; set; }
        public string? Isp { get; set; }
    }

    public class NetTopCountryDto
    {
        public string Country { get; set; } = "";
        public string CountryCode { get; set; } = "";
        public int Count { get; set; }
        public int DistinctIps { get; set; }
    }

    public class NetTopDetectorDto
    {
        public string Detector { get; set; } = "";
        public int Count { get; set; }
    }

    public class NetLastBlockDto
    {
        public string Ip { get; set; } = "";
        public string? Detector { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Automatic { get; set; }
    }
}