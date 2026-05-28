using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace MCEMonitor.Utils
{
    public static class EmailTester
    {
        public static async Task TestAsync(string smtp, int port, string from, string password, string to)
        {
            using var client = new SmtpClient(smtp, port);
            client.EnableSsl = true;
            client.Credentials = new NetworkCredential(from, password);

            await client.SendMailAsync(new MailMessage(from, to, "Test SMTP", "Connexion OK"));
        }
    }
}

