using System;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using MCEMonitorClient.Tray.Logs;

namespace MCEMonitorClient.Tray.Ipc
{
    /// <summary>
    /// Écoute le pipe de push du Service et déclenche un événement à chaque message.
    /// </summary>
    public class PushListener
    {
        private const string PushPipeName = "MCEMonitor_ClientPush";

        private Thread? _thread;
        private bool _running;

        /// <summary>
        /// Event déclenché quand le Service envoie une alerte.
        /// </summary>
        public event Action<PushAlert>? OnAlert;
        public event Action<PushMedia>? OnMedia;

        public void Start()
        {
            if (_running) return;
            _running = true;

            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();

            CoreLog.Write("PushListener démarré");
        }

        public void Stop()
        {
            _running = false;
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(
                        ".", PushPipeName, PipeDirection.In);

                    pipe.Connect(5000);

                    if (!pipe.IsConnected)
                    {
                        Thread.Sleep(5000);
                        continue;
                    }

                    CoreLog.Write("PushListener : connecté au Service");

                    using var reader = new System.IO.StreamReader(pipe, Encoding.UTF8);

                    while (_running)
                    {
                        string? line = reader.ReadLine();

                        if (line == null)
                        {
                            CoreLog.Write("PushListener : pipe fermé");
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        CoreLog.Write($"PushListener : message reçu = {line.Substring(0, Math.Min(150, line.Length))}...");

                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("type", out var typeProp))
                            {
                                string evtType = typeProp.GetString() ?? "";
                                CoreLog.Write($"PushListener : type = '{evtType}'");

                                if (evtType == "alert")
                                {
                                    var alert = JsonSerializer.Deserialize<PushAlert>(line,
                                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                                    if (alert != null)
                                    {
                                        CoreLog.Write($"PushListener : ? OnAlert ({alert.ServerName} = {alert.Status})");
                                        OnAlert?.Invoke(alert);
                                    }
                                }
                                else if (evtType == "media")
                                {
                                    var media = JsonSerializer.Deserialize<PushMedia>(line,
                                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                                    if (media != null)
                                    {
                                        CoreLog.Write($"PushListener : ? OnMedia ({media.ServerName} = {media.EventType} '{media.Title}')");
                                        OnMedia?.Invoke(media);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            CoreLog.Write("PushListener parse ERROR : " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_running)
                        CoreLog.Write("PushListener connexion ERROR : " + ex.Message);
                }

                if (_running)
                    Thread.Sleep(5000);   // Retry après 5s
            }

            CoreLog.Write("PushListener arrêté");
        }
    }

    /// <summary>
    /// Alerte reçue du Service via push.
    /// </summary>
    public class PushAlert
    {
        public string Type { get; set; } = "";
        public string ServerId { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string ServiceType { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Status { get; set; } = "ok";
        public string PreviousStatus { get; set; } = "unknown";
        public int ProblemCount { get; set; }
        public string FirstProblem { get; set; } = "";

        // ? NOUVEAU : liste complète
        public System.Collections.Generic.List<ProblemItem> Problems { get; set; } = new();

        public DateTime Timestamp { get; set; }
    }

    public class ProblemItem
    {
        public string Severity { get; set; } = "";
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
    }
    
    public class PushMedia
    {
        public string Type { get; set; } = "";
        public string EventType { get; set; } = "";    // "started" / "stopped"
        public string ServerId { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string ServiceType { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Client { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string Title { get; set; } = "";
        public int Saison { get; set; }
        public int Episode { get; set; }
        public DateTime Timestamp { get; set; }
    }    
}