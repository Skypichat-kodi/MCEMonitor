using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Services;

/// <summary>
/// Surveille les connexions TCP/UDP via ETW (provider noyau NetworkTCPIP).
/// Aucune dépendance native, aucun pilote à installer.
/// Nécessite les privilèges administrateur (ou SYSTEM).
/// </summary>
public sealed class EtwConnectionWatcher : IDisposable
{
    private const string SessionName = "MCEMonitor-NetMonitor-ETW";

    private TraceEventSession? _session;
    private Thread? _processingThread;
    private volatile bool _running;

    public event EventHandler<ConnectionInfo>? ConnectionOpened;
    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;

        // Nettoyage d'une éventuelle session orpheline laissée par un crash précédent
        try
        {
            foreach (var name in TraceEventSession.GetActiveSessionNames())
            {
                if (name == SessionName)
                {
                    using var old = new TraceEventSession(SessionName) { StopOnDispose = true };
                    old.Stop();
                }
            }
        }
        catch (Exception ex) { CoreLog.Warn($"ETW cleanup : {ex.Message}"); }

        _running = true;
        _processingThread = new Thread(RunSession)
        {
            IsBackground = true,
            Name = "NetMonitor-ETW",
            Priority = ThreadPriority.AboveNormal
        };
        _processingThread.Start();
    }

    private void RunSession()
    {
        try
        {
            using var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            _session = session;

            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            // ---- TCP connect (ouverture de connexion) ----
            session.Source.Kernel.TcpIpConnect += data =>
            {
                var remoteIp = data.daddr?.ToString() ?? "";
                if (IsNoise(remoteIp, data.dport)) return;

                CoreLog.Debug($"[ETW] {data.saddr}:{data.sport} -> {remoteIp}:{data.dport} PID={data.ProcessID}");
                
                ConnectionOpened?.Invoke(this, new ConnectionInfo
                {
                    Pid         = data.ProcessID,
                    LocalIp     = data.saddr?.ToString() ?? "",
                    LocalPort   = data.sport,
                    RemoteIp    = remoteIp,
                    RemotePort  = data.dport,
                    Timestamp   = DateTime.UtcNow,
                    Protocol    = TransportProtocol.Tcp
                });
            };

            // ---- UDP send ----
            session.Source.Kernel.UdpIpSend += data =>
            {
                var remoteIp = data.daddr?.ToString() ?? "";
                if (IsNoise(remoteIp, data.dport)) return;

                ConnectionOpened?.Invoke(this, new ConnectionInfo
                {
                    Pid         = data.ProcessID,
                    LocalIp     = data.saddr?.ToString() ?? "",
                    LocalPort   = data.sport,
                    RemoteIp    = remoteIp,
                    RemotePort  = data.dport,
                    Timestamp   = DateTime.UtcNow,
                    Protocol    = TransportProtocol.Udp
                });
            };

            CoreLog.Info("Session ETW démarrée (NetworkTCPIP).");
            session.Source.Process(); // bloquant jusqu'à Stop()
        }
        catch (Exception ex)
        {
            CoreLog.Error($"ETW : échec de la session — {ex.Message}");
            _running = false;
        }
    }

    /// <summary>Filtre le bruit : loopback, multicast, broadcast, mDNS/SSDP/LLMNR/NetBIOS.</summary>
    private static bool IsNoise(string? destIp, int destPort)
    {
        if (string.IsNullOrEmpty(destIp)) return true;
        if (destIp is "127.0.0.1" or "::1" or "0.0.0.0") return true;
        if (destIp.StartsWith("224.") || destIp.StartsWith("239.")) return true;
        if (destIp == "255.255.255.255") return true;

        return destPort is 5353 or 1900 or 5355 or 137 or 138;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        try { _session?.Stop(); }
        catch (Exception ex) { CoreLog.Warn($"ETW stop : {ex.Message}"); }

        _processingThread?.Join(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        Stop();
        _session?.Dispose();
    }
}