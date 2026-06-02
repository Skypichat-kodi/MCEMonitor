using System;

public class WakeInfo
{
    // --- Données brutes Windows ---
    public DateTime WakeTime { get; set; }
    public DateTime SleepTime { get; set; }
    public TimeSpan SleepDuration { get; set; }

    // Cause brute fournie par Windows (Power Button, Unknown, etc.)
    public string Cause { get; set; } = "Inconnue";

    // Nombre d'événements dans /lastwake (0 = Windows n'a rien enregistré)
    public int LastWakeCount { get; set; } = -1;

    // La carte réseau supporte-t-elle le WOL ? (wake_programmable)
    public bool NicSupportsWol { get; set; } = false;

    // --- Détection intelligente ---
    // True si on estime que le réveil est un WOL mal classé par Windows
    public bool IsLikelyWol { get; set; } = false;

    // Cause corrigée (si WOL probable)
    public string CorrectedCause
    {
        get
        {
            if (IsLikelyWol)
                return "Wake-on-LAN (classification Windows incorrecte)";

            return Cause;
        }
    }
}

