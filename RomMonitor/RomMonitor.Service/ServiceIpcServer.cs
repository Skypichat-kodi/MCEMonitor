using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace RomMonitor.Service
{
    public class ServiceIpcServer
    {
        private readonly RomMonitorEngine _engine;
        private readonly RomMonitorSettings _settings;
        private bool _running = true;

        private const string PipeName = "MCEMonitor_RomMonitorPipe";

        public ServiceIpcServer(RomMonitorEngine engine, RomMonitorSettings settings)
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

                    // (logs IPC supprimés car trop bruyants)
                    server.WaitForConnection();

                    // Lecture brute
                    byte[] buffer = new byte[4096];
                    var readTask = server.ReadAsync(buffer, 0, buffer.Length);

                    if (!readTask.Wait(5000))
                    {
                        CoreLog.Write("IPC : timeout (pas de commande)");
                        continue;
                    }

                    int bytesRead = readTask.Result;

                    if (bytesRead == 0)
                    {
                        CoreLog.Write("IPC : client déconnecté sans commande");
                        continue;
                    }

                    string command = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    // On ne log PAS les commandes de polling (get-*), trop bruyantes
                    if (!command.StartsWith("get-", StringComparison.OrdinalIgnoreCase))
                    {
                        CoreLog.Write($"IPC : commande reçue = '{command}'");
                    }

                    var handler = new IpcCommandHandler(_engine, _settings);
                    handler.Handle(command, server);

                    if (!command.StartsWith("get-", StringComparison.OrdinalIgnoreCase))
                    {
                        CoreLog.Write("IPC : commande traitée");
                    }
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