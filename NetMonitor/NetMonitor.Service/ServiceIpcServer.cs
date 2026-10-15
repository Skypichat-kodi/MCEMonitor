using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using NetMonitor.Service.Ipc;

namespace NetMonitor.Service
{
    public class ServiceIpcServer
    {
        private readonly NetMonitorEngine _engine;
        private readonly NetMonitorSettings _settings;
        private bool _running = true;

        private const string PipeName = "MCEMonitor_NetMonitorPipe";

        public ServiceIpcServer(NetMonitorEngine engine, NetMonitorSettings settings)
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
                    using var server = CreateServerPipe();

                    server.WaitForConnection();

                    byte[] buffer = new byte[4096];
                    var readTask = server.ReadAsync(buffer, 0, buffer.Length);

                    if (!readTask.Wait(5000))
                        continue;

                    int bytesRead = readTask.Result;

                    if (bytesRead == 0)
                        continue;

                    string command = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    if (!command.StartsWith("get-", StringComparison.OrdinalIgnoreCase))
                        CoreLog.Write($"IPC : commande reçue = '{command}'");

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

        /// <summary>
        /// Crée un NamedPipeServerStream avec une ACL qui autorise
        /// tout utilisateur authentifié à s'y connecter.
        /// </summary>
        private static NamedPipeServerStream CreateServerPipe()
        {
            var security = new PipeSecurity();

            // Utilisateurs authentifiés : lecture/écriture
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));

            // SYSTEM : contrôle total (par défaut, mais on s'en assure)
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.None,
                0, 0,
                security);
        }

        public void Stop() => _running = false;
    }
}