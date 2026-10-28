using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NetMonitor.Service.Capture
{
    public sealed class PacketCaptureService : IDisposable
    {
        private const int WINDIVERT_LAYER_NETWORK = 0;
        private const ulong WINDIVERT_FLAG_SNIFF = 1;

        // Configuration du retry
        private const int MAX_OPEN_ATTEMPTS = 5;
        private static readonly int[] RETRY_DELAYS_MS = { 0, 1000, 2000, 3000, 5000 };

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertRecv(IntPtr handle, byte[] pPacket, uint packetLen, out uint readLen, IntPtr pAddr);

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertClose(IntPtr handle);

        private readonly string _filter;
        private readonly ChannelWriter<RawPacket> _writer;
        private readonly CancellationTokenSource _cts = new();
        private Task? _captureTask;
        private IntPtr _handle = IntPtr.Zero;

        public event EventHandler<Exception>? OnError;

        public PacketCaptureService(string filter, ChannelWriter<RawPacket> writer)
        {
            _filter = filter;
            _writer = writer;
        }

        public void Start()
        {
            if (!TryOpenWithRetry())
                return;

            _captureTask = Task.Run(() => CaptureLoopAsync(_cts.Token));
            CoreLog.Info($"WinDivert : capture démarrée avec le filtre '{_filter}'");
        }

        /// <summary>
        /// Tente d'ouvrir WinDivert avec plusieurs essais espacés.
        /// Objectif : absorber les échecs transitoires au démarrage
        /// (BFE pas prêt, autre logiciel libérant le pilote, antivirus
        /// temporisant le chargement…).
        ///
        /// Codes d'erreur fréquents :
        ///   1058 (ERROR_SERVICE_DISABLED)  ? pilote pas encore disponible
        ///   5    (ERROR_ACCESS_DENIED)     ? droits admin insuffisants
        ///   2    (ERROR_FILE_NOT_FOUND)    ? DLL/SYS manquants
        ///   1275 (ERROR_DRIVER_BLOCKED)    ? antivirus bloque le pilote
        /// </summary>
        private bool TryOpenWithRetry()
        {
            int totalAttempts = Math.Min(MAX_OPEN_ATTEMPTS, RETRY_DELAYS_MS.Length);
            int lastErr = 0;

            for (int attempt = 0; attempt < totalAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    int delayMs = RETRY_DELAYS_MS[attempt];
                    CoreLog.Warn(
                        $"WinDivert : nouvelle tentative {attempt + 1}/{totalAttempts} " +
                        $"dans {delayMs} ms (dernier code : {lastErr})…");
                    try { Thread.Sleep(delayMs); }
                    catch { /* jamais bloquant */ }
                }

                _handle = WinDivertOpen(_filter, WINDIVERT_LAYER_NETWORK, 0, WINDIVERT_FLAG_SNIFF);

                if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
                    return true;

                lastErr = Marshal.GetLastWin32Error();
                _handle = IntPtr.Zero;

                // Pour ces codes, réessayer ne sert à rien : on abandonne tout de suite.
                //   2   = ERROR_FILE_NOT_FOUND      ? fichiers absents, retry inutile
                //   5   = ERROR_ACCESS_DENIED       ? droits admin manquants, retry inutile
                //   1275= ERROR_DRIVER_BLOCKED      ? antivirus, retry inutile
                if (lastErr == 2 || lastErr == 5 || lastErr == 1275)
                {
                    CoreLog.Error(
                        $"WinDivert : échec définitif (code {lastErr}) — nouvelle tentative inutile.");
                    break;
                }
            }

            string hint = lastErr switch
            {
                2    => "Fichiers WinDivert.dll / WinDivert64.sys introuvables dans le dossier de sortie.",
                5    => "Droits administrateur insuffisants.",
                1058 => "Le pilote WinDivert ne peut pas démarrer (service désactivé, " +
                        "ou conflit avec un autre logiciel utilisant WinDivert, " +
                        "ou Base Filtering Engine (BFE) non démarré).",
                1275 => "Le pilote WinDivert est bloqué par un antivirus / une politique de sécurité.",
                577  => "Signature numérique du pilote WinDivert non reconnue par Windows.",
                _    => "Consultez le journal Windows (Event Viewer) pour plus de détails."
            };

            string msg =
                $"WinDivert : échec de l'ouverture après {totalAttempts} tentative(s) " +
                $"(dernier code {lastErr}). {hint}";

            CoreLog.Error(msg);
            OnError?.Invoke(this, new Exception(msg));
            return false;
        }

        private async Task CaptureLoopAsync(CancellationToken ct)
        {
            try
            {
                var buffer = new byte[65535];
                var addrBuffer = new byte[64];
                var addrHandle = GCHandle.Alloc(addrBuffer, GCHandleType.Pinned);

                try
                {
                    while (!ct.IsCancellationRequested && _handle != IntPtr.Zero)
                    {
                        var (success, readLen, err) = await Task.Run(() =>
                        {
                            bool s = WinDivertRecv(_handle, buffer, (uint)buffer.Length, out uint len, addrHandle.AddrOfPinnedObject());
                            return (s, len, Marshal.GetLastWin32Error());
                        }, ct);

                        if (success && readLen > 0)
                        {
                            var data = new byte[readLen];
                            Buffer.BlockCopy(buffer, 0, data, 0, (int)readLen);
                            var rawPacket = new RawPacket(data, DateTime.UtcNow);

                            if (!_writer.TryWrite(rawPacket))
                            {
                                CoreLog.Warn("WinDivert : file de traitement pleine, paquet ignoré");
                            }
                        }
                        else if (!ct.IsCancellationRequested)
                        {
                            CoreLog.Warn($"[WinDivert] WinDivertRecv a échoué (code {err})");
                            await Task.Delay(10, ct);
                        }
                    }
                }
                finally
                {
                    addrHandle.Free();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                CoreLog.Error($"WinDivert : erreur critique : {ex.Message}");
                OnError?.Invoke(this, ex);
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            if (_handle != IntPtr.Zero)
            {
                WinDivertClose(_handle);
                _handle = IntPtr.Zero;
            }
            try { _captureTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            CoreLog.Info("WinDivert : capture arr?tée");
        }

        public void Dispose() { Stop(); _cts.Dispose(); }
    }

    public record RawPacket(byte[] Data, DateTime Timestamp);
}