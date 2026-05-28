using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;

public static class NetworkDiagnostics
{
    public static string GetInfo()
    {
        var sb = new StringBuilder();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                sb.AppendLine($"Interface : {nic.Name}");
                sb.AppendLine($"  Type : {nic.NetworkInterfaceType}");
                sb.AppendLine($"  Vitesse : {nic.Speed / 1_000_000} Mbps");
                sb.AppendLine($"  MAC : {nic.GetPhysicalAddress()}");

                var props = nic.GetIPProperties();

                var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ipv4 != null)
                    sb.AppendLine($"  IPv4 : {ipv4.Address}");

                var ipv6 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
                if (ipv6 != null)
                    sb.AppendLine($"  IPv6 : {ipv6.Address}");

                var gw = props.GatewayAddresses.FirstOrDefault();
                if (gw != null)
                    sb.AppendLine($"  Gateway : {gw.Address}");

                sb.AppendLine("  DNS :");
                foreach (var dns in props.DnsAddresses)
                    sb.AppendLine($"    - {dns}");
            }

            // Ping Google DNS
            try
            {
                var ping = new Ping();
                var reply = ping.Send("8.8.8.8", 500);
                sb.AppendLine($"Ping 8.8.8.8 : {reply?.RoundtripTime} ms");
            }
            catch { sb.AppendLine("Ping impossible."); }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERREUR NetworkDiagnostics : " + ex.Message);
        }

        return sb.ToString();
    }
}

