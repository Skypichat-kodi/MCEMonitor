using System;
using System.Collections.Generic;

namespace SystemMonitor.Service
{
    public enum AlertType
    {
        CpuHigh,
        RamHigh,
        TempHigh,
        GpuTempHigh,

        /// <summary>Processus lourd et durable. Trace dans l historique, sans e-mail.</summary>
        ProcessHigh
    }

    public class Alert
    {
        public DateTime Timestamp { get; set; }
        public AlertType Type { get; set; }
        public string Severity { get; set; } = "";
        public string Target { get; set; } = "";
        public string Message { get; set; } = "";
        public bool EmailSent { get; set; }

        /// <summary>
        /// Courbe de la période concernée, quand elle est disponible.
        ///
        /// Le graphique de l'interface est glissant : au moment où l'alerte entre dans
        /// l'historique, sa courbe n'y est déjà plus. Sans ces points, la ligne
        /// d'historique ne serait plus qu'un texte, sans rien à montrer plus tard.
        /// </summary>
        public List<ProcessSeriesPoint>? CpuSeries { get; set; }
    }
}