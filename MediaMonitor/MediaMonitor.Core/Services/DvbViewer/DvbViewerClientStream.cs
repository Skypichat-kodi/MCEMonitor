using System;

namespace MediaMonitor.Core.DvbViewer
{
    public class DvbViewerClientStream
    {
        /// <summary>
        /// Colonne "Client" dans l'UI.
        /// Exemple : "192.168.1.34" ou "DVB-T Tuner/Demod (2)"
        /// </summary>
        public string Client { get; set; }

        /// <summary>
        /// Colonne "Type" dans l'UI.
        /// Exemple : "TV" ou "REC F3 Rhône-Alpes"
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Colonne "Nom" dans l'UI.
        /// Exemple : "LiveTV (W)" ou "Des racines et des ailes..."
        /// </summary>
        public string Nom { get; set; }

        public override string ToString()
            => $"{Client} | {Type} | {Nom}";
    }
}

