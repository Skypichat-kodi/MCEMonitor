using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using SystemMonitor.Service.Ipc;

namespace SystemMonitor.Service
{
    public class ServiceIpcServer
    {
        private readonly SystemMonitorEngine _engine;
        private readonly SystemMonitorSettings _settings;
        private bool _running = true;

        private const string PipeName = "MCEMonitor_SystemMonitorPipe";

        public ServiceIpcServer(SystemMonitorEngine engine, SystemMonitorSettings settings)
        {
            _engine = engine;
            _settings = settings;
        }

        public void Start()
        {
            CoreLog.Write("IPC Server démarrage...");
            new Thread(ServerLoop) { IsBackground = true }.Start();
        }

        private void ServerLoop()
        {
            CoreLog.Write("IPC : ServerLoop démarré");

            while (_running)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None
                    );

                    server.WaitForConnection();

                    byte[] buffer = new byte[4096];
                    var readTask = server.ReadAsync(buffer, 0, buffer.Length);

                    if (!readTask.Wait(5000))
                        continue;

                    int bytesRead = readTask.Result;

                    if (bytesRead == 0)
                        continue;

                    string command = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    // On ne log PAS les commandes de polling (get-*)
                    if (!command.StartsWith("get-", StringComparison.OrdinalIgnoreCase))
                    {
                        CoreLog.Write($"IPC : commande reçue = '{command}'");
                    }

                    var handler = new IpcCommandHandler(_engine, _settings);
                    handler.Handle(command, server);
                }
                catch (Exception ex)
                {
                    CoreLog.Write("IPC ERREUR : " + ex.Message);
                }
            }

            CoreLog.Write("IPC : ServerLoop terminé");
        }

        public void Stop() => _running = false;
    }
}