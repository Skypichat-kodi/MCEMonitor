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
            // Anti multi-instance
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_StopMonitor", out createdNew);
            if (!createdNew)
                return;

            string mode = args.Length > 0 ? args[0].ToLower() : "none";

            switch (mode)
            {
                case "shutdown":
                    LogHelper.SetLogFile("StopMonitor_Shutdown.log");
                    break;

                case "boot":
                    LogHelper.SetLogFile("StopMonitor_Boot.log");
                    break;

                default:
                    LogHelper.SetLogFile("StopMonitor_Debug.log");
                    break;
            }

            LogHelper.Clear();
            LogHelper.Write($"StopMonitor démarré. Mode = {mode}");

            try
            {
                var service = new StopMonitorService();

                switch (mode)
                {
                    case "shutdown":
                        await service.ProcessShutdownAsync();
                        break;

                    case "boot":
                        await service.ProcessBootAsync();
                        break;

                    default:
                        LogHelper.Write("Aucun mode spécifié.");
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
