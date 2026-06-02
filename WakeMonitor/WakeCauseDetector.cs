using WakeMonitor;

namespace WakeMonitor
{
    public static class WakeCauseDetector
    {
        public static WolResult DetectWakeCause(WakeInfo wake, string localMac, WakeMonitorSettings opt)
        {
            // Si ce n’est pas un réveil WOL ? normal
            if (!WakeEventFilter.IsWolAttempt())
                return WolResult.Normal();

            // Détection moderne ou legacy
            return WolDetectionStrategy.Detect(wake, localMac, opt);
        }

        /// <summary>
        /// Détection intelligente d’un WOL probable même si Windows ment.
        /// </summary>
        private static bool IsLikelyWol(WakeInfo wake)
        {
            // 1) Windows n’a pas enregistré la source
            bool windowsWrong =
                string.IsNullOrWhiteSpace(wake.Cause) ||
                wake.Cause == "Inconnue" ||
                wake.Cause == "Unknown" ||
                wake.Cause == "Power Button" ||
                wake.LastWakeCount == 0;

            // 2) La carte réseau supporte le WOL
            bool nicCapable = wake.NicSupportsWol;

            // 3) Le réveil est très proche de l’endormissement (WOL typique)
            bool timingMatches = wake.SleepDuration < TimeSpan.FromSeconds(10);

            return windowsWrong && nicCapable && timingMatches;
        }

        public static string GetProbableWakeCause(WakeInfo wake, string localMac, WakeMonitorSettings opt)
        {
            // Détection WOL classique
            var result = DetectWakeCause(wake, localMac, opt);

            // Si ce n’est pas un WOL détecté par la stratégie ? cause Windows
            if (result == null || !result.IsWol)
            {
                // Détection intelligente
                wake.IsLikelyWol = IsLikelyWol(wake);

                if (wake.IsLikelyWol)
                    return "Wake-on-LAN (classification Windows incorrecte)";

                return wake.Cause ?? "Réveil normal";
            }

            // Si WOL détecté normalement
            wake.IsLikelyWol = true;
            return result.Tag;
        }
    }
}

