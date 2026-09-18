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
                // 1. Mapping volume ? disque physique (via WMI)
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

                        // Associer le serial du disque physique
                        string letter = drive.Name.Replace(":\\", "").Replace(":", "").ToUpperInvariant();

                        if (volumeToPhysical.TryGetValue(letter, out var physical))
                        {
                            disk.PhysicalSerial = physical.Serial;
                            disk.PhysicalDiskNumber = physical.DiskNumber;
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
        //  Mapping : lettre de volume ? (DiskNumber, Serial du disque physique)
        // ------------------------------------------------------------------
        private static Dictionary<string, (int DiskNumber, string Serial)> GetVolumeToPhysicalMapping()
        {
            var result = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 1. Récupérer : Lettre de volume ? DiskNumber
                var volToDisk = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                using (var partSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT DriveLetter, DiskNumber FROM MSFT_Partition"))
                {
                    foreach (ManagementObject p in partSearcher.Get())
                    {
                        try
                        {
                            var letter = p["DriveLetter"]?.ToString();
                            var diskNum = p["DiskNumber"];

                            if (!string.IsNullOrEmpty(letter) && diskNum != null)
                            {
                                volToDisk[letter.ToUpperInvariant()] = Convert.ToInt32(diskNum);
                            }
                        }
                        catch { }
                    }
                }

                // 2. Récupérer : DiskNumber ? SerialNumber
                var diskToSerial = new Dictionary<int, string>();

                using (var diskSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT Number, SerialNumber FROM MSFT_Disk"))
                {
                    foreach (ManagementObject d in diskSearcher.Get())
                    {
                        try
                        {
                            var num = Convert.ToInt32(d["Number"]);
                            var serial = (d["SerialNumber"]?.ToString() ?? "").Trim();

                            if (!string.IsNullOrEmpty(serial))
                                diskToSerial[num] = serial;
                        }
                        catch { }
                    }
                }

                // 3. Fusion
                foreach (var kv in volToDisk)
                {
                    if (diskToSerial.TryGetValue(kv.Value, out var serial))
                    {
                        result[kv.Key] = (kv.Value, serial);
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur GetVolumeToPhysicalMapping : " + ex.Message);
            }

            return result;
        }
    }
}