using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SystemMonitor.Service
{
    public static class BsodReader
    {
        private const string MinidumpFolder = @"C:\Windows\Minidump";

        /// <summary>
        /// Lit l'Event Log Windows et renvoie les BSOD récents.
        /// </summary>
        public static List<BsodInfo> GetRecentBsods(int maxAgeDays = 30)
        {
            var result = new List<BsodInfo>();

            try
            {
                var cutoff = DateTime.Now.AddDays(-maxAgeDays);

                using var log = new EventLog("System");

                foreach (EventLogEntry entry in log.Entries)
                {
                    // Trop vieux, on saute
                    if (entry.TimeGenerated < cutoff)
                        continue;

                    // On cherche les BugCheck (Event ID 1001) de WER
                    bool isBugCheck =
                        (entry.Source == "Microsoft-Windows-WER-SystemErrorReporting" && entry.InstanceId == 1001) ||
                        (entry.Source == "BugCheck" && entry.InstanceId == 1001);

                    if (!isBugCheck)
                        continue;

                    var bsod = ParseBugCheckEntry(entry);
                    if (bsod != null)
                        result.Add(bsod);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur GetRecentBsods : " + ex.Message);
            }

            return result.OrderByDescending(b => b.Timestamp).ToList();
        }

        /// <summary>
        /// Parse une entrée BugCheck pour extraire code + paramètres.
        /// </summary>
        private static BsodInfo? ParseBugCheckEntry(EventLogEntry entry)
        {
            try
            {
                string message = entry.Message ?? "";

                var bsod = new BsodInfo
                {
                    Timestamp = entry.TimeGenerated,
                    Source = entry.Source
                };

                // Pattern : "Le système a redémarré à la suite d'une vérification de bogue. Code : 0x0000007e"
                // ou : "The computer has rebooted from a bugcheck. The bugcheck was: 0x0000007e"
                var matchCode = Regex.Match(message, @"0x[0-9a-fA-F]{8}");
                if (matchCode.Success)
                    bsod.BugCheckCode = matchCode.Value.ToLower();

                // Recherche des paramètres (0x..., 0x..., ...)
                var matches = Regex.Matches(message, @"0x[0-9a-fA-F]+");
                if (matches.Count >= 5)
                {
                    var parameters = new List<string>();
                    for (int i = 1; i < Math.Min(5, matches.Count); i++)
                        parameters.Add(matches[i].Value);

                    bsod.Parameters = string.Join(", ", parameters);
                }

                // Nom lisible du bugcheck
                bsod.BugCheckName = GetBugCheckName(bsod.BugCheckCode);

                // Chercher le minidump correspondant (le plus proche en date)
                bsod.DumpPath = FindDumpForCrash(entry.TimeGenerated);
                bsod.DumpExists = !string.IsNullOrEmpty(bsod.DumpPath) && File.Exists(bsod.DumpPath);

                // ? Parser le dump
                if (bsod.DumpExists)
                {
                    try
                    {
                        var fault = MinidumpParser.Analyze(bsod.DumpPath);
                        if (fault != null)
                        {
                            if (fault.IsKernelDump)
                            {
                                // Kernel dump : le pilote n'est pas extractible
                                bsod.FaultyModule = "Kernel dump (pilote non identifiable sans WinDbg)";
                                bsod.FaultAddress = fault.BugCheckParameters;
                            }
                            else
                            {
                                bsod.FaultyModule = fault.FaultyModuleName;
                                bsod.FaultAddress = $"0x{fault.FaultAddress:X16}";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CoreLog.Write("Erreur parsing dump : " + ex.Message);
                    }
                }

                return bsod;
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur ParseBugCheckEntry : " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Cherche un fichier .dmp dans C:\Windows\Minidump proche de la date du crash.
        /// Tolérance : ±5 minutes.
        /// </summary>
        private static string FindDumpForCrash(DateTime crashTime)
        {
            try
            {
                if (!Directory.Exists(MinidumpFolder))
                    return "";

                var files = Directory.GetFiles(MinidumpFolder, "*.dmp");

                string bestFile = "";
                TimeSpan bestDelta = TimeSpan.MaxValue;

                foreach (var file in files)
                {
                    var fileTime = File.GetLastWriteTime(file);
                    var delta = (fileTime - crashTime).Duration();

                    if (delta < TimeSpan.FromMinutes(5) && delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestFile = file;
                    }
                }

                return bestFile;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Table des codes BugCheck les plus courants ? nom lisible.
        /// </summary>
        private static string GetBugCheckName(string code)
        {
            return code.ToLower() switch
            {
                "0x0000000a" => "IRQL_NOT_LESS_OR_EQUAL",
                "0x0000001e" => "KMODE_EXCEPTION_NOT_HANDLED",
                "0x00000024" => "NTFS_FILE_SYSTEM",
                "0x0000002e" => "DATA_BUS_ERROR",
                "0x0000003b" => "SYSTEM_SERVICE_EXCEPTION",
                "0x00000050" => "PAGE_FAULT_IN_NONPAGED_AREA",
                "0x0000007e" => "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
                "0x0000007f" => "UNEXPECTED_KERNEL_MODE_TRAP",
                "0x0000009f" => "DRIVER_POWER_STATE_FAILURE",
                "0x000000c2" => "BAD_POOL_CALLER",
                "0x000000d1" => "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
                "0x000000d5" => "DRIVER_PAGE_FAULT_IN_FREED_SPECIAL_POOL",
                "0x000000ea" => "THREAD_STUCK_IN_DEVICE_DRIVER",
                "0x000000ef" => "CRITICAL_PROCESS_DIED",
                "0x000000f4" => "CRITICAL_OBJECT_TERMINATION",
                "0x00000109" => "CRITICAL_STRUCTURE_CORRUPTION",
                "0x00000116" => "VIDEO_TDR_FAILURE",
                "0x00000124" => "WHEA_UNCORRECTABLE_ERROR",
                "0x00000133" => "DPC_WATCHDOG_VIOLATION",
                "0x00000139" => "KERNEL_SECURITY_CHECK_FAILURE",
                "0x00000154" => "UNEXPECTED_STORE_EXCEPTION",
                "0x00000191" => "PF_DETECTED_CORRUPTION",
                "0x000001c8" => "ASSERT_FAILED",
                "0x1000007e" => "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED (x64)",
                "0xc000021a" => "STATUS_SYSTEM_PROCESS_TERMINATED",
                _ => "UNKNOWN_BUGCHECK"
            };
        }
    }
}