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
            _handle = WinDivertOpen(_filter, WINDIVERT_LAYER_NETWORK, 0, WINDIVERT_FLAG_SNIFF);
            
            if (_handle == IntPtr.Zero || _handle == new IntPtr(-1))
            {
                int err = Marshal.GetLastWin32Error();
                string msg = $"WinDivert : échec de l'ouverture (code {err}). Vérifiez que WinDivert.dll et WinDivert64.sys sont présents et que le service a les droits admin.";
                CoreLog.Error(msg);
                OnError?.Invoke(this, new Exception(msg));
                return;
            }

            _captureTask = Task.Run(() => CaptureLoopAsync(_cts.Token));
            CoreLog.Info($"WinDivert : capture démarrée avec le filtre '{_filter}'");
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
            CoreLog.Info("WinDivert : capture arrêtée");
        }

        public void Dispose() { Stop(); _cts.Dispose(); }
    }

    public record RawPacket(byte[] Data, DateTime Timestamp);
}