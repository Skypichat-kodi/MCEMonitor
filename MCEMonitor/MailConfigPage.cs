using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MCEMonitor
{
    public class MailConfigPage : UserControl
    {
        private TextBox txtFrom;
        private TextBox txtPassword;
        private TextBox txtTo;
        private TextBox txtServer;
        private NumericUpDown numPort;
        private ComboBox cboSecurity;
        private Button btnSave;
        private Button btnTest;
        private PictureBox picStatus;

        public MailConfigPage()
        {
            InitializeUI();
            LoadConfig();
        }

        private void InitializeUI()
        {
            this.Dock = DockStyle.Fill;

            var lblFrom = new Label { Text = "Email expéditeur :", AutoSize = true, Left = 20, Top = 20 };
            txtFrom = new TextBox { Left = 160, Top = 18, Width = 280 };

            var lblPassword = new Label { Text = "Mot de passe :", AutoSize = true, Left = 20, Top = 55 };
            txtPassword = new TextBox { Left = 160, Top = 53, Width = 280, UseSystemPasswordChar = true };

            var lblTo = new Label { Text = "Destinataire :", AutoSize = true, Left = 20, Top = 90 };
            txtTo = new TextBox { Left = 160, Top = 88, Width = 280 };

            var lblServer = new Label { Text = "Serveur SMTP :", AutoSize = true, Left = 20, Top = 125 };
            txtServer = new TextBox { Left = 160, Top = 123, Width = 280 };

            var lblPort = new Label { Text = "Port :", AutoSize = true, Left = 20, Top = 160 };
            numPort = new NumericUpDown
            {
                Left = 160,
                Top = 158,
                Width = 80,
                Minimum = 1,
                Maximum = 65535,
                Value = 465
            };

            var lblSecurity = new Label { Text = "Sécurité :", AutoSize = true, Left = 20, Top = 195 };
            cboSecurity = new ComboBox
            {
                Left = 160,
                Top = 193,
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboSecurity.Items.AddRange(new object[] { "SSL", "STARTTLS", "AUCUN" });
            cboSecurity.SelectedIndex = 0;

            btnSave = new Button
            {
                Text = "Enregistrer",
                Left = 160,
                Top = 240,
                Width = 120
            };
            btnSave.Click += (s, e) => SaveConfig();

            btnTest = new Button
            {
                Text = "Tester SMTP",
                Left = 300,
                Top = 240,
                Width = 140
            };
            btnTest.Click += BtnTest_Click;

            picStatus = new PictureBox
            {
                Left = 20,
                Top = 240,
                Width = 20,
                Height = 20,
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

            MessageBox.Show("Configuration enregistrée.", "Succès",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
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

                // 1?? Test du port
                Log("Test du port...");
                await TestPortAsync(cfg.Server, cfg.Port);
                Log("Port OK");

                // 2?? Envoi réel via MailKit
                Log("Envoi email via MailKit...");
                await SendMailKitAsync(cfg);
                Log("Email envoyé avec succès");

                picStatus.BackColor = Color.LimeGreen;

                MessageBox.Show("Test SMTP réussi.", "Succès",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                picStatus.BackColor = Color.Red;
                Log("ERREUR : " + ex.Message);

                MessageBox.Show("Erreur SMTP : " + ex.Message,
                    "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                Text = "Ceci est un email de test envoyé depuis MCEMonitor."
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

