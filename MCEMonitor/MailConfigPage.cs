using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using Krypton.Toolkit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MCEMonitor
{
    public class MailConfigPage : UserControl
    {
        private KryptonTextBox txtFrom;
        private KryptonTextBox txtPassword;
        private KryptonTextBox txtTo;
        private KryptonTextBox txtServer;
        private KryptonNumericUpDown numPort;
        private KryptonComboBox cboSecurity;
        private KryptonButton btnSave;
        private KryptonButton btnTest;
        private KryptonPictureBox picStatus;
        private KryptonLabel lblFrom;
        private KryptonLabel lblPassword;
        private KryptonLabel lblTo;
        private KryptonLabel lblServer;
        private KryptonLabel lblPort;
        private KryptonLabel lblSecurity;

        public MailConfigPage()
        {
            InitializeUI();
            LoadConfig();
        }

        private void InitializeUI()
        {
            this.Dock = DockStyle.Fill;

            lblFrom = new KryptonLabel { Text = "Email expéditeur :", AutoSize = true, Location = new Point(20, 20) };
            txtFrom = new KryptonTextBox { Location = new Point(160, 18), Width = 280 };

            lblPassword = new KryptonLabel { Text = "Mot de passe :", AutoSize = true, Location = new Point(20, 55) };
            txtPassword = new KryptonTextBox { Location = new Point(160, 53), Width = 280, UseSystemPasswordChar = true };

            lblTo = new KryptonLabel { Text = "Destinataire :", AutoSize = true, Location = new Point(20, 90) };
            txtTo = new KryptonTextBox { Location = new Point(160, 88), Width = 280 };

            lblServer = new KryptonLabel { Text = "Serveur SMTP :", AutoSize = true, Location = new Point(20, 125) };
            txtServer = new KryptonTextBox { Location = new Point(160, 123), Width = 280 };

            lblPort = new KryptonLabel { Text = "Port :", AutoSize = true, Location = new Point(20, 160) };
            numPort = new KryptonNumericUpDown
            {
                Location = new Point(160, 158),
                Width = 80,
                Minimum = 1,
                Maximum = 65535,
                Value = 465
            };

            lblSecurity = new KryptonLabel { Text = "Sécurité :", AutoSize = true, Location = new Point(20, 195) };
            cboSecurity = new KryptonComboBox
            {
                Location = new Point(160, 193),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboSecurity.Items.AddRange(new object[] { "SSL", "STARTTLS", "AUCUN" });
            cboSecurity.SelectedIndex = 0;

            btnSave = new KryptonButton
            {
                Text = LanguageManager.Get("Enregistrer") ?? "Enregistrer",
                Location = new Point(160, 240),
                Size = new Size(120, 32)
            };
            btnSave.Click += (s, e) => SaveConfig();

            btnTest = new KryptonButton
            {
                Text = LanguageManager.Get("Tester SMTP") ?? "Tester SMTP",
                Location = new Point(300, 240),
                Size = new Size(140, 32)
            };
            btnTest.Click += BtnTest_Click;

            picStatus = new KryptonPictureBox
            {
                Location = new Point(20, 240),
                Size = new Size(20, 20),
                BackColor = Color.Gray,
                BorderStyle = BorderStyle.FixedSingle
            };

            Controls.Add(lblFrom);
            Controls.Add(txtFrom);
            Controls.Add(lblPassword);
            Controls.Add(txtPassword);
            Controls.Add(lblTo);
            Controls.Add(txtTo);
            Controls.Add(lblServer);
            Controls.Add(txtServer);
            Controls.Add(lblPort);
            Controls.Add(numPort);
            Controls.Add(lblSecurity);
            Controls.Add(cboSecurity);
            Controls.Add(btnSave);
            Controls.Add(btnTest);
            Controls.Add(picStatus);
        }

        private void LoadConfig()
        {
            var cfg = EmailConfig.Load();

            txtFrom.Text = cfg.From;
            txtPassword.Text = cfg.Password;
            txtTo.Text = cfg.To;
            txtServer.Text = cfg.Server;
            numPort.Value = Math.Clamp(cfg.Port, 1, 65535);
            cboSecurity.SelectedItem = cfg.SecurityMode.ToUpper();
        }

        private void SaveConfig()
        {
            var cfg = new EmailConfig
            {
                From = txtFrom.Text.Trim(),
                Password = txtPassword.Text,
                To = txtTo.Text.Trim(),
                Server = txtServer.Text.Trim(),
                Port = (int)numPort.Value,
                SecurityMode = (cboSecurity.SelectedItem?.ToString() ?? "SSL").ToUpper()
            };

            cfg.Save();

            KryptonMessageBox.Show("Configuration enregistrée.", "Succès",
                KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
        }

        private void Log(string message)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "smtp_test.log"
            );

            File.AppendAllText(path, DateTime.Now + " - " + message + Environment.NewLine);
        }

        private async void BtnTest_Click(object? sender, EventArgs e)
        {
            picStatus.BackColor = Color.Gray;
            Log("=== Nouveau test SMTP ===");

            try
            {
                var cfg = new EmailConfig
                {
                    From = txtFrom.Text.Trim(),
                    Password = txtPassword.Text,
                    To = txtTo.Text.Trim(),
                    Server = txtServer.Text.Trim(),
                    Port = (int)numPort.Value,
                    SecurityMode = (cboSecurity.SelectedItem?.ToString() ?? "SSL").ToUpper()
                };

                Log($"Paramètres : Server={cfg.Server}, Port={cfg.Port}, Mode={cfg.SecurityMode}");

                Log("Test du port...");
                await TestPortAsync(cfg.Server, cfg.Port);
                Log("Port OK");

                Log("Envoi email via MailKit...");
                await SendMailKitAsync(cfg);
                Log("Email envoyé avec succès");

                picStatus.BackColor = Color.LimeGreen;

                KryptonMessageBox.Show("Test SMTP réussi.", "Succès",
                    KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                picStatus.BackColor = Color.Red;
                Log("ERREUR : " + ex.Message);

                KryptonMessageBox.Show("Erreur SMTP : " + ex.Message,
                    "Erreur", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Error);
            }
        }

        private async Task TestPortAsync(string host, int port)
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);

            if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
                throw new Exception("Timeout : le port ne répond pas.");

            if (!client.Connected)
                throw new Exception("Impossible de se connecter au port.");
        }

        private async Task SendMailKitAsync(EmailConfig cfg)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("MCEMonitor", cfg.From));
            message.To.Add(new MailboxAddress(cfg.To, cfg.To));
            message.Subject = "Test SMTP - MCEMonitor";
            message.Body = new TextPart("plain")
            {
                Text = LanguageManager.Get("Ceci est un email de test envoyé depuis MCEMonitor.") ?? "Ceci est un email de test envoyé depuis MCEMonitor."
            };

            SecureSocketOptions options = SecureSocketOptions.Auto;

            switch (cfg.SecurityMode)
            {
                case "SSL":
                    options = SecureSocketOptions.SslOnConnect;
                    break;

                case "STARTTLS":
                    options = SecureSocketOptions.StartTls;
                    break;

                case "AUCUN":
                    options = SecureSocketOptions.None;
                    break;
            }

            using var client = new SmtpClient();

            await client.ConnectAsync(cfg.Server, cfg.Port, options);
            await client.AuthenticateAsync(cfg.From, cfg.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}