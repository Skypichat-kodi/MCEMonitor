using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SystemMonitor.Service
{
    /// <summary>Usage CPU d'un nom de processus, en pourcentage de la machine.</summary>
    public class ProcessCpuUsage
    {
        public string Name   { get; set; } = "";
        public double CpuPct { get; set; }
    }

    /// <summary>Un point d'une série de processus, pour le graphique.</summary>
    public class ProcessSeriesPoint
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
    }

    /// <summary>Série complète d'un processus, telle que renvoyée à l'interface.</summary>
    public class ProcessSeries
    {
        public string Name { get; set; } = "";
        public List<ProcessSeriesPoint> Points { get; set; } = new();
    }

    /// <summary>
    /// Mesure l'usage CPU de chaque processus par écart de temps processeur entre deux
    /// relevés.
    ///
    /// Contrairement à TopProcessesCollector, qui échantillonne pendant une seconde
    /// entière (acceptable pour un e-mail d'alerte, pas pour un tick de 5 s), cette
    /// mesure ne coûte quasiment rien : elle réutilise les compteurs que Windows tient
    /// déjà pour chaque processus.
    ///
    /// Les valeurs sont agrégées par nom : plusieurs chrome.exe donnent une seule ligne,
    /// celle que l'utilisateur attend.
    /// </summary>
    public class ProcessCpuTracker
    {
        private Dictionary<int, TimeSpan> _tempsPrecedents = new();
        private DateTime _relevePrecedent = DateTime.MinValue;
        private readonly double _coeurs = Math.Max(1, Environment.ProcessorCount);

        /// <summary>
        /// Relève l'usage CPU depuis le relevé précédent. Le premier appel ne renvoie
        /// rien : sans point de comparaison, aucun écart n'est calculable.
        /// </summary>
        public List<ProcessCpuUsage> Relever()
        {
            DateTime maintenant = DateTime.UtcNow;

            if (_relevePrecedent == DateTime.MinValue)
            {
                _tempsPrecedents = LireTemps();
                _relevePrecedent = maintenant;
                return new List<ProcessCpuUsage>();
            }

            double dureeMs = (maintenant - _relevePrecedent).TotalMilliseconds;
            if (dureeMs <= 0)
                return new List<ProcessCpuUsage>();

            var cumuls = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var nouveauxTemps = new Dictionary<int, TimeSpan>();

            foreach (Process processus in Process.GetProcesses())
            {
                try
                {
                    TimeSpan temps = processus.TotalProcessorTime;

                    if (_tempsPrecedents.TryGetValue(processus.Id, out TimeSpan precedent))
                    {
                        double pct = (temps - precedent).TotalMilliseconds / dureeMs / _coeurs * 100.0;

                        if (pct > 0)
                        {
                            string nom = processus.ProcessName;
                            cumuls[nom] = cumuls.TryGetValue(nom, out double deja) ? deja + pct : pct;
                        }
                    }

                    nouveauxTemps[processus.Id] = temps;
                }
                catch
                {
                    // Processus terminé pendant l'énumération, ou inaccessible.
                }
                finally
                {
                    processus.Dispose();
                }
            }

            // Les PID disparus sont oubliés : le dictionnaire reste à la taille réelle.
            _tempsPrecedents = nouveauxTemps;
            _relevePrecedent = maintenant;

            return cumuls
                .Select(kv => new ProcessCpuUsage { Name = kv.Key, CpuPct = kv.Value })
                .OrderByDescending(u => u.CpuPct)
                .ToList();
        }

        private static Dictionary<int, TimeSpan> LireTemps()
        {
            var temps = new Dictionary<int, TimeSpan>();

            foreach (Process processus in Process.GetProcesses())
            {
                try { temps[processus.Id] = processus.TotalProcessorTime; }
                catch { }
                finally { processus.Dispose(); }
            }

            return temps;
        }
    }
}
