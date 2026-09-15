using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Ipc;

namespace MediaMonitor.Service
{
    public class ServiceIpcServer
    {
        private readonly MediaMonitorEngine _engine;
        private readonly IpcCommandHandler _handler;
        private bool _running = true;

        public static bool ServiceLoggingEnabled = false;
        public static bool EmailSendingEnabled = true;

        public ServiceIpcServer(MediaMonitorEngine engine)
        {
            _engine = engine;
            _handler = new IpcCommandHandler(engine);

            CoreLog.IsLoggingEnabled = () => ServiceLoggingEnabled;
        }

        public void Start()
        {
            Log("IPC Server démarrage du thread serveur.");

            new Thread(ServerLoop)
            {
                IsBackground = true
            }.Start();
        }

        private void ServerLoop()
        {
            while (_running)
            {
                try
                {
                    Log("IPC : attente d'une connexion client...");

                    using var server = new NamedPipeServerStream(
                        "MediaMonitorPipe",
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None
                    );

                    server.WaitForConnection();
                    Log("IPC : client connecté.");

                    var reader = new StreamReader(server);
                    var writer = new StreamWriter(server) { AutoFlush = true };

                    string command = reader.ReadLine()?.Trim() ?? "";
                    Log("IPC : commande reçue = " + command);

                    _handler.Handle(command, writer, server);
                }
                catch (Exception ex)
                {
                    Log("ERREUR IPC : " + ex);
                }
            }

            Log("IPC ServerLoop terminé (running = false).");
        }

        private void Log(string message)
        {
            if (!ServiceLoggingEnabled)
                return;

            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                Directory.CreateDirectory(folder);

                string file = Path.Combine(folder, "MediaMonitor.Service.log");

                File.AppendAllText(
                    file,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}"
                );
            }
            catch { }
        }
    }
}