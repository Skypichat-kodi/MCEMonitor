using System.Buffers.Binary;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

public sealed class DnsSpoofDetector : IDetector
{
    public string Name => "DnsSpoof";

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx)
    {
        if (evt.Protocol != TransportProtocol.Udp) return null;

        // Seulement les réponses DNS (source port 53)
        if (evt.SourcePort != 53) return null;
        if (evt.Payload == null || evt.Payload.Length < 12) return null;

        var span = evt.Payload.AsSpan();

        // Vérifie que c'est bien une réponse (bit QR dans le byte 2)
        bool isResponse = (span[2] & 0x80) != 0;
        if (!isResponse) return null;

        int questionCount = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4, 2));
        int answerCount = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6, 2));
        if (answerCount == 0) return null;

        // Skip la section Question
        int offset = 12;
        for (int q = 0; q < questionCount && offset < span.Length; q++)
        {
            while (offset < span.Length)
            {
                int len = span[offset];
                if (len == 0) { offset++; break; }
                if ((len & 0xC0) == 0xC0) { offset += 2; break; }
                offset += 1 + len;
            }
            offset += 4; // QTYPE + QCLASS
        }

        if (offset >= span.Length) return null;

        // Cherche un TTL=0 dans les réponses (signature DNS poisoning)
        for (int a = 0; a < answerCount && offset + 10 < span.Length; a++)
        {
            while (offset < span.Length)
            {
                int len = span[offset];
                if (len == 0) { offset++; break; }
                if ((len & 0xC0) == 0xC0) { offset += 2; break; }
                offset += 1 + len;
            }

            if (offset + 10 > span.Length) break;

            offset += 4; // TYPE + CLASS
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4));
            offset += 4;
            int rdlen = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset, 2));
            offset += 2 + rdlen;

            if (ttl == 0)
            {
                if (!ctx.TryAcquireAlertSlot($"dnsspoof-{evt.SourceIp}", TimeSpan.FromMinutes(5)))
                    return null;

                return new DetectorResult
                {
                    IsThreat = true,
                    Severity = Severity.Warning,  // Warning : peut avoir des faux positifs
                    Message = $"Réponse DNS avec TTL=0 depuis {evt.SourceIp} " +
                              $"(possible DNS poisoning)",
                    Details = $"Taille payload : {evt.Payload.Length} octets"
                };
            }
        }

        return null;
    }
}