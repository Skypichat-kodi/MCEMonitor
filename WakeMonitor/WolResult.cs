public class WolResult
{
    public bool IsWol { get; set; }
    public bool IsAuthorized { get; set; }
    public bool IsLegacy { get; set; }
    public string IpSource { get; set; }
    public string MacSource { get; set; }
    public string Tag { get; set; }
    public string HtmlBlock { get; set; }

    public static WolResult Normal() => new WolResult
    {
        IsWol = false,
        Tag = "Réveil normal",
        HtmlBlock = "<div style='color:#444;font-weight:bold;font-size:18px;'>Réveil normal</div><br>"
    };

    public static WolResult WolAuthorized(string ip, string mac) => new WolResult
    {
        IsWol = true,
        IsAuthorized = true,
        Tag = "WOL autorisé",
        IpSource = ip,
        MacSource = mac,
        HtmlBlock =
            "<div style='color:green;font-weight:bold;font-size:20px;'>Wake-on-LAN autorisé</div><br>" +
            $"<b>IP source :</b> {ip}<br>" +
            $"<b>MAC source :</b> {mac}<br><br>"
    };

    public static WolResult WolSuspect(string ip, string mac) => new WolResult
    {
        IsWol = true,
        IsAuthorized = false,
        Tag = "WOL suspect",
        IpSource = ip,
        MacSource = mac,
        HtmlBlock =
            "<div style='color:red;font-weight:bold;font-size:20px;'>Wake-on-LAN suspect</div><br>" +
            $"<b>IP source :</b> {ip}<br>" +
            $"<b>MAC source :</b> {mac}<br><br>"
    };

    public static WolResult WolLegacy() => new WolResult
    {
        IsWol = true,
        IsLegacy = true,
        Tag = "WOL (NIC)",
        HtmlBlock =
            "<div style='color:green;font-weight:bold;font-size:20px;'>Wake-on-LAN (carte ancienne)</div><br>" +
            "<b>Source :</b> Indéterminable (carte réseau ancienne)<br><br>"
    };
}

