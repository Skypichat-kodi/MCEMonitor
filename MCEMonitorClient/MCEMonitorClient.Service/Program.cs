using System;
using System.Threading;
using MCEMonitorClient.Service.Ipc;
using MCEMonitorClient.Service.Logs;
using MCEMonitorClient.Service.Services;

namespace MCEMonitorClient.Service
{
    internal static class Program
    {
        private static Mutex? _mutex;
        private static PollingEngine? _engine;
        private static ServiceIpcServer? _ipc;

        static void Main(string[] args)
        {
            // Mutex global : une seule instance
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitorClient_Service", out createdNew);

            if (!createdNew)
                return;

            // Init logs
            CoreLog.Clear();
            CoreLog.Write("=== MCEMonitorClient.Service démarré ===");

            // Charger la config
            var config = ServerConfigStore.Load();
            CoreLog.Write($"Config chargée : {config.Servers.Count} serveur(s), intervalle {config.PollIntervalSeconds}s");

            if (config.Servers.Count == 0)
            {
                CoreLog.Write("Aucun serveur configuré. En attente...");
            }

            // Démarrer le moteur de polling
            _engine = new PollingEngine();
            _engine.Start();

            // Démarrer le serveur IPC
            _ipc = new ServiceIpcServer(_engine);
            _ipc.Start();

            CoreLog.Write("Service en attente (Thread.Sleep Infinite).");
            Thread.Sleep(Timeout.Infinite);
        }
    }
}