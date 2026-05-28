using System;
using System.Threading;
using System.Threading.Tasks;

namespace StopMonitor
{
    internal class Program
    {
        private static Mutex _mutex;   // AJOUT

        static async Task Main()
        {
            // AJOUT : Mutex anti-multi-instance
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_StopMonitor", out createdNew);
            if (!createdNew)
                return;

            LogHelper.Write("StopMonitor démarré.");

            try
            {
                var service = new StopMonitorService();
                await service.SendShutdownEmail();
            }
            catch (Exception ex)
            {
                LogHelper.Write("ERREUR : " + ex.Message);
            }

            LogHelper.Write("StopMonitor terminé.");
        }
    }
}

