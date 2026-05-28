using System;
using System.Linq;
using System.Net.NetworkInformation;

public static class NetworkInfo
{
    public static string GetActiveMac()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .FirstOrDefault();

        if (nic == null)
            return "Inconnue";

        return string.Join(":", nic.GetPhysicalAddress()
            .GetAddressBytes()
            .Select(b => b.ToString("X2")));
    }

    public static string GetActiveIp()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .FirstOrDefault();

        if (nic == null)
            return "Inconnue";

        var ip = nic.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .FirstOrDefault();

        return ip ?? "Inconnue";
    }
}

