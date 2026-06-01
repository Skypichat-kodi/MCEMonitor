using System.Management;

public static class NetworkCardInfo
{
    public static bool IsModernWolCapable()
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? "";

                // Les cartes modernes exposent plusieurs capacités WOL
                if (name.Contains("Intel") || name.Contains("Realtek"))
                    return true;

                // Les vieilles cartes Atheros n'ont que "MagicPacket"
                if (name.Contains("Atheros"))
                    return false;
            }
        }
        catch { }

        return false; // fallback prudent
    }
}

