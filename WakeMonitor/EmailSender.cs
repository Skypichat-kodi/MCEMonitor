using System;
using System.IO;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

public static class EmailSender
{
    public static async Task SendAsync(
        EmailConfig cfg,
        string subject,
        string body,
        bool isHtml = false)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("WakeMonitor", cfg.From));
            message.To.Add(new MailboxAddress(cfg.To, cfg.To));
            message.Subject = subject;

            // Si HTML ? on encapsule dans un template propre
            if (isHtml)
            {
                body = $@"
<div style='font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#333;background:#f5f5f5;padding:20px'>
    <div style='max-width:650px;margin:auto;background:white;border-radius:8px;padding:20px;
                box-shadow:0 2px 6px rgba(0,0,0,0.1)'>

        <h2 style='text-align:center;color:#2c3e50;margin-top:0'>
            WakeMonitor – Rapport de réveil
        </h2>

        <p style='text-align:center;color:#555;margin-top:5px'>
            Machine : <b>{Environment.MachineName}</b><br>
            Généré le {DateTime.Now:dd/MM/yyyy HH:mm:ss}
        </p>

        {body}

        <p style='margin-top:20px;color:#888;font-size:12px;text-align:center'>
            Rapport généré automatiquement par WakeMonitor.
        </p>
    </div>
</div>";
            }

            message.Body = new TextPart(isHtml ? "html" : "plain")
            {
                Text = body
            };

            SecureSocketOptions options = cfg.SecurityMode.ToUpper() switch
            {
                "SSL" => SecureSocketOptions.SslOnConnect,
                "TLS" => SecureSocketOptions.StartTls,
                "STARTTLS" => SecureSocketOptions.StartTls,
                "NONE" => SecureSocketOptions.None,
                _ => SecureSocketOptions.Auto
            };

            using var client = new SmtpClient();

            await client.ConnectAsync(cfg.Server, cfg.Port, options);
            await client.AuthenticateAsync(cfg.From, cfg.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "email_error.log"
            );

            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            File.AppendAllText(logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - SMTP error: {ex.Message}{Environment.NewLine}");

            throw;
        }
    }
}

