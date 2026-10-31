using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MCEMonitorClient.Service.Logs;
using MCEMonitorClient.Service.Models;

namespace MCEMonitorClient.Service.Ipc
{
    /// <summary>
    /// Canal de push vers les Tray connectés.
    /// Le Tray ouvre une connexion persistante sur ce pipe et le Service écrit dedans.
    /// </summary>
    public static class PushChannel
    {
        public const string PushPipeName = "MCEMonitor_ClientPush";

        // Liste des Tray actuellement connectés
        private static readonly List<NamedPipeServerStream> _clients = new();
        private static readonly object _lock = new();

        // Démarrage : écoute les connexions entrantes
        public static void Start()
        {
            var thread = new Thread(AcceptLoop) { IsBackground = true };
            thread.Start();

            CoreLog.Write("PushChannel démarré (pipe = " + PushPipeName + ")");
        }

        private static void AcceptLoop()
        {
            while (true)
            {
                try
                {
                var server = new NamedPipeServerStream(
                    PushPipeName,
                    PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                    CoreLog.Write("PushChannel : attente d'un Tray...");
                    server.WaitForConnection();

                    CoreLog.Write("PushChannel : Tray connecté");

                    lock (_lock)
                        _clients.Add(server);
                }
                catch (Exception ex)
                {
                    CoreLog.Write("PushChannel AcceptLoop ERROR : " + ex.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        /// <summary>
        /// Envoie un événement à tous les Tray connectés.
        /// </summary>
        public static void Broadcast(object payload)
        {
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) + "\n";

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            lock (_lock)
            {
                var dead = new List<NamedPipeServerStream>();

                foreach (var client in _clients)
                {
                    try
                    {
                        if (!client.IsConnected)
                        {
                            dead.Add(client);
                            continue;
                        }

                        client.Write(bytes, 0, bytes.Length);
                        client.Flush();
                    }
                    catch
                    {
                        dead.Add(client);
                    }
                }

                // Retire les clients morts
                foreach (var d in dead)
                {
                    try { d.Dispose(); } catch { }
                    _clients.Remove(d);
                }
            }
        }

        /// <summary>
        /// Envoie un événement "alert" au Tray, avec :
        ///  - resolvedProblems : problèmes présents avant et absents maintenant
        ///  - newProblems      : problèmes absents avant et présents maintenant
        /// </summary>
        public static void PushAlert(PollResult result, PollResult? previous,
                                     List<ProblemItem>? newProblems = null)
        {
            // Diff pour les résolutions (clé stable = Category|Target)
            var resolvedProblems = new List<ProblemItem>();
            if (previous != null && previous.Problems.Count > 0)
            {
                static string KeyOf(ProblemItem p) => $"{p.Category}|{p.Target}";
                var currentKeys = result.Problems.Select(KeyOf).ToHashSet();

                foreach (var p in previous.Problems)
                {
                    if (!string.IsNullOrEmpty(p.Category) && !currentKeys.Contains(KeyOf(p)))
                        resolvedProblems.Add(p);
                }
            }

            Broadcast(new
            {
                type             = "alert",
                serverId         = result.ServerId,
                serverName       = result.ServerName,
                serviceType      = result.ServiceType,
                baseUrl          = result.BaseUrl,
                status           = result.Status,
                previousStatus   = previous?.Status ?? "unknown",
                problemCount     = result.ProblemCount,
                firstProblem     = result.FirstProblem,
                problems         = result.Problems,
                previousProblems = previous?.Problems ?? new List<ProblemItem>(),
                resolvedProblems,
                newProblems      = newProblems ?? new List<ProblemItem>(), 
                lastBlock        = result.LastBlock,
                timestamp        = result.Timestamp
            });
        }
        
        /// <summary>
        /// Envoie un événement sur un média (démarré ou terminé).
        /// </summary>
        public static void PushMediaEvent(PollResult server, MediaItem media, string eventType)
        {
            Broadcast(new
            {
                type = "media",
                eventType = eventType,        // "started" ou "stopped"
                serverId = server.ServerId,
                serverName = server.ServerName,
                serviceType = server.ServiceType,
                baseUrl = server.BaseUrl,
                client = media.Client,
                mediaType = media.Type,
                title = media.Title,
                saison = media.Saison,
                episode = media.Episode,
                timestamp = DateTime.Now
            });
        }        
    }
}