namespace NetMonitor.Service;

public sealed class EmailConfig
{
    public bool Enabled { get; set; } = false;
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public bool UseSsl { get; set; } = false;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string SubjectPrefix { get; set; } = "[NetMonitor]";

    /// <summary>N'envoie un mail que si la sévérité >= ce seuil.</summary>
    public Models.Severity MinSeverity { get; set; } = Models.Severity.High;

    /// <summary>Anti-spam : ne pas envoyer plus d'un mail par ce délai.</summary>
    public int ThrottleSeconds { get; set; } = 60;
}