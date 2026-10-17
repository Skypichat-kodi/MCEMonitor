using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace NetMonitor.Service.Services
{
    public class ThreatIntelService
    {
        private HashSet<string> _blacklist = new();
        private readonly object _lock = new();
        private readonly string _blacklistFile;
        private FileSystemWatcher? _watcher;

        public ThreatIntelService()
        {
            _blacklistFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor", "NetMonitor", "blacklist.txt");
        }

        public void Start()
        {
            LoadBlacklist();

            // Surveille le fichier pour recharger à chaud si on le modifie
            try
            {
                var dir = Path.GetDirectoryName(_blacklistFile);
                if (dir != null)
                {
                    Directory.CreateDirectory(dir);
                    _watcher = new FileSystemWatcher(dir, "blacklist.txt");
                    _watcher.NotifyFilter = NotifyFilters.LastWrite;
                    _watcher.Changed += (s, e) => {
                        System.Threading.Thread.Sleep(500); // Laisse le temps au fichier d'être écrit
                        LoadBlacklist();
                    };
                    _watcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"ThreatIntelService Watcher : {ex.Message}");
            }
        }

        public bool IsBlacklisted(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            lock (_lock) return _blacklist.Contains(ip);
        }

        private void LoadBlacklist()
        {
            try
            {
                if (!File.Exists(_blacklistFile))
                {
                    // Crée un fichier vide avec un commentaire explicatif
                    File.WriteAllText(_blacklistFile, 
                        "# Liste noire d'IP (une par ligne)\n" +
                        "# Exemple: 192.0.2.1\n" +
                        "# Les lignes commençant par # sont ignorées.\n");
                    return;
                }

                var newList = new HashSet<string>();
                foreach (var line in File.ReadLines(_blacklistFile))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;
                    
                    // Vérifie que c'est une IP valide (IPv4 ou IPv6)
                    if (IPAddress.TryParse(trimmed, out _))
                        newList.Add(trimmed);
                }

                lock (_lock)
                {
                    _blacklist = newList;
                }

                CoreLog.Info($"ThreatIntel : {_blacklist.Count} IPs chargées depuis la blacklist.");
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"Erreur chargement blacklist : {ex.Message}");
            }
        }

        public void Stop()
        {
            _watcher?.Dispose();
        }
    }
}