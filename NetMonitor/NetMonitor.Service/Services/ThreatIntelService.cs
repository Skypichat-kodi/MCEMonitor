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
            LoadManualBlacklist();
            LoadCache(); // Charge le cache local au démarrage
            _ = RefreshAutoBlacklistAsync(); // Télécharge en arrière-plan

            // Rafraîchit toutes les 24h
            _refreshTimer = new Timer(async _ => await RefreshAutoBlacklistAsync(), null,
                TimeSpan.FromHours(24), TimeSpan.FromHours(24));
        }

        private void LoadManualBlacklist()
        {
            try
            {
                if (!File.Exists(_manualBlacklistFile))
                {
                    File.WriteAllText(_manualBlacklistFile, 
                        "# Liste noire manuelle (une IP par ligne)\n" +
                        "# Les IPs ajoutées ici sont bloquées par le détecteur BlacklistIpDetector.\n");
                    return;
                }

                foreach (var line in File.ReadLines(_manualBlacklistFile))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                        lock (_lock) _blacklist.Add(trimmed);
                }
            }
            catch (Exception ex) { CoreLog.Warn($"ThreatIntel manual : {ex.Message}"); }
        }

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(_autoBlacklistFile)) return;
                
                int count = 0;
                foreach (var line in File.ReadLines(_autoBlacklistFile))
                {
                    var ip = line.Trim();
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        lock (_lock) _blacklist.Add(ip);
                        count++;
                    }
                }
                CoreLog.Info($"ThreatIntel : {count} IP chargées depuis le cache local.");
            }
            catch { }
        }

        private async Task RefreshAutoBlacklistAsync()
        {
            try
            {
                CoreLog.Info("ThreatIntel : téléchargement de la liste IPsum (niveau 3)...");
                // Niveau 3 = IP présentes sur au moins 3 listes (bon compromis)
                string content = await _httpClient.GetStringAsync("https://raw.githubusercontent.com/stamparm/ipsum/master/levels/3.txt");
                
                var newIps = new HashSet<string>();
                foreach (var line in content.Split('\n'))
                {
                    var ip = line.Trim();
                    if (!string.IsNullOrWhiteSpace(ip) && !ip.StartsWith("#"))
                        newIps.Add(ip);
                }

                // Remplace la partie auto dans la blacklist globale
                lock (_lock)
                {
                    // On retire les anciennes IP auto (simplifié : on recharge tout proprement)
                    _blacklist.Clear();
                    LoadManualBlacklistInternal();
                    _blacklist.UnionWith(newIps);
                }

                // Sauvegarde locale pour les redémarrages hors-ligne
                await File.WriteAllTextAsync(_autoBlacklistFile, string.Join("\n", newIps));
                CoreLog.Info($"ThreatIntel : mise à jour réussie. {newIps.Count} IP dans la liste auto.");
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"ThreatIntel download échoué : {ex.Message}. Utilisation du cache.");
            }
        }

        private void LoadManualBlacklistInternal()
        {
            if (!File.Exists(_manualBlacklistFile)) return;
            foreach (var line in File.ReadLines(_manualBlacklistFile))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                    _blacklist.Add(trimmed);
            }
        }

        public bool IsBlacklisted(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            lock (_lock) return _blacklist.Contains(ip);
        }

        public void Stop() => _refreshTimer?.Dispose();
    }
}