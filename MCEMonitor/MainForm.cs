using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCEMonitor.Services;
using MCEMonitor.Utils;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MCEMonitor
{
    public partial class MainForm : Form
    {
        private readonly MediaMonitorService _media;
        private readonly WakeMonitorService _wake;

        public MainForm(MediaMonitorService media, WakeMonitorService wake)
        {
            _media = media;
            _wake = wake;

            InitializeComponent();
            
            // Empêche le redimensionnement
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            
            // Centre la fenêtre au démarrage
            this.StartPosition = FormStartPosition.CenterScreen;                        

            this.Icon = new Icon("Assets/MediaMonitor.ico");

            LoadEmailConfig();
            LoadMediaConfig();
            LoadWakeConfig();
            UpdateWakeTaskStatus();
            LoadShutdownConfig();
            UpdateShutdownTaskStatus();
            UpdateStopTaskStatus();
            UpdateNextReportLabel();
            UpdateLastReportLabel();

        }

        // ============================================================
        // ONGLET EMAIL
        // ============================================================

        private void LoadEmailConfig()
        {
            var cfg = EmailConfig.Load();

            txtSmtpServer.Text = cfg.Server;
            txtSmtpPort.Text = cfg.Port.ToString();
            txtEmailFrom.Text = cfg.From;
            txtEmailPassword.Text = cfg.Password;
            txtEmailTo.Text = cfg.To;
            cmbSecurityMode.Text = cfg.SecurityMode;
        }

        private void BtnSaveEmail_Click(object sender, EventArgs e)
        {
            var cfg = new EmailConfig
            {
                Server = txtSmtpServer.Text.Trim(),
                Port = int.TryParse(txtSmtpPort.Text, out int p) ? p : 465,
                From = txtEmailFrom.Text.Trim(),
                Password = txtEmailPassword.Text.Trim(),
                To = txtEmailTo.Text.Trim(),
                SecurityMode = cmbSecurityMode.Text.Trim()
            };

            cfg.Save();

            PopupHelper.ShowBottomPopup(
            this,
                LanguageManager.Get("Configuration Email enregistrée") ?? "Configuration Email enregistrée"
            );
        }

        private void BtnTogglePassword_Click(object sender, EventArgs e)
        {
            if (txtEmailPassword.PasswordChar == '*')
            {
                txtEmailPassword.PasswordChar = '\0';
                btnTogglePassword.Text =
                    LanguageManager.Get("Masquer") ?? "Masquer";
            }
            else
            {
                txtEmailPassword.PasswordChar = '*';
                btnTogglePassword.Text =
                    LanguageManager.Get("Afficher") ?? "Afficher";
            }
        }

        private async void BtnTestEmail_Click(object sender, EventArgs e)
        {
            var cfg = EmailConfig.Load();

            var logForm = new SmtpTestForm();
            logForm.Show();
            logForm.Log(LanguageManager.Get("Démarrage du test SMTP…") ?? "Démarrage du test SMTP…");

            try
            {
                logForm.Log(
                    (LanguageManager.Get("Résolution DNS du serveur") ?? "Résolution DNS du serveur") +
                    $" {cfg.Server}…"
                );

                var addresses = await Dns.GetHostAddressesAsync(cfg.Server);
                if (addresses.Length > 0)
                    logForm.Log($"IP : {addresses[0]}");

                logForm.Log(
                    (LanguageManager.Get("Connexion au serveur SMTP sur le port") ?? "Connexion au serveur SMTP sur le port") +
                    $" {cfg.Port}…"
                );

                var options = cfg.SecurityMode.ToUpper() switch
                {
                    "SSL" => SecureSocketOptions.SslOnConnect,
                    "TLS" => SecureSocketOptions.StartTls,
                    "STARTTLS" => SecureSocketOptions.StartTls,
                    "NONE" => SecureSocketOptions.None,
                    _ => SecureSocketOptions.Auto
                };

                using var client = new SmtpClient();

                await client.ConnectAsync(cfg.Server, cfg.Port, options);
                logForm.Log($"Connexion établie ({options})");

                logForm.Log(LanguageManager.Get("Authentification…") ?? "Authentification…");
                await client.AuthenticateAsync(cfg.From, cfg.Password);
                logForm.Log(LanguageManager.Get("Authentification réussie.") ?? "Authentification réussie.");

                logForm.Log(LanguageManager.Get("Envoi du message de test…") ?? "Envoi du message de test…");

                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress("MCEMonitor", cfg.From));
                msg.To.Add(new MailboxAddress(cfg.To, cfg.To));
                msg.Subject = "Test SMTP MCEMonitor";
                msg.Body = new TextPart("plain") { Text = "Ceci est un test SMTP." };

                await client.SendAsync(msg);
                logForm.Log(LanguageManager.Get("Email envoyé avec succès !") ?? "Email envoyé avec succès !");

                await client.DisconnectAsync(true);
                logForm.Log(LanguageManager.Get("Déconnexion du serveur.") ?? "Déconnexion du serveur.");
            }
            catch (Exception ex)
            {
                logForm.Log(
                    (LanguageManager.Get("ERREUR : ") ?? "ERREUR : ") + ex.Message
                );
            }
        }

        // ============================================================
        // ONGLET MEDIA MONITOR
        // ============================================================

        private void UpdateNextReportLabel()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs",
                    "MediaMonitor.Schedule.log"
                );

                if (!File.Exists(path))
                {
                    lblNextReport.Text = "";
                    return;
                }

                // On cherche la DERNIÈRE ligne contenant [CODE01]
                string lastCode01 = File.ReadLines(path)
                    .Reverse()
                    .FirstOrDefault(l => l.Contains("[CODE01]"));

                if (string.IsNullOrWhiteSpace(lastCode01))
                {
                    lblNextReport.Text = "";
                    return;
                }

                // Retirer le timestamp "[xxxx-xx-xx xx:xx:xx] "
                int idx = lastCode01.IndexOf("] ");
                if (idx > 0)
                    lastCode01 = lastCode01.Substring(idx + 2);

                // Retirer le tag [CODE01]
                lastCode01 = lastCode01.Replace("[CODE01]", "").Trim();

                // Afficher EXACTEMENT ce qui reste
                lblNextReport.Text = lastCode01;
            }
            catch
            {
                lblNextReport.Text = "";
            }
        }

        private void LoadMediaConfig()
        {
            UpdateMediaToggle();
            UpdateMediaTaskButtons();
        }

        private void BtnCreateMediaTask_Click(object sender, EventArgs e)
        {
            try
            {
                string result = TaskSchedulerHelper.CreateMediaMonitorServiceTask();
                PopupHelper.ShowBottomPopup(
                this,
                    result,
                    LanguageManager.Get("Résultat création tâche MediaMonitor") ?? "Résultat création tâche MediaMonitor"
                );

                UpdateMediaTaskButtons();
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(
                this,
                    (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message
                );
            }
        }

        private void BtnDeleteMediaTask_Click(object sender, EventArgs e)
        {
            try
            {
                string result = TaskSchedulerHelper.DeleteMediaMonitorServiceTask();
                PopupHelper.ShowBottomPopup(
                this,
                    result,
                    LanguageManager.Get("Résultat suppression tâche MediaMonitor") ?? "Résultat suppression tâche MediaMonitor"
                );

                UpdateMediaTaskButtons();
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(
                this,
                    (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message
                );
            }
        }

private void BtnOpenUI_Click(object sender, EventArgs e)
{
    try
    {
        string uiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MediaMonitor.UI.exe");

        if (!File.Exists(uiPath))
        {
            PopupHelper.ShowBottomPopup(
                this,
                "MediaMonitor.UI.exe est introuvable dans le dossier de MCEMonitor.",
                "Erreur"
            );
            return;
        }

        // ?? Récupération de la langue actuellement utilisée par MCEMonitor
        string lang = LanguageManager.CurrentLanguage ?? "fr-FR";

        Process.Start(new ProcessStartInfo
        {
            FileName = uiPath,
            Arguments = $"--from-mcem -lang {lang}",
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        PopupHelper.ShowBottomPopup(
            this,
            "Erreur lors de l'ouverture de MediaMonitor.UI : " + ex.Message
        );
    }
}


        private bool IsMediaServiceRunning()
        {
            return Process.GetProcessesByName("MediaMonitor.Service").Length > 0;
        }

        private bool IsMediaUIRunning()
        {
            return Process.GetProcessesByName("MediaMonitor.UI").Length > 0;
        }

        private void UpdateMediaToggle()
        {
            bool running = IsMediaServiceRunning();

            if (running)
            {
                toggleMediaService.BackColor = Color.LimeGreen;
                toggleKnob.Left = 20;
                lblMediaStatus.Text =
                    LanguageManager.Get("Service MediaMonitor : actif") ??
                    "Service MediaMonitor : actif";
            }
            else
            {
                toggleMediaService.BackColor = Color.LightGray;
                toggleKnob.Left = 2;
                lblMediaStatus.Text =
                    LanguageManager.Get("Service MediaMonitor : arrêté") ??
                    "Service MediaMonitor : arrêté";
            }
        }

        private void toggleMediaService_Click(object sender, EventArgs e)
        {
            bool running = IsMediaServiceRunning();

            if (running)
            {
                // Empêcher l'arrêt si MediaMonitor.UI est ouvert
                if (IsMediaUIRunning())
                {
                PopupHelper.ShowBottomPopup(
                    this,
                    LanguageManager.Get("Impossible d'arrêter MediaMonitor.Service tant que MediaMonitor.UI est ouvert. Veuillez fermer MediaMonitor.UI d'abord.") ??
                    "Impossible d'arrêter MediaMonitor.Service tant que MediaMonitor.UI est ouvert.\nVeuillez fermer MediaMonitor.UI d'abord.",
                    LanguageManager.Get("Service en cours d'utilisation") ?? "Service en cours d'utilisation"
                );
                    return;
                }

                // 1. Arrêter le service
                foreach (var p in Process.GetProcessesByName("MediaMonitor.Service"))
                    p.Kill();
            }
            else
            {
                // 1. Démarrer le service
                string servicePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "MediaMonitor.Service.exe"
                );

                if (!File.Exists(servicePath))
                {
                PopupHelper.ShowBottomPopup(
                    this,
                    LanguageManager.Get("MediaMonitor.Service.exe introuvable.") ??
                    "MediaMonitor.Service.exe introuvable.",
                    "Erreur"
                );
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = servicePath,
                    UseShellExecute = true
                });

                Thread.Sleep(1200);

                // 2. Vérifier si le service tourne réellement
                bool serviceRunning = Process.GetProcesses()
                    .Any(p => p.ProcessName.StartsWith("MediaMonitor.Service", StringComparison.OrdinalIgnoreCase));

                if (!serviceRunning)
                {
                PopupHelper.ShowBottomPopup(
                    this,
                    "Le service MediaMonitor.Service n'a pas pu démarrer.",
                    "Erreur"
                );
                return;
                }

                // 3. Démarrer le Tray
                string trayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "MCEMonitor",
                    "MediaMonitor.Tray.exe"
                );

                if (!File.Exists(trayPath))
                {
                    trayPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "MCEMonitor",
                        "MediaMonitor.Tray.exe"
                    );
                }

                if (File.Exists(trayPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = trayPath,
                        UseShellExecute = true
                    });
                }
            }

            Task.Delay(500).ContinueWith(_ =>
            {
                this.Invoke(new Action(UpdateMediaToggle));
            });
        }

        private void MediaServiceTimer_Tick(object sender, EventArgs e)
        {
            UpdateMediaToggle();
            UpdateMediaTaskButtons();
        }

        private void UpdateMediaTaskButtons()
        {
            bool exists = TaskSchedulerHelper.MediaMonitorServiceTaskExists();

            btnCreateMediaTask2.Enabled = !exists;
            btnDeleteMediaTask2.Enabled = exists;

            Color lightGreen = Color.FromArgb(200, 255, 200);
            Color lightRed = Color.FromArgb(255, 200, 200);
            Color defaultColor = SystemColors.Control;

            btnCreateMediaTask2.BackColor = btnCreateMediaTask2.Enabled ? lightGreen : defaultColor;
            btnDeleteMediaTask2.BackColor = btnDeleteMediaTask2.Enabled ? lightRed : defaultColor;
        }
        private void UpdateLastReportLabel()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs",
                    "MediaMonitor.Schedule.log"
                );

                if (!File.Exists(path))
                {
                    lblLastReport.Text = "";
                    return;
                }

                // On cherche la DERNIÈRE ligne contenant [CODE02]
                string lastCode02 = File.ReadLines(path)
                    .Reverse()
                    .FirstOrDefault(l => l.Contains("[CODE02]"));

                if (string.IsNullOrWhiteSpace(lastCode02))
                {
                    lblLastReport.Text = "";
                    return;
                }

                // Retirer le timestamp "[xxxx-xx-xx xx:xx:xx] "
                int idx = lastCode02.IndexOf("] ");
                if (idx > 0)
                    lastCode02 = lastCode02.Substring(idx + 2);

                // Retirer le tag [CODE02]
                lastCode02 = lastCode02.Replace("[CODE02]", "").Trim();

                // Afficher EXACTEMENT ce qui reste
                lblLastReport.Text = lastCode02;
            }
            catch
            {
                lblLastReport.Text = "";
            }
        }
        private void LogRefreshTimer_Tick(object sender, EventArgs e)
        {
            UpdateNextReportLabel();
            UpdateLastReportLabel();
        }

        // ============================================================
        // ONGLET WAKE MONITOR
        // ============================================================

        private void LoadWakeConfig()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "MCEMonitor");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, "WakeMonitor.config");

            if (!File.Exists(path))
                return;

            var lines = File.ReadAllLines(path);

            foreach (var line in lines)
            {
                if (!line.Contains("=")) continue;

                var parts = line.Split('=');
                string key = parts[0].Trim();
                bool val = parts[1].Trim().ToLower() == "true";

                switch (key)
                {
                    case "IncludePublicIP": chkPublicIP.Checked = val; break;
                    case "IncludeLocalIP": chkLocalIP.Checked = val; break;
                    case "IncludeMAC": chkMAC.Checked = val; break;
                    case "IncludeUSB": chkUSB.Checked = val; break;
                    case "IncludeCause": chkCause.Checked = val; break;
                    case "IncludeDuration": chkDuration.Checked = val; break;
                }
            }
        }

        private void BtnSaveWakeConfig_Click(object sender, EventArgs e)
        {
            var lines = new[]
            {
                $"IncludePublicIP={chkPublicIP.Checked}",
                $"IncludeLocalIP={chkLocalIP.Checked}",
                $"IncludeMAC={chkMAC.Checked}",
                $"IncludeUSB={chkUSB.Checked}",
                $"IncludeCause={chkCause.Checked}",
                $"IncludeDuration={chkDuration.Checked}"
            };

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "MCEMonitor");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, "WakeMonitor.config");

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);

            PopupHelper.ShowBottomPopup(
                this,
                LanguageManager.Get("Configuration WakeMonitor enregistrée") ?? "Configuration WakeMonitor enregistrée",
                "Information"
            );
        }

        private async void BtnCreateWakeTask_Click(object sender, EventArgs e)
        {
            TaskSchedulerHelper.CreateWakeTask();
            await Task.Delay(500);
            UpdateWakeTaskStatus();
        }

        private async void BtnDeleteWakeTask_Click(object sender, EventArgs e)
        {
            TaskSchedulerHelper.DeleteWakeTask();
            await Task.Delay(500);
            UpdateWakeTaskStatus();
        }

        private void BtnRunWake_Click(object sender, EventArgs e)
        {
            try
            {
                string exePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "WakeMonitor.exe"
                );

                if (!File.Exists(exePath))
                {
                    PopupHelper.ShowBottomPopup(
                        this,
                        LanguageManager.Get("WakeMonitor.exe est introuvable dans C:\\ProgramData\\MCEMonitor.") ??
                        "WakeMonitor.exe est introuvable dans C:\\ProgramData\\MCEMonitor.",
                        LanguageManager.Get("Erreur") ?? "Erreur"
                    );
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                PopupHelper.ShowBottomPopup(
                    this,
                    LanguageManager.Get("WakeMonitor exécuté") ?? "WakeMonitor exécuté",
                    "Information"
                );
            }
            catch (Exception ex)
            {
              PopupHelper.ShowBottomPopup(
                  this,
                  (LanguageManager.Get("Impossible d'exécuter WakeMonitor.exe : ") ??
                  "Impossible d'exécuter WakeMonitor.exe :\n") + ex.Message,
                  LanguageManager.Get("Erreur") ?? "Erreur"
              );
            }
        }

        private void UpdateWakeTaskStatus()
        {
            bool exists = TaskSchedulerHelper.WakeTaskExists();

            btnCreateWakeTask.Enabled = !exists;
            btnDeleteWakeTask.Enabled = exists;

            Color lightGreen = Color.FromArgb(200, 255, 200);
            Color lightRed = Color.FromArgb(255, 200, 200);
            Color defaultColor = SystemColors.Control;

            btnCreateWakeTask.BackColor = btnCreateWakeTask.Enabled ? lightGreen : defaultColor;
            btnDeleteWakeTask.BackColor = btnDeleteWakeTask.Enabled ? lightRed : defaultColor;
        }

        private void BtnManageWolMacs_Click(object sender, EventArgs e)
        {
            try
            {
                using (var frm = new FormWolMacManager())
                {
                    frm.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(
                    this,
                    "Impossible d’ouvrir la gestion des MAC autorisées :\n" + ex.Message,
                    "Erreur"
                );
            }
        }

        private void BtnSaveOnOff_Click(object sender, EventArgs e)
        {
            int hour = (int)numShutdownHour.Value;
            int minute = (int)numShutdownMinute.Value;

            SaveShutdownConfig(hour, minute);

            PopupHelper.ShowBottomPopup(
                this,
                LanguageManager.Get("Configuration enregistrée.") ??
                "Configuration enregistrée.",
                "Information"
            );

            // Si une tâche existe déjà, on la met à jour
            if (TaskSchedulerHelper.ShutdownTaskExists())
            {
                string mode = cmbShutdownType.SelectedItem.ToString() == "Veille"
                    ? "sleep"
                    : "shutdown";

                TaskSchedulerHelper.CreateShutdownTask(hour, minute, mode);
            }

            UpdateShutdownTaskStatus();
        }

        // ============================================================
        // STOP MONITOR
        // ============================================================

        private async void BtnCreateStopTask_Click(object sender, EventArgs e)
        {
            TaskSchedulerHelper.CreateStopTask();
            await Task.Delay(500);
            UpdateStopTaskStatus();
        }

        private async void BtnDeleteStopTask_Click(object sender, EventArgs e)
        {
            TaskSchedulerHelper.DeleteStopTask();
            await Task.Delay(500);
            UpdateStopTaskStatus();
        }

        private void BtnRunStopMonitor_Click(object sender, EventArgs e)
        {
            try
            {
                string exePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "StopMonitor.exe"
                );

                if (!File.Exists(exePath))
                {
                    PopupHelper.ShowBottomPopup(
                        this,
                        LanguageManager.Get("StopMonitor.exe est introuvable dans C:\\ProgramData\\MCEMonitor.") ??
                        "StopMonitor.exe est introuvable dans C:\\ProgramData\\MCEMonitor.",
                        LanguageManager.Get("Erreur") ?? "Erreur"
                    );
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                    PopupHelper.ShowBottomPopup(
                        this,
                        LanguageManager.Get("StopMonitor exécuté") ?? "StopMonitor exécuté",
                        "Information"
                    );
            }
            catch (Exception ex)
            {
                  PopupHelper.ShowBottomPopup(
                      this,
                      (LanguageManager.Get("Impossible d'exécuter StopMonitor.exe :") ??
                      "Impossible d'exécuter StopMonitor.exe :\n") + ex.Message,
                      LanguageManager.Get("Erreur") ?? "Erreur"
                  );
            }
        }

        private void UpdateStopTaskStatus()
        {
            bool exists = TaskSchedulerHelper.StopTaskExists();

            btnCreateStopTask.Enabled = !exists;
            btnDeleteStopTask.Enabled = exists;

            Color lightGreen = Color.FromArgb(200, 255, 200);
            Color lightRed = Color.FromArgb(255, 200, 200);
            Color defaultColor = SystemColors.Control;

            btnCreateStopTask.BackColor = btnCreateStopTask.Enabled ? lightGreen : defaultColor;
            btnDeleteStopTask.BackColor = btnDeleteStopTask.Enabled ? lightRed : defaultColor;
        }

        // ============================================================
        // ARRÊT PROGRAMMÉ
        // ============================================================

        private void LoadShutdownConfig()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "Shutdown.config"
            );

            if (!File.Exists(path))
            {
                var t = TaskSchedulerHelper.GetShutdownTaskTime();
                if (t != null)
                {
                    numShutdownHour.Value = t.Value.hour;
                    numShutdownMinute.Value = t.Value.minute;
                }
                return;
            }

            var lines = File.ReadAllLines(path);

            foreach (var line in lines)
            {
                if (line.StartsWith("Hour="))
                    numShutdownHour.Value = int.Parse(line.Substring(5));

                if (line.StartsWith("Minute="))
                    numShutdownMinute.Value = int.Parse(line.Substring(7));
            }
        }

        private void SaveShutdownConfig(int hour, int minute)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "Shutdown.config"
            );

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            File.WriteAllLines(path, new[]
            {
                $"Hour={hour}",
                $"Minute={minute}"
            });
        }

        private void UpdateShutdownTaskStatus()
        {
            bool exists = TaskSchedulerHelper.ShutdownTaskExists();

            btnCreateShutdownTask.Enabled = !exists;
            btnDeleteShutdownTask.Enabled = exists;

            Color lightGreen = Color.FromArgb(200, 255, 200);
            Color lightRed = Color.FromArgb(255, 200, 200);
            Color defaultColor = SystemColors.Control;

            btnCreateShutdownTask.BackColor = btnCreateShutdownTask.Enabled ? lightGreen : defaultColor;
            btnDeleteShutdownTask.BackColor = btnDeleteShutdownTask.Enabled ? lightRed : defaultColor;

            if (exists)
            {
                string mode = TaskSchedulerHelper.GetShutdownTaskMode();

                if (mode == "sleep")
                    cmbShutdownType.SelectedItem = "Veille";
                else
                    cmbShutdownType.SelectedItem = "Arrêt";
            }
        }

        private void BtnCreateShutdownTask_Click(object sender, EventArgs e)
        {
            try
            {
                int hour = (int)numShutdownHour.Value;
                int minute = (int)numShutdownMinute.Value;

                string mode = cmbShutdownType.SelectedItem.ToString() == "Veille"
                    ? "sleep"
                    : "shutdown";

                TaskSchedulerHelper.CreateShutdownTask(hour, minute, mode);

                SaveShutdownConfig(hour, minute);

                PopupHelper.ShowBottomPopup(
                    this,
                    LanguageManager.Get("Tâche planifiée créée avec succès.") ??
                    "Tâche planifiée créée avec succès.",
                    "Information"
                );

                UpdateShutdownTaskStatus();
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(
                    this,
                    (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message,
                    "Erreur"
                );
            }
        }

        private void BtnDeleteShutdownTask_Click(object sender, EventArgs e)
        {
            try
            {
                TaskSchedulerHelper.DeleteShutdownTask();

                PopupHelper.ShowBottomPopup(
                    this,
                    LanguageManager.Get("Tâche planifiée supprimée avec succès.") ??
                    "Tâche planifiée supprimée avec succès.",
                    "Information"
                );

                UpdateShutdownTaskStatus();
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(
                    this,
                    (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message,
                    "Erreur"
                );
            }
        }
    }
}

