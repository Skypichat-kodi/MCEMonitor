namespace MCEMonitor.Services
{
    public class EmailSettings
    {
        public string Smtp     { get; set; } = string.Empty;
        public int    Port     { get; set; }
        public string From     { get; set; } = string.Empty;
        public string To       { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}