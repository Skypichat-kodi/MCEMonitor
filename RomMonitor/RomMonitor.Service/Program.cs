using System;
using System.Threading;

namespace RomMonitor.Service
{
    internal static class Program
    {
        private static Mutex _mutex;
        private static RomMonitorEngine _engine;
        private static ServiceIpcServer _ipc;

        static void Main(string[] args)
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_RomMonitor", out createdNew);

            if (!createdNew)
            {
                CoreLog.Write("RomMonitor.Service déjà en cours. Sortie.");
                return;
            }

            // Init logs
            CoreLog.Clear();
            CoreLog.Write("=== RomMonitor.Service démarré ===");

            // Charger config
            var settings = RomMonitorSettings.Load();
            CoreLog.Write($"Config : Interval={settings.Interval}min, AlertOnSmartFailure={settings.AlertOnSmartFailure}");

            // Démarrer engine
            _engine = new RomMonitorEngine(settings);
            _engine.Start();

            // Démarrer IPC
            _ipc = new ServiceIpcServer(_engine, settings);
            _ipc.Start();

            CoreLog.Write("Service en attente (Thread.Sleep Infinite).");
            Thread.Sleep(Timeout.Infinite);
        }
    }
}