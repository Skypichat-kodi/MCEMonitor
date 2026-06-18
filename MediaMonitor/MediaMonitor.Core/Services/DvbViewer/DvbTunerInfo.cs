using System;

namespace MediaMonitor.Core.DvbViewer
{
    public class DvbTunerInfo
    {
        public string TunerName { get; set; }
        public string Channel { get; set; }
        public string Title { get; set; }

        public override string ToString()
            => $"{TunerName} | {Channel} | {Title}";
    }
}

