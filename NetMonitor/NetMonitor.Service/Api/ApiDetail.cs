using System;
using System.Collections.Generic;

namespace NetMonitor.Service.Api
{
    public class ApiDetail
    {
        public string service { get; set; } = "";
        public string machine { get; set; } = "";
        public DateTime timestamp { get; set; } = DateTime.Now;
        public string worstSeverity { get; set; } = "ok";
        public int activeCount { get; set; }
        public int blockedCount { get; set; }
        public List<IntrusionDetailDto> active { get; set; } = new();
        public List<BlockedIpDetailDto> blocked { get; set; } = new();
        public List<BlockedRetryDetailDto> retries { get; set; } = new();
        public List<PendingProcessDetailDto> pending { get; set; } = new();
        public List<WhitelistProcessDto> whitelist { get; set; } = new();
        public List<ProblemDetailDto> problems { get; set; } = new();
        public StatsSummaryDto stats { get; set; } = new();
        public LastBlockDetailDto? lastBlock { get; set; }
    }

    public class IntrusionDetailDto
    {
        public DateTime timestamp { get; set; }
        public string source { get; set; } = "";
        public string severity { get; set; } = "";
        public string message { get; set; } = "";
        public string? remoteIp { get; set; }
        public int remotePort { get; set; }
        public int localPort { get; set; }
        public int pid { get; set; }
        public string? processName { get; set; }
        public string? country { get; set; }
        public string? countryCode { get; set; }
        public string? isp { get; set; }
        public bool isKnownScanner { get; set; }
    }

    public class BlockedIpDetailDto
    {
        public string ip { get; set; } = "";
        public DateTime blockedAt { get; set; }
        public string source { get; set; } = "";
        public string message { get; set; } = "";
        public string severity { get; set; } = "-";
        public bool isProcess { get; set; }
        public string? processPath { get; set; }
        public string? country { get; set; }
        public string? countryCode { get; set; }
        public string? city { get; set; }
        public string? isp { get; set; }
        public bool isPrivate { get; set; }
        public bool isKnownScanner { get; set; }
    }

    public class BlockedRetryDetailDto
    {
        public string ip { get; set; } = "";
        public string originalDetector { get; set; } = "-";
        public DateTime blockedAt { get; set; }
        public DateTime firstRetryAt { get; set; }
        public DateTime lastRetryAt { get; set; }
        public int retryCount { get; set; }
    }

    public class PendingProcessDetailDto
    {
        public string processPath { get; set; } = "";
        public string processName { get; set; } = "";
        public string? trustReason { get; set; }
        public string? remoteIp { get; set; }
        public int remotePort { get; set; }
        public int pid { get; set; }
        public DateTime firstSeen { get; set; }
        public DateTime lastSeen { get; set; }
        public int count { get; set; }
        public string severity { get; set; } = "Info";
        public string? detector { get; set; }
    }

    public class ProblemDetailDto
    {
        public DateTime timestamp { get; set; }
        public string severity { get; set; } = "";
        public string category { get; set; } = "";
        public string message { get; set; } = "";
    }

    public class StatsSummaryDto
    {
        public int totalAlerts { get; set; }
        public int distinctIps { get; set; }
        public int distinctCountries { get; set; }
        public List<TopIpDto> topIps { get; set; } = new();
        public List<TopCountryDto> topCountries { get; set; } = new();
        public List<TopDetectorDto> topDetectors { get; set; } = new();
    }

    public class TopIpDto
    {
        public string ip { get; set; } = "";
        public int count { get; set; }
        public string maxSeverity { get; set; } = "";
        public string? country { get; set; }
        public string? countryCode { get; set; }
        public string? isp { get; set; }
    }

    public class TopCountryDto
    {
        public string country { get; set; } = "";
        public string countryCode { get; set; } = "";
        public int count { get; set; }
        public int distinctIps { get; set; }
    }

    public class TopDetectorDto
    {
        public string detector { get; set; } = "";
        public int count { get; set; }
    }

    public class LastBlockDetailDto
    {
        public string ip { get; set; } = "";
        public string? detector { get; set; }
        public DateTime timestamp { get; set; }
        public bool automatic { get; set; }
    }
    
    public class WhitelistProcessDto
    {
        public string name { get; set; } = "";
        public string path { get; set; } = "";
        public bool enabled { get; set; }
    }    
}