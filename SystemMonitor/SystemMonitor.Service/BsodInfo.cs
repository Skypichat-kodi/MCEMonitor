using System;

namespace SystemMonitor.Service
{
    public class BsodInfo
    {
        public DateTime Timestamp { get; set; }
        public string BugCheckCode { get; set; } = "";     // ex: "0x0000007E"
        public string BugCheckName { get; set; } = "";     // ex: "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED"
        public string Parameters { get; set; } = "";       // les paramètres du bugcheck
        public string DumpPath { get; set; } = "";         // chemin du fichier .dmp
        public bool DumpExists { get; set; }               // le fichier existe-t-il ?
        public string Source { get; set; } = "";           // BugCheck ou WER-SystemErrorReporting
    }
}