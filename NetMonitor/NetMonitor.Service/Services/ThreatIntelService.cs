using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NetMonitor.Service.Services
{
    public class ThreatIntelService : IDisposable
    {
        // La liste elle-même est renouvelée toutes les 24 h, mais la VÉRIFICATION
        // tourne beaucoup plus souvent. Un cycle fixe de 24 h ratait le rattrapage
        // quand le réseau n'était pas encore prêt au démarrage du service, quand la
        // machine sortait de veille, ou après un arrêt : plus rien ne réessayait
        // avant le lendemain.
        private static readonly TimeSpan IntervalleRafraichissement = TimeSpan.FromHours(24);
        private static readonly TimeSpan IntervalleVerification     = TimeSpan.FromMinutes(15);
        private const string UrlModele =
            "https://raw.githubusercontent.com/stamparm/ipsum/master/levels/{0}.txt";

        private HashSet<string> _blacklist = new();
        private readonly object _lock = new();
        private readonly string _manualBlacklistFile;
        private readonly string _autoBlacklistFile;
        private Timer? _refreshTimer;
        private FileSystemWatcher? _watcher;
        private readonly HttpClient _httpClient = new();
        private volatile bool _disposed;

        // Le service ne reçoit pas une copie des réglages mais des accesseurs :
        // il lit ainsi toujours la valeur courante, y compris après une
        // modification faite depuis l'interface.
        private readonly Func<int> _lireNiveau;
        private readonly Func<DateTime> _lireDerniereMaj;
        private readonly Action<DateTime> _enregistrerMaj;

        private int _niveauEnCache = -1;      // niveau dont le cache local est issu
        private int _telechargementEnCours;   // 0/1 : évite deux téléchargements simultanés

        public ThreatIntelService(
            Func<int> lireNiveau,
            Func<DateTime> lireDerniereMaj,
            Action<DateTime> enregistrerMaj)
        {
            _lireNiveau = lireNiveau;
            _lireDerniereMaj = lireDerniereMaj;
            _enregistrerMaj = enregistrerMaj;

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

            // 3. Vérifie tout de suite si une mise à jour est due
            _ = VerifierEtRafraichirAsync();

            // 4. Revérifie régulièrement : c'est ce battement qui rattrape les
            //    mises à jour manquées (réseau absent au démarrage, veille, arrêt).
            //    Premier tir différé : l'étape 3 vient de le faire.
            _refreshTimer = new Timer(_ => { _ = VerifierEtRafraichirAsync(); }, null,
                IntervalleVerification, IntervalleVerification);
        }

        /// <summary>
        /// Télécharge la liste seulement si elle est due : cache absent, niveau
        /// demandé différent de celui du cache, ou liste de plus de 24 h. Appelée
        /// souvent, elle ne fait rien la plupart du temps.
        /// </summary>
        private async Task VerifierEtRafraichirAsync()
        {
            if (Interlocked.Exchange(ref _telechargementEnCours, 1) == 1) return;
            try
            {
                int niveau = Math.Clamp(_lireNiveau(), 1, 6);

                bool cacheAbsent  = !File.Exists(_autoBlacklistFile);
                bool niveauChange = _niveauEnCache != niveau;

                DateTime derniere = _lireDerniereMaj();
                bool perimee = derniere == DateTime.MinValue
                            || DateTime.UtcNow - derniere >= IntervalleRafraichissement;

                if (!cacheAbsent && !niveauChange && !perimee) return;

                string raison = cacheAbsent  ? "cache absent"
                              : niveauChange ? $"niveau {niveau} demandé"
                                             : "liste de plus de 24 h";

                CoreLog.Info($"ThreatIntel : mise à jour due ({raison}).");
                await RefreshAutoBlacklistAsync(niveau);
            }
            finally
            {
                Interlocked.Exchange(ref _telechargementEnCours, 0);
            }
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

                        // Le cache porte le niveau qui l'a produit : au redémarrage,
                        // on sait s'il correspond encore au réglage, sans rien
                        // télécharger inutilement.
                        if (ip.StartsWith("# ipsum level="))
                        {
                            if (int.TryParse(ip.Substring("# ipsum level=".Length), out int niveauCache))
                                _niveauEnCache = niveauCache;
                            continue;
                        }

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

        private async Task RefreshAutoBlacklistAsync(int niveau)
        {
            try
            {
                string url = string.Format(UrlModele, niveau);
                CoreLog.Info($"ThreatIntel : téléchargement de la liste IPsum (niveau {niveau})...");

                string content = await _httpClient.GetStringAsync(url);

                var newIps = new HashSet<string>();
                foreach (var line in content.Split('\n'))
                {
                    var ip = line.Trim();
                    if (!string.IsNullOrWhiteSpace(ip) && !ip.StartsWith("#"))
                        newIps.Add(ip);
                }

                // Sauvegarde locale pour les redémarrages hors-ligne. La première
                // ligne retient le niveau utilisé.
                await File.WriteAllTextAsync(_autoBlacklistFile,
                    $"# ipsum level={niveau}\n" + string.Join("\n", newIps));

                _niveauEnCache = niveau;

                // Recharge la blacklist globale (qui va fusionner manuel + auto)
                LoadBlacklist();

                // La date est enfin enregistrée : l'interface ne peut plus afficher
                // « inconnue » après une mise à jour réussie.
                _enregistrerMaj(DateTime.UtcNow);

                CoreLog.Info($"ThreatIntel : mise à jour réussie (niveau {niveau}). " +
                             $"{newIps.Count} IP dans la liste auto, " +
                             $"prochaine due dans {IntervalleRafraichissement.TotalHours:0} h.");
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"ThreatIntel download échoué (niveau {niveau}) : {ex.Message}. " +
                             $"Nouvelle tentative dans {IntervalleVerification.TotalMinutes:0} min, " +
                             $"cache conservé.");
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

        /// <summary>
        /// Libère le timer, le FileSystemWatcher et surtout le HttpClient.
        /// Stop() ne libérait que le timer et le watcher : le client HTTP et son pool
        /// de sockets restaient vivants jusquà la fin du processus.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _watcher?.Dispose(); }
            catch (Exception ex) { CoreLog.Warn($"ThreatIntelService.Dispose (watcher) : {ex.Message}"); }
            _watcher = null;

            try { _refreshTimer?.Dispose(); }
            catch (Exception ex) { CoreLog.Warn($"ThreatIntelService.Dispose (timer) : {ex.Message}"); }
            _refreshTimer = null;

            try { _httpClient.Dispose(); }
            catch (Exception ex) { CoreLog.Warn($"ThreatIntelService.Dispose (http) : {ex.Message}"); }
        }
    }
}