using System.Collections.Generic;

namespace MCEMonitorClient.Config.Models
{
    /// <summary>
    /// Configuration globale du client : liste de serveurs + options.
    /// </summary>
    public class ServersConfig
    {
        /// <summary>Intervalle de polling en secondes.</summary>
        public int PollIntervalSeconds { get; set; } = 30;

        /// <summary>Notifier quand un serveur tombe KO.</summary>
        public bool NotifyOnStateChange { get; set; } = true;

        /// <summary>Jouer un son sur les alertes critiques.</summary>
        public bool PlaySoundOnCritical { get; set; } = true;

        /// <summary>Lancer le service au démarrage de Windows.</summary>
        public bool StartWithWindows { get; set; } = true;

        /// <summary>Liste des serveurs.</summary>
        public List<ServerEntry> Servers { get; set; } = new();
    }
}