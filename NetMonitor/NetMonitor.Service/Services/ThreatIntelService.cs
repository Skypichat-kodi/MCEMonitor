using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NetMonitor.Service.Services
{
    public class ThreatIntelService
    {
        private HashSet<string> _blacklist = new();
        private readonly object _lock = new();
        private readonly string _manualBlacklistFile;
        private readonly string _autoBlacklistFile;
        private Timer? _refreshTimer;
        private FileSystemWatcher? _watcher;
        private readonly HttpClient _httpClient = new();

        public ThreatIntelService()
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor", "NetMonitor");
            _manualBlacklistFile = Path.Combine(baseDir, "blacklist.txt");
            _autoBlacklistFile = Path.Combine(baseDir, "blacklist_auto.txt");
        }

        public void Start()
        {
            // 1. Charge la blacklist (manuelle + cache auto)
            LoadBlacklist();

            // 2. Surveille le fichier manuel pour recharger à chaud
            try
            {
                var dir = Path.GetDirectoryName(_manualBlacklistFile);
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
                CoreLog.Warn($"ThreatIntel Watcher : {ex.Message}");
            }

            // 3. Télécharge la liste auto en arrière-plan
            _ = RefreshAutoBlacklistAsync();

            // 4. Rafraîchit toutes les 24h
            _refreshTimer = new Timer(async _ => await RefreshAutoBlacklistAsync(), null,
                TimeSpan.FromHours(24), TimeSpan.FromHours(24));
        }

        /// <summary>
        /// Charge la blacklist globale (manuel + auto) en mémoire.
        /// Appelé au démarrage et à chaque modification de blacklist.txt.
        /// </summary>
        private void LoadBlacklist()
        {
            try
            {
                var tempList = new HashSet<string>();

                // --- 1. Charge la liste manuelle ---
                if (!File.Exists(_manualBlacklistFile))
                {
                    File.WriteAllText(_manualBlacklistFile, 
                        "# Liste noire manuelle (une IP par ligne)\n" +
                        "# Les IPs ajoutées ici sont bloquées par le détecteur BlacklistIpDetector.\n");
                }
                else
                {
                    foreach (var line in File.ReadLines(_manualBlacklistFile))
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                            tempList.Add(trimmed);
                    }
                }

                // --- 2. Charge la liste auto (cache local) ---
                if (File.Exists(_autoBlacklistFile))
                {
                    foreach (var line in File.ReadLines(_autoBlacklistFile))
                    {
                        var ip = line.Trim();
                        if (!string.IsNullOrWhiteSpace(ip) && !ip.StartsWith("#"))
                            tempList.Add(ip);
                    }
                }

                // --- 3. Met à jour la liste globale ---
                lock (_lock)
                {
                    _blacklist = tempList;
                }

                CoreLog.Info($"ThreatIntel : {_blacklist.Count} IP chargées au total.");
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"Erreur chargement blacklist : {ex.Message}");
            }
        }

        private async Task RefreshAutoBlacklistAsync()
        {
            try
            {
                CoreLog.Info("ThreatIntel : téléchargement de la liste IPsum (niveau 3)...");
                string content = await _httpClient.GetStringAsync("https://raw.githubusercontent.com/stamparm/ipsum/master/levels/3.txt");
                
                var newIps = new HashSet<string>();
                foreach (var line in content.Split('\n'))
                {
                    var ip = line.Trim();
                    if (!string.IsNullOrWhiteSpace(ip) && !ip.StartsWith("#"))
                        newIps.Add(ip);
                }

                // Sauvegarde locale pour les redémarrages hors-ligne
                await File.WriteAllTextAsync(_autoBlacklistFile, string.Join("\n", newIps));
                
                // Recharge la blacklist globale (qui va fusionner manuel + auto)
                LoadBlacklist();
                
                CoreLog.Info($"ThreatIntel : mise à jour réussie. {newIps.Count} IP dans la liste auto.");
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"ThreatIntel download échoué : {ex.Message}. Utilisation du cache.");
            }
        }

        public bool IsBlacklisted(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            lock (_lock) return _blacklist.Contains(ip);
        }

        public void Stop()
        {
            _watcher?.Dispose();
            _refreshTimer?.Dispose();
        }
    }
}