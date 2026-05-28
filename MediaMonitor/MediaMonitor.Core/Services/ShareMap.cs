using System.Collections.Generic;

namespace MediaMonitor.Core.Services
{
    public static class ShareMap
    {
        public static readonly Dictionary<string, string> DriveToShare = new()
        {
            { "H:", @"\\MCE-SERVER\MEDIAS0" },
            { "G:", @"\\MCE-SERVER\MEDIAS1" },
            { "D:", @"\\MCE-SERVER\PROCESSUS AUTO" }
        };
    }
}

