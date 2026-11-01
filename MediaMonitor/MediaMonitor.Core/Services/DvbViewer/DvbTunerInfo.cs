using System;

namespace MediaMonitor.Core.DvbViewer
{
    public class DvbTunerInfo
    {
        public string TunerName { get; set; } = string.Empty;
        public string Channel   { get; set; } = string.Empty;
        public string Title     { get; set; } = string.Empty;

        public override string ToString()
            => $"{TunerName} | {Channel} | {Title}";
    }
}