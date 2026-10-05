using System;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using MCEMonitorClient.Service.Logs;
using MCEMonitorClient.Service.Services;

namespace MCEMonitorClient.Service.Ipc
{
    /// <summary>
    /// Serveur IPC pour communiquer avec le Tray.
    /// </summary>
    public class ServiceIpcServer
    {
        private readonly PollingEngine _engine;
        private Thread? _thread;
        private bool _running;

        public const string PipeName = "MCEMonitor_ClientPipe";

        public ServiceIpcServer(PollingEngine engine)
        {
            _engine = engine;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            _thread = new Thread(ServerLoop) { IsBackground = true };
            _thread.Start();

            CoreLog.Write("ServiceIpcServer démarré");
        }

        public void Stop()
        {
            _running = false;
            CoreLog.Write("ServiceIpcServer arrêté");
        }

        private void ServerLoop()
        {
            while (_running)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None);

                    server.WaitForConnection();

                    byte[] buffer = new byte[4096];
                    int bytesRead = server.Read(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                        continue;

                    string command = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    CoreLog.Write($"IPC reçu : '{command}'");

                    HandleCommand(command, server);
                }
                catch (Exception ex)
                {
                    if (_running)
                        CoreLog.Write("IPC ERROR : " + ex.Message);
                }
            }
        }

        private void HandleCommand(string command, NamedPipeServerStream server)
        {
            switch (command)
            {
                case "get-state":
                    {
                        var snapshot = _engine.GetSnapshot();
                        IpcResponse.Json(server, new
                        {
                            globalState = _engine.GlobalState,
                            servers = snapshot
                        });
                        break;
                    }

                case "get-server":
                    // Extrait l'ID
                    if (command.StartsWith("get-server "))
                    {
                        string id = command.Substring("get-server ".Length).Trim();
                        var result = _engine.GetServerResult(id);

                        if (result == null)
                            IpcResponse.Error(server, "server not found");
                        else
                            IpcResponse.Json(server, result);
                    }
                    break;

                case "force-scan":
                    _engine.ForceScan();
                    IpcResponse.Ok(server);
                    break;

                case "shutdown":
                    IpcResponse.Ok(server);
                    Environment.Exit(0);
                    break;

                default:
                    IpcResponse.Error(server, "unknown command");
                    break;
            }
        }
    }
}