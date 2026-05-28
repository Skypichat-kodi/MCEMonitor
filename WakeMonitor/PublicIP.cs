using System;
using System.Net.Http;
using System.Threading.Tasks;

public static class PublicIP
{
    public static async Task<string> GetPublicIP()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(4);

            string ip = await client.GetStringAsync("https://api.ipify.org");
            return ip.Trim();
        }
        catch
        {
            return "Inconnue";
        }
    }
}

