using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;

namespace RomMonitor.Service
{
    /// <summary>
    /// Charge de lecture/écriture d'un disque logique sur l'intervalle de mesure.
    /// </summary>
    public class DiskIoInfo
    {
        public string Name { get; set; } = "";        // « C: »
        public double PercentBusy { get; set; }       // % du temps où le disque était occupé
        public double ReadMoPerSec { get; set; }
        public double WriteMoPerSec { get; set; }
        public double QueueLength { get; set; }
    }

    /// <summary>
    /// Mesure la charge des disques logiques via les compteurs de performance WMI.
    ///
    /// PercentDiskTime est le critère de saturation : lorsque le disque ne suit plus,
    /// il reste à 100 % quel que soit son débit réel — ce qui vaut aussi bien pour un
    /// disque mécanique que pour un NVMe.
    /// </summary>
    public static class DiskIoChecker
    {
        private static readonly Regex NomDeLecteur = new("^[A-Za-z]:$", RegexOptions.Compiled);

        public static List<DiskIoInfo> GetIo()
        {
            var resultats = new List<DiskIoInfo>();

            try
            {
                using var requete = new ManagementObjectSearcher(
                    "SELECT Name, PercentDiskTime, DiskReadBytesPerSec, DiskWriteBytesPerSec, " +
                    "CurrentDiskQueueLength FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk");

                foreach (ManagementBaseObject brut in requete.Get())
                {
                    try
                    {
                        string nom = brut["Name"]?.ToString() ?? string.Empty;

                        // On ne garde que les lecteurs (« C: ») : les instances _Total et
                        // HarddiskVolume* n'ont pas de sens pour l'utilisateur.
                        if (!NomDeLecteur.IsMatch(nom))
                            continue;

                        resultats.Add(new DiskIoInfo
                        {
                            Name          = nom,
                            PercentBusy   = VersDouble(brut["PercentDiskTime"]),
                            ReadMoPerSec  = VersDouble(brut["DiskReadBytesPerSec"]) / 1024.0 / 1024.0,
                            WriteMoPerSec = VersDouble(brut["DiskWriteBytesPerSec"]) / 1024.0 / 1024.0,
                            QueueLength   = VersDouble(brut["CurrentDiskQueueLength"])
                        });
                    }
                    catch (Exception ex)
                    {
                        CoreLog.Write($"Erreur mesure E/S {brut["Name"]} : {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur DiskIoChecker : " + ex.Message);
            }

            return resultats;
        }

        private static double VersDouble(object? valeur)
        {
            if (valeur is null)
                return 0;

            return double.TryParse(
                valeur.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double resultat)
                ? resultat
                : 0;
        }
    }
}
