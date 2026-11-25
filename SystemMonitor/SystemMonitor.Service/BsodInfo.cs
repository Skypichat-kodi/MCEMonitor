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

        /// <summary>
        /// Date à laquelle l'utilisateur a dit avoir pris connaissance du crash.
        /// Un BSOD acquitté quitte les alertes du Tray, mais reste dans l'historique :
        /// rien n'est effacé, ni la ligne, ni le dump.
        /// </summary>
        public DateTime? AcknowledgedAt { get; set; }
    }
}