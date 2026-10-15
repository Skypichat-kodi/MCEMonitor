using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using NetMonitor.Service.Models;

namespace NetMonitor.Service;

public sealed class EmailSender : IDisposable
{
    private readonly EmailConfig _config;
    private DateTime _lastSentUtc = DateTime.MinValue;
    private readonly object _lock = new();

    public EmailSender(EmailConfig config) => _config = config;

    public void Reload(EmailConfig config) { /* à brancher sur un rechargement de config */ }

    public async Task SendAlertAsync(Alert alert, CancellationToken ct = default)
    {
        if (!_config.Enabled) return;
        if (alert.Severity < _config.MinSeverity) return;

        lock (_lock)
        {
            if ((DateTime.UtcNow - _lastSentUtc).TotalSeconds < _config.ThrottleSeconds)
                return;
            _lastSentUtc = DateTime.UtcNow;
        }

        try
        {
            var msg = new MimeMessage();
            msg.From.Add(MailboxAddress.Parse(_config.From));
            foreach (var to in _config.To.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                msg.To.Add(MailboxAddress.Parse(to));

            msg.Subject = $"{_config.SubjectPrefix} [{alert.Severity}] {alert.Title}";

            var body = new BodyBuilder
            {
                HtmlBody = BuildHtml(alert),
                TextBody = $"{alert.Title}\n\n{alert.Message}\n\n{alert.Details}\n\n{alert.Timestamp:O}"
            };
            msg.Body = body.ToMessageBody();

            using var client = new SmtpClient();
            var secOpts = _config.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : _config.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

            await client.ConnectAsync(_config.SmtpHost, _config.SmtpPort, secOpts, ct);
            if (!string.IsNullOrEmpty(_config.User))
                await client.AuthenticateAsync(_config.User, _config.Password, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);

            CoreLog.Info($"Mail envoyé : {alert.Title}");
        }
        catch (Exception ex)
        {
            CoreLog.Error($"Échec envoi mail : {ex.Message}");
        }
    }

    private static string BuildHtml(Alert a)
    {
        var color = a.Severity switch
        {
            Severity.Critical => "#c0392b",
            Severity.High     => "#e67e22",
            Severity.Warning  => "#f1c40f",
            _                 => "#3498db"
        };

        return $@"<html><body style=""font-family:Segoe UI,Arial,sans-serif"">
<h2 style=""color:{color}"">[{a.Severity}] {System.Net.WebUtility.HtmlEncode(a.Title)}</h2>
<p><b>Source :</b> {System.Net.WebUtility.HtmlEncode(a.Source)}</p>
<p><b>Date :</b> {a.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</p>
<p>{System.Net.WebUtility.HtmlEncode(a.Message)}</p>
{(string.IsNullOrEmpty(a.Details) ? "" : $"<pre style=\"background:#f4f4f4;padding:8px\">{System.Net.WebUtility.HtmlEncode(a.Details)}</pre>")}
</body></html>";
    }

    public void Dispose() { }
}