using System;
using System.Collections.Generic;

namespace NetMonitor.Service.Services
{
    public static class SystemProcessFilter
    {
        private static readonly HashSet<int> SystemPids = new() { 0, 4, 8 };

        private static readonly HashSet<string> SystemProcessNames =
            new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "Memory Compression", "Secure System",
            "csrss", "lsass", "wininit", "winlogon", "services", "smss",
            "fontdrvhost", "dwm", "spoolsv", "audiodg", "sihost",
            "LogonUI", "WUDFHost", "SecurityHealthService", "Sense",
            "MsMpEng", "NisSrv", "MpDefenderCoreService"
        };

        private static readonly string[] MicrosoftPaths = new[]
        {
            @"C:\Windows\System32\",
            @"C:\Windows\SysWOW64\",
            @"C:\Windows\SystemApps\",
            @"C:\ProgramData\Microsoft\Windows Defender\",
            @"C:\Program Files\Windows Defender\",
            @"C:\Program Files (x86)\Windows Defender\"
        };

        public static bool ShouldIgnore(int pid, string? processName, string? fullPath)
        {
            // 1) PID noyau
            if (SystemPids.Contains(pid))
                return true;

            // 2) Nom système + chemin Microsoft → ignoré
            //    Nom système + chemin inaccessible → ignoré (faux négatif accepté)
            //    Nom système + chemin non-Microsoft → PAS ignoré (usurpation)
            if (!string.IsNullOrEmpty(processName) && SystemProcessNames.Contains(processName))
            {
                if (!string.IsNullOrEmpty(fullPath))
                    return IsMicrosoftPath(fullPath);

                return true;   // chemin inaccessible
            }

            // 3) Chemin Microsoft + signature Microsoft valide → ignoré
            if (!string.IsNullOrEmpty(fullPath) && IsMicrosoftPath(fullPath))
                return ProcessTrustService.IsSignedBy(fullPath, "Microsoft");

            return false;
        }

        private static bool IsMicrosoftPath(string path)
        {
            foreach (var prefix in MicrosoftPaths)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}