using System;
using System.IO;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MCEMonitor
{
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

                // ?? Expéditeur dynamique avec nom de machine
                message.From.Add(new MailboxAddress(
                    $"MCEMonitor – {Environment.MachineName}",
                    cfg.From
                ));

                message.To.Add(new MailboxAddress(cfg.To, cfg.To));
                message.Subject = subject;

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

                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - SMTP error: {ex.Message}{Environment.NewLine}");

                throw;
            }
        }
    }
}

