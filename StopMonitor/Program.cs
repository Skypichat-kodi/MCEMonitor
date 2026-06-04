using System;
using System.Threading;
using System.Threading.Tasks;

namespace StopMonitor
{
    internal class Program
    {
        private static Mutex _mutex;

        static async Task Main(string[] args)
        {
            // Mutex anti-multi-instance
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_StopMonitor", out createdNew);
            if (!createdNew)
                return;

            string mode = args.Length > 0 ? args[0].ToLower() : "none";

            // Choisir le bon fichier log AVANT d'écrire
            switch (mode)
            {
                case "shutdown":
                    LogHelper.SetLogFile("StopMonitor_Stop.log");
                    break;

                case "boot":
                    LogHelper.SetLogFile("StopMonitor_Boot.log");
                    break;

                default:
                    LogHelper.SetLogFile("StopMonitor_Debug.log");
                    break;
            }

            // ?? Vider le log AVANT toute écriture (Option A)
            LogHelper.Clear();

            LogHelper.Write($"StopMonitor démarré. Mode = {mode}");

            try
            {
                var service = new StopMonitorService();

                switch (mode)
                {
                    case "shutdown":
                        // Laisser Windows écrire 1074 / 6006 / 6008
                        await Task.Delay(5000);
                        await service.SendShutdownEmail();
                        break;

                    case "boot":
                        // Laisser Windows écrire 6005 / 12 / 6009 / 41 / 1001
                        await Task.Delay(5000);
                        await service.SendCrashEmail();
                        break;

                    default:
                        LogHelper.Write("Aucun mode spécifié. Rien à faire.");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Write("ERREUR : " + ex.Message);
            }

            LogHelper.Write("StopMonitor terminé.");
        }
    }
}

