using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace RomMonitor.Service
{
    public static class DiskSpaceChecker
    {
        public static List<DiskInfo> GetDisks()
        {
            var result = new List<DiskInfo>();

            try
            {
                var volumeToPhysical = GetVolumeToPhysicalMapping();

                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady)
                            continue;

                        if (drive.DriveType != DriveType.Fixed &&
                            drive.DriveType != DriveType.Removable)
                            continue;

                        var disk = new DiskInfo
                        {
                            Name = drive.Name,
                            Label = drive.VolumeLabel,
                            DriveType = drive.DriveType.ToString(),
                            TotalBytes = drive.TotalSize,
                            FreeBytes = drive.TotalFreeSpace
                        };

                        string letter = drive.Name.Replace(":\\", "").Replace(":", "").ToUpperInvariant();

                        if (volumeToPhysical.TryGetValue(letter, out var physical))
                        {
                            disk.PhysicalSerial = physical.Serial;
                            disk.PhysicalDiskNumber = physical.DiskNumber;
                            disk.PhysicalSizeGo = physical.SizeGo;    // ??
                        }

                        result.Add(disk);
                    }
                    catch (Exception ex)
                    {
                        CoreLog.Write($"Erreur disque {drive.Name} : {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur GetDisks : " + ex.Message);
            }

            return result;
        }

        // ------------------------------------------------------------------
        //  Mapping : lettre ? (DiskNumber, Serial, SizeGo du disque physique)
        //  Compatible Windows 10 ET Windows 11 (via Win32_* classes)
        // ------------------------------------------------------------------
        private static Dictionary<string, (int DiskNumber, string Serial, double SizeGo)>
            GetVolumeToPhysicalMapping()
        {
            var result = new Dictionary<string, (int, string, double)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // a) LogicalDisk ? Partition
                var letterToPartition = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
                {
                    foreach (ManagementObject m in searcher.Get())
                    {
                        try
                        {
                            string dependent = m["Dependent"]?.ToString() ?? "";
                            string antecedent = m["Antecedent"]?.ToString() ?? "";

                            string letter = ExtractLetter(dependent);
                            string partition = ExtractDiskPartition(antecedent);

                            if (!string.IsNullOrEmpty(letter) && !string.IsNullOrEmpty(partition))
                                letterToPartition[letter] = partition;
                        }
                        catch { }
                    }
                }

                // b) Partition ? DiskDrive
                var partitionToDrive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition"))
                {
                    foreach (ManagementObject m in searcher.Get())
                    {
                        try
                        {
                            string antecedent = m["Antecedent"]?.ToString() ?? "";
                            string dependent = m["Dependent"]?.ToString() ?? "";

                            string drive = ExtractDiskDrive(antecedent);
                            string partition = ExtractDiskPartition(dependent);

                            if (!string.IsNullOrEmpty(drive) && !string.IsNullOrEmpty(partition))
                                partitionToDrive[partition] = drive;
                        }
                        catch { }
                    }
                }

                // c) DiskDrive ? (Index, Serial, SizeGo)
                var driveToInfo = new Dictionary<string, (int Index, string Serial, double SizeGo)>(StringComparer.OrdinalIgnoreCase);

                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Index, SerialNumber, Size FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject m in searcher.Get())
                    {
                        try
                        {
                            string deviceId = m["DeviceID"]?.ToString() ?? "";
                            int index = Convert.ToInt32(m["Index"] ?? 0);
                            string serial = (m["SerialNumber"]?.ToString() ?? "").Trim();
                            long sizeBytes = Convert.ToInt64(m["Size"] ?? 0);
                            double sizeGo = sizeBytes / 1024.0 / 1024.0 / 1024.0;

                            if (!string.IsNullOrEmpty(deviceId))
                                driveToInfo[deviceId] = (index, serial, sizeGo);
                        }
                        catch { }
                    }
                }

                // 2. Fusion : lettre ? (DiskNumber, Serial, SizeGo)
                foreach (var kv in letterToPartition)
                {
                    string letter = kv.Key;
                    string partition = kv.Value;

                    if (!partitionToDrive.TryGetValue(partition, out var drive))
                        continue;

                    if (!driveToInfo.TryGetValue(drive, out var info))
                        continue;

                    result[letter] = (info.Index, info.Serial, info.SizeGo);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur GetVolumeToPhysicalMapping : " + ex.Message);
            }

            return result;
        }

        // ------------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------------
        private static string ExtractLetter(string wmiPath)
        {
            int idx = wmiPath.IndexOf("DeviceID=");
            if (idx < 0) return "";

            string s = wmiPath.Substring(idx + 9).Trim('"', '\\');
            if (s.Length >= 1)
                return s.Substring(0, 1).ToUpperInvariant();

            return "";
        }

        private static string ExtractDiskPartition(string wmiPath)
        {
            int idx = wmiPath.IndexOf("DeviceID=");
            if (idx < 0) return "";

            return wmiPath.Substring(idx + 9).Trim('"', '\\');
        }

        private static string ExtractDiskDrive(string wmiPath)
        {
            int idx = wmiPath.IndexOf("DeviceID=");
            if (idx < 0) return "";

            return wmiPath.Substring(idx + 9).Trim('"', '\\').Replace("\\\\", "\\");
        }
    }
}