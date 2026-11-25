using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace WakeMonitor
{
    /// <summary>
    /// Émission des paquets magiques de réveil.
    ///
    /// Un paquet magique, c'est six octets à FF suivis de l'adresse MAC de la
    /// machine visée répétée seize fois, envoyé en UDP sur le port 9 en diffusion
    /// locale. C'est la carte réseau de la machine éteinte qui l'interprète : aucun
    /// programme n'y tourne au moment du réveil.
    /// </summary>
    public static class WolSender
    {
        private const int PortWol = 9;

        /// <summary>
        /// Envoie un paquet à chaque adresse de la liste, et retourne celles qui ont
        /// été servies. Les échecs sont décrits dans <paramref name="erreurs"/>.
        /// </summary>
        public static List<string> EnvoyerAUxMacsAutorisees(out List<string> erreurs)
        {
            erreurs = new List<string>();
            var envoyees = new List<string>();

            var reglages = WakeMonitorSettings.Load();

            if (reglages.AllowedWolMacs.Count == 0)
            {
                erreurs.Add("aucune adresse MAC dans AllowedWolMacs.txt");
                return envoyees;
            }

            using var client = new UdpClient { EnableBroadcast = true };

            foreach (string mac in reglages.AllowedWolMacs.Distinct())
            {
                try
                {
                    byte[] paquet = Construire(mac);
                    client.Send(paquet, paquet.Length, new IPEndPoint(IPAddress.Broadcast, PortWol));
                    envoyees.Add(mac);
                }
                catch (Exception ex)
                {
                    erreurs.Add($"{mac} : {ex.Message}");
                }
            }

            return envoyees;
        }

        /// <summary>Construit le paquet magique d'une adresse MAC.</summary>
        private static byte[] Construire(string mac)
        {
            string propre = mac.Replace(":", "").Replace("-", "").Trim();

            if (propre.Length != 12)
                throw new FormatException("adresse MAC invalide");

            var octets = new byte[6];

            for (int i = 0; i < 6; i++)
                octets[i] = Convert.ToByte(propre.Substring(i * 2, 2), 16);

            var paquet = new byte[6 + 16 * 6];

            for (int i = 0; i < 6; i++)
                paquet[i] = 0xFF;

            for (int repetition = 0; repetition < 16; repetition++)
                Buffer.BlockCopy(octets, 0, paquet, 6 + repetition * 6, 6);

            return paquet;
        }
    }
}
