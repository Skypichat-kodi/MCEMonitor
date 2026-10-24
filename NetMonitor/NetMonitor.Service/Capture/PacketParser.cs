using System;
using System.Buffers.Binary;
using System.Net;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Capture
{
    public static class PacketParser
    {
        public static NetworkEvent? Parse(RawPacket raw)
        {
            if (raw.Data.Length < 20) return null;

            var span = raw.Data.AsSpan();
            
            // WinDivert (Layer NETWORK) fournit directement l'en-tête IP.
            // Le premier octet contient la version (4 bits) et la longueur d'en-tête (4 bits).
            byte version = (byte)(span[0] >> 4);

            if (version == 4)
            {
                return ParseIPv4(span, raw.Timestamp);
            }

            // (IPv6 non géré pour l'instant)
            return null;
        }

        private static NetworkEvent? ParseIPv4(ReadOnlySpan<byte> ip, DateTime ts)
        {
            if (ip.Length < 20) return null;

            byte versionAndHeaderLength = ip[0];
            int headerLength = (versionAndHeaderLength & 0x0F) * 4;
            if (ip.Length < headerLength) return null;

            byte protocol = ip[9];
            var srcIp = new IPAddress(ip.Slice(12, 4).ToArray());
            var dstIp = new IPAddress(ip.Slice(16, 4).ToArray());

            var payload = ip.Slice(headerLength);
            var evt = new NetworkEvent
            {
                Timestamp = ts,
                SourceIp = srcIp.ToString(),
                DestIp = dstIp.ToString(),
                Length = ip.Length,
                // Le paquet est "sortant" si la source est une IP locale
                IsOutbound = LocalIps.IsLocal(srcIp),
            };

            switch (protocol)
            {
                case 6: evt.Protocol = TransportProtocol.Tcp; ParseTcp(payload, evt); break;
                case 17: evt.Protocol = TransportProtocol.Udp; ParseUdp(payload, evt); break;
                case 1: evt.Protocol = TransportProtocol.Icmp; ParseIcmp(payload, evt); break;
                default: return null;
            }

            return evt;
        }

        private static void ParseTcp(ReadOnlySpan<byte> tcp, NetworkEvent evt)
        {
            if (tcp.Length < 20) return;
            evt.SourcePort = BinaryPrimitives.ReadUInt16BigEndian(tcp.Slice(0, 2));
            evt.DestPort = BinaryPrimitives.ReadUInt16BigEndian(tcp.Slice(2, 2));
            
            // Flags TCP (offset 13)
            byte flags = tcp[13];
            evt.TcpFlags = flags;
        }

        private static void ParseUdp(ReadOnlySpan<byte> udp, NetworkEvent evt)
        {
            if (udp.Length < 8) return;
            evt.SourcePort = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(0, 2));
            evt.DestPort = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(2, 2));
            
            // Si c'est du DNS (port 53), on stocke le payload pour analyse ultérieure
            if (evt.SourcePort == 53 || evt.DestPort == 53)
            {
                evt.Payload = udp.Slice(8).ToArray();
            }
        }

        private static void ParseIcmp(ReadOnlySpan<byte> icmp, NetworkEvent evt)
        {
            if (icmp.Length < 8) return;
            evt.IcmpType = icmp[0];
            evt.IcmpCode = icmp[1];
        }
    }
}