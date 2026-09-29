using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaMonitor.Core.Models;
using MediaMonitor.UI.Models;

namespace MediaMonitor.UI.Services
{
    public static class BackupLoader
    {
        public const string BackupDir  = @"C:\ProgramData\MCEMonitor\Backups";
        public const string BackupFile = "history_backup.json";

        public static string BackupPath => Path.Combine(BackupDir, BackupFile);

        public static BackupFileModel? Load()
        {
            try
            {
                if (!File.Exists(BackupPath)) return null;
                return JsonSerializer.Deserialize<BackupFileModel>(File.ReadAllText(BackupPath));
            }
            catch { return null; }
        }

        public static List<MediaUsageItem> Flatten(BackupFileModel backup) =>
            backup.Reports
                  .Where(r => r.Items != null)
                  .SelectMany(r => r.Items)
                  .OrderByDescending(i => i.Timestamp)
                  .ToList();
    }
}