using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SystemMonitor.Service
{
    public class BsodHistory
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "SystemMonitor"
        );

        private static readonly string FilePath = Path.Combine(Folder, "bsod_history.json");

        private List<BsodInfo> _bsods = new();

        public BsodHistory()
        {
            Load();
        }

        public List<BsodInfo> GetAll() => _bsods;

        public void Add(BsodInfo bsod)
        {
            // Éviter les doublons : même timestamp + même code d'arrêt
            if (_bsods.Any(b => b.Timestamp == bsod.Timestamp && b.BugCheckCode == bsod.BugCheckCode))
                return;

            _bsods.Add(bsod);
            Save();

            CoreLog.Write($"[BSOD] {bsod.Timestamp:yyyy-MM-dd HH:mm:ss} | {bsod.BugCheckCode} | {bsod.BugCheckName}");
        }

        public void Clear()
        {
            try
            {
                _bsods.Clear();
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, "[]");
                CoreLog.Write("Historique BSOD purgé");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Clear BsodHistory : " + ex.Message);
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;

                string json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<List<BsodInfo>>(json);

                if (loaded != null)
                    _bsods = loaded;
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Load BsodHistory : " + ex.Message);
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);

                // Garder les 1 an d'historique
                var limit = DateTime.Now.AddYears(-1);
                _bsods = _bsods.Where(b => b.Timestamp >= limit).ToList();

                string json = JsonSerializer.Serialize(_bsods, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Save BsodHistory : " + ex.Message);
            }
        }
    }
}