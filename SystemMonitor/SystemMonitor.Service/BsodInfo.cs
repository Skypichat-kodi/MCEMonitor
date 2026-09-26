using System;

namespace SystemMonitor.Service
{
    public class BsodInfo
    {
        public DateTime Timestamp { get; set; }
        public string BugCheckCode { get; set; } = "";
        public string BugCheckName { get; set; } = "";
        public string Parameters { get; set; } = "";
        public string DumpPath { get; set; } = "";
        public bool DumpExists { get; set; }
        public string Source { get; set; } = "";
        public string FaultyModule { get; set; } = "";      // ? NOUVEAU
        public string FaultAddress { get; set; } = "";      // ? NOUVEAU
    }
}