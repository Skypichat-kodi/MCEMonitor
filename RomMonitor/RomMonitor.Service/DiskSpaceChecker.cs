using System;
using System.Collections.Generic;
using System.IO;

namespace RomMonitor.Service
{
    /// <summary>
    /// Énumère les disques et récupère l'espace libre.
    /// </summary>
    public static class DiskSpaceChecker
    {
        public static List<DiskInfo> GetDisks()
        {
            var result = new List<DiskInfo>();

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady)
                            continue;

                        // Ignorer CD-ROM, disquettes, etc.
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
    }
}