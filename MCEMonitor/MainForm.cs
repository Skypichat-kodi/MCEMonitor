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
using System.Collections.Generic;
using Krypton.Toolkit;
using Krypton.Navigator;

namespace MCEMonitor
{
    public partial class MainForm : KryptonForm
    {
        private readonly MediaMonitorService _media;
        private readonly WakeMonitorService _wake;
        private readonly List<KryptonPage> _lockedPages = new();
        private readonly Dictionary<KryptonPage, int> _originalTabOrder = new();
        private bool _smtpTabsUnlocked = false;

        // Garde-fous anti-récursion pour les toggles KryptonCheckButton
        private bool _suppressToggleMedia = false;
        private bool _suppressToggleRom = false;

        // Anti-double-fermeture
        private bool _closingInProgress = false;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (e.CloseReason == CloseReason.UserClosing && !_closingInProgress)
            {
                _closingInProgress = true;

                // 1. Cacher la fenêtre immédiatement (évite le clignotement)
                this.SuspendLayout();
                this.Visible = false;

                // 2. Arrêter tous les timers
                logRefreshTimer?.Stop();
                mediaServiceTimer?.Stop();
                romMonitorTimer?.Stop();

                // 3. Forcer la sortie propre
                Application.Exit();
            }
        }

        public MainForm(MediaMonitorService media, WakeMonitorService wake)
        {
            _media = media;
            _wake = wake;

            InitializeComponent();

            // Double-buffering pour éliminer les repaints parasites
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();

            // Fenêtre non redimensionnable
            this.MaximizeBox = false;
            this.MinimizeBox = true;

            this.StartPosition = FormStartPosition.CenterScreen;

            // Verrouille la taille (Min == Max ? non redimensionnable)
            this.MinimumSize = new Size(716, 579);
            this.MaximumSize = new Size(716, 579);

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "MediaMonitor.ico");

            if (File.Exists(iconPath))
                this.Icon = new Icon(iconPath);
            else
                this.Icon = SystemIcons.Application;

            LoadEmailConfig();
            LoadMediaConfig();
            LoadRomMonitorConfig();
            LoadWakeConfig();
            UpdateWakeTaskStatus();
            LoadShutdownConfig();
            UpdateShutdownTaskStatus();
            UpdateStopTaskStatus();
            UpdateNextReportLabel();
            UpdateLastReportLabel();
            InitializeSmtpTabGating();

            this.Shown += async (s, e) => await CheckSmtpAtStartupAsync();
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

            _ = Task.Run(async () =>
            {
                var status = await SmtpHealthChecker.CheckAsync(cacheMinutes: 0);

                if (this.IsDisposed || this.Disposing || _closingInProgress)
                    return;

                try
                {
                    if (InvokeRequired)
                        Invoke(new Action(() => UpdateEmailStatusBadge(status)));
                    else
                        UpdateEmailStatusBadge(status);
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });

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

                _ = RefreshSmtpStatusAsync();

                SmtpHealthChecker.MarkAsReady();
                UnlockTabs();

                UpdateEmailStatusBadge(SmtpStatus.Ready);
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

        private string TranslateStatusCode(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return "";

            if (rawLine.StartsWith("[CODE01]|"))
            {
                var parts = rawLine.Substring("[CODE01]|".Length).Split('|');
                string heure = parts.Length > 0 ? parts[0] : "";
                string dans  = parts.Length > 1 ? parts[1] : "";

                string label = LanguageManager.Get("Prochain envoi du rapport prévu à")
                               ?? "Prochain envoi du rapport prévu à";
                string labelDans = LanguageManager.Get("dans") ?? "dans";

                return $"{label} {heure} ({labelDans} {dans})";
            }

            if (rawLine.StartsWith("[CODE02]|"))
            {
                string value = rawLine.Substring("[CODE02]|".Length).Trim();

                if (value == "AUCUN")
                    return LanguageManager.Get("Aucun rapport envoyé") ?? "Aucun rapport envoyé";

                string label = LanguageManager.Get("Rapport envoyé à") ?? "Rapport envoyé à";
                return $"{label} {value}";
            }

            return rawLine;
        }

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

                string lastCode01 = File.ReadLines(path)
                    .Reverse()
                    .FirstOrDefault(l => l.Contains("[CODE01]"));

                if (string.IsNullOrWhiteSpace(lastCode01))
                {
                    lblNextReport.Text = "";
                    return;
                }

                int idx = lastCode01.IndexOf("] ");
                if (idx > 0)
                    lastCode01 = lastCode01.Substring(idx + 2);

                lblNextReport.Text = TranslateStatusCode(lastCode01);
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
            bool serviceRunning = Process.GetProcessesByName("MediaMonitor.Service").Length > 0;

            if (!serviceRunning)
            {
                string message =
                    (LanguageManager.Get("Le service MediaMonitor n'est pas en cours d'exécution.")
                        ?? "Le service MediaMonitor n'est pas en cours d'exécution.")
                    + "\n\n" +
                    (LanguageManager.Get("Voulez-vous le démarrer maintenant ?")
                        ?? "Voulez-vous le démarrer maintenant ?");

                bool confirmed = ConfirmDialog.Show(
                    this,
                    message,
                    LanguageManager.Get("Service non démarré") ?? "Service non démarré",
                    yesText: LanguageManager.Get("Démarrer") ?? "Démarrer",
                    noText: LanguageManager.Get("Annuler") ?? "Annuler",
                    warning: true);

                if (!confirmed)
                    return;

                if (!StartMediaMonitorService())
                    return;

                UpdateMediaToggle();
            }

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
            _suppressToggleMedia = true;
            try
            {
                bool running = IsMediaServiceRunning();

                if (running)
                {
                    toggleMediaService.Checked = true;
                    lblMediaStatus.Text =
                        LanguageManager.Get("Service MediaMonitor : actif") ??
                        "Service MediaMonitor : actif";
                }
                else
                {
                    toggleMediaService.Checked = false;
                    lblMediaStatus.Text =
                        LanguageManager.Get("Service MediaMonitor : arrêté") ??
                        "Service MediaMonitor : arrêté";
                }
            }
            finally
            {
                _suppressToggleMedia = false;
            }
        }

        private void toggleMediaService_Click(object sender, EventArgs e)
        {
            if (_suppressToggleMedia)
                return;

            _suppressToggleMedia = true;
            try
            {
                bool running = IsMediaServiceRunning();

                if (running)
                {
                    if (IsMediaUIRunning())
                    {
                        toggleMediaService.Checked = true;

                        PopupHelper.ShowBottomPopup(
                            this,
                            LanguageManager.Get("Impossible d'arrêter MediaMonitor.Service tant que MediaMonitor.UI est ouvert. Veuillez fermer MediaMonitor.UI d'abord.") ??
                            "Impossible d'arrêter MediaMonitor.Service tant que MediaMonitor.UI est ouvert.\nVeuillez fermer MediaMonitor.UI d'abord.",
                            LanguageManager.Get("Service en cours d'utilisation") ?? "Service en cours d'utilisation"
                        );
                        return;
                    }

                    foreach (var p in Process.GetProcessesByName("MediaMonitor.Service"))
                        p.Kill();
                }
                else
                {
                    string servicePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MCEMonitor",
                        "MediaMonitor.Service.exe"
                    );

                    if (!File.Exists(servicePath))
                    {
                        toggleMediaService.Checked = false;
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

                    bool serviceRunning = Process.GetProcesses()
                        .Any(p => p.ProcessName.StartsWith("MediaMonitor.Service", StringComparison.OrdinalIgnoreCase));

                    if (!serviceRunning)
                    {
                        toggleMediaService.Checked = false;
                        PopupHelper.ShowBottomPopup(
                            this,
                            "Le service MediaMonitor.Service n'a pas pu démarrer.",
                            "Erreur"
                        );
                        return;
                    }

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
            }
            finally
            {
                _suppressToggleMedia = false;
            }

            Task.Delay(500).ContinueWith(_ =>
            {
                this.Invoke(new Action(UpdateMediaToggle));
            });
        }

        private void MediaServiceTimer_Tick(object sender, EventArgs e)
        {
            if (_closingInProgress || this.IsDisposed)
                return;

            UpdateMediaToggle();
            UpdateMediaTaskButtons();
        }

        private void UpdateMediaTaskButtons()
        {
            bool exists = TaskSchedulerHelper.MediaMonitorServiceTaskExists();
            btnCreateMediaTask2.Enabled = !exists;
            btnDeleteMediaTask2.Enabled = exists;
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

                string lastCode02 = File.ReadLines(path)
                    .Reverse()
                    .FirstOrDefault(l => l.Contains("[CODE02]"));

                if (string.IsNullOrWhiteSpace(lastCode02))
                {
                    lblLastReport.Text = "";
                    return;
                }

                int idx = lastCode02.IndexOf("] ");
                if (idx > 0)
                    lastCode02 = lastCode02.Substring(idx + 2);

                lblLastReport.Text = TranslateStatusCode(lastCode02);
            }
            catch
            {
                lblLastReport.Text = "";
            }
        }

        private void LogRefreshTimer_Tick(object sender, EventArgs e)
        {
            if (_closingInProgress || this.IsDisposed)
                return;

            UpdateNextReportLabel();
            UpdateLastReportLabel();

            _ = RefreshSmtpStatusAsync();
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
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
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
                    "Impossible d'ouvrir la gestion des MAC autorisées :\n" + ex.Message,
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
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
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

        // ============================================================
        // ROM MONITOR
        // ============================================================

        private void LoadRomMonitorConfig()
        {
            UpdateRomMonitorToggle();
            UpdateRomTaskButtons();
            LoadRomMonitorSettings();
        }

        private void LoadRomMonitorSettings()
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "RomMonitor.config"
                );

                if (!File.Exists(configPath))
                    return;

                foreach (var line in File.ReadAllLines(configPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string val = parts[1].Trim();

                    switch (key)
                    {
                        case "Interval":
                            if (int.TryParse(val, out int i))
                                numRomInterval.Value = Math.Max(numRomInterval.Minimum, Math.Min(numRomInterval.Maximum, i));
                            break;

                        case "DiskSpaceWarnPercent":
                            if (int.TryParse(val, out int wp))
                                numRomWarnPct.Value = Math.Max(numRomWarnPct.Minimum, Math.Min(numRomWarnPct.Maximum, wp));
                            break;

                        case "DiskSpaceCriticalPercent":
                            if (int.TryParse(val, out int cp))
                                numRomCritPct.Value = Math.Max(numRomCritPct.Minimum, Math.Min(numRomCritPct.Maximum, cp));
                            break;

                        case "DiskSpaceWarnGo":
                            if (int.TryParse(val, out int wg))
                                numRomWarnGo.Value = Math.Max(numRomWarnGo.Minimum, Math.Min(numRomWarnGo.Maximum, wg));
                            break;

                        case "DiskSpaceCriticalGo":
                            if (int.TryParse(val, out int cg))
                                numRomCritGo.Value = Math.Max(numRomCritGo.Minimum, Math.Min(numRomCritGo.Maximum, cg));
                            break;

                        case "AlertOnSmartFailure":
                            chkRomSmartAlert.Checked = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;

                        case "AlertCooldownHours":
                            if (int.TryParse(val, out int cd))
                                numRomCooldown.Value = Math.Max(numRomCooldown.Minimum, Math.Min(numRomCooldown.Maximum, cd));
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Erreur LoadRomMonitorSettings : " + ex.Message);
            }
        }

        private void BtnSaveRomConfig_Click(object sender, EventArgs e)
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "RomMonitor.config"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

                bool webEnabled = true;
                int webPort = 8085;
                string webUsername = "admin";
                string webPassword = "changeme";

                if (File.Exists(configPath))
                {
                    foreach (var line in File.ReadAllLines(configPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                            continue;

                        var parts = line.Split('=', 2);
                        if (parts.Length != 2) continue;

                        string key = parts[0].Trim();
                        string val = parts[1].Trim();

                        switch (key)
                        {
                            case "WebEnabled":
                                if (bool.TryParse(val, out bool we)) webEnabled = we;
                                break;
                            case "WebPort":
                                if (int.TryParse(val, out int wp)) webPort = wp;
                                break;
                            case "WebUsername":
                                webUsername = val;
                                break;
                            case "WebPassword":
                                webPassword = val;
                                break;
                        }
                    }
                }

                var lines = new[]
                {
                    "# ============================================================",
                    "# RomMonitor.config",
                    "# Configuration du service RomMonitor",
                    "# ============================================================",
                    "",
                    "# Fréquence de contrôle (minutes)",
                    $"Interval={(int)numRomInterval.Value}",
                    "",
                    "# Seuil d'alerte espace disque (% libre)",
                    $"DiskSpaceWarnPercent={(int)numRomWarnPct.Value}",
                    $"DiskSpaceCriticalPercent={(int)numRomCritPct.Value}",
                    "",
                    "# Seuil d'alerte espace disque (Go libre)",
                    "DiskSpaceWarnGo=10",
                    "DiskSpaceCriticalGo=5",
                    "",
                    "# Alertes email",
                    $"AlertOnSmartFailure={chkRomSmartAlert.Checked.ToString().ToLower()}",
                    "AlertOnLowDiskSpace=true",
                    "",
                    "# Anti-spam : délai minimum entre 2 alertes email du même type (heures)",
                    $"AlertCooldownHours={(int)numRomCooldown.Value}",
                    "",
                    "# Serveur Web",
                    $"WebEnabled={webEnabled.ToString().ToLower()}",
                    $"WebPort={webPort}",
                    $"WebUsername={webUsername}",
                    $"WebPassword={webPassword}"
                };

                File.WriteAllLines(configPath, lines);

                PopupHelper.ShowBottomPopup(
                    this,
                    LanguageManager.Get("Réglages RomMonitor enregistrés") ?? "Réglages RomMonitor enregistrés",
                    "Information"
                );
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(
                    this,
                    (LanguageManager.Get("Erreur lors de l'enregistrement : ") ?? "Erreur lors de l'enregistrement : ") + ex.Message,
                    "Erreur"
                );
            }
        }

        private void UpdateRomMonitorToggle()
        {
            _suppressToggleRom = true;
            try
            {
                bool running = Process.GetProcessesByName("RomMonitor.Service").Length > 0;

                if (running)
                {
                    toggleRomService.Checked = true;
                    lblRomStatus.Text =
                        LanguageManager.Get("Service RomMonitor : actif") ?? "Service RomMonitor : actif";
                }
                else
                {
                    toggleRomService.Checked = false;
                    lblRomStatus.Text =
                        LanguageManager.Get("Service RomMonitor : arrêté") ?? "Service RomMonitor : arrêté";
                }
            }
            finally
            {
                _suppressToggleRom = false;
            }
        }

        private void toggleRomService_Click(object sender, EventArgs e)
        {
            if (_suppressToggleRom)
                return;

            _suppressToggleRom = true;
            try
            {
                bool running = Process.GetProcessesByName("RomMonitor.Service").Length > 0;

                if (running)
                {
                    if (Process.GetProcessesByName("RomMonitor.UI").Length > 0)
                    {
                        toggleRomService.Checked = true;

                        PopupHelper.ShowBottomPopup(
                            this,
                            LanguageManager.Get("Impossible d'arrêter RomMonitor.Service tant que RomMonitor.UI est ouvert. Veuillez fermer RomMonitor.UI d'abord.")
                                ?? "Impossible d'arrêter RomMonitor.Service tant que RomMonitor.UI est ouvert.\nVeuillez fermer RomMonitor.UI d'abord.",
                            LanguageManager.Get("Service en cours d'utilisation") ?? "Service en cours d'utilisation"
                        );
                        return;
                    }

                    foreach (var p in Process.GetProcessesByName("RomMonitor.Service"))
                        p.Kill();
                }
                else
                {
                    StartRomMonitorService();
                }
            }
            finally
            {
                _suppressToggleRom = false;
            }

            Task.Delay(800).ContinueWith(_ =>
            {
                this.Invoke(new Action(UpdateRomMonitorToggle));
            });
        }

        /// <summary>
        /// Démarre le service MediaMonitor et lance le Tray.
        /// Renvoie true si le service tourne à la fin.
        /// </summary>
        private bool StartMediaMonitorService()
        {
            try
            {
                string servicePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "MediaMonitor.Service.exe"
                );

                if (!File.Exists(servicePath))
                {
                    PopupHelper.ShowBottomPopup(
                        this,
                        LanguageManager.Get("MediaMonitor.Service.exe introuvable.") ?? "MediaMonitor.Service.exe introuvable.",
                        "Erreur"
                    );
                    return false;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = servicePath,
                    UseShellExecute = true
                });

                Thread.Sleep(1200);

                // Vérifier que le service tourne
                if (Process.GetProcessesByName("MediaMonitor.Service").Length == 0)
                {
                    PopupHelper.ShowBottomPopup(
                        this,
                        "Le service MediaMonitor n'a pas pu démarrer.",
                        "Erreur"
                    );
                    return false;
                }

                // Démarrer le Tray
                StartMediaMonitorTray();

                return true;
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(
                    this,
                    "Erreur lors du démarrage du service MediaMonitor : " + ex.Message,
                    "Erreur"
                );
                return false;
            }
        }

        /// <summary>
        /// Lance le Tray MediaMonitor s'il n'est pas déjà en cours.
        /// </summary>
        private void StartMediaMonitorTray()
        {
            try
            {
                if (Process.GetProcessesByName("MediaMonitor.Tray").Length > 0)
                    return;

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
            catch { }
        }

        private void BtnOpenRomUI_Click(object sender, EventArgs e)
        {
            bool serviceRunning = Process.GetProcessesByName("RomMonitor.Service").Length > 0;

            if (!serviceRunning)
            {
                string message =
                    (LanguageManager.Get("Le service RomMonitor n'est pas en cours d'exécution.")
                        ?? "Le service RomMonitor n'est pas en cours d'exécution.")
                    + "\n\n" +
                    (LanguageManager.Get("Voulez-vous le démarrer maintenant ?")
                        ?? "Voulez-vous le démarrer maintenant ?");

                bool confirmed = ConfirmDialog.Show(
                    this,
                    message,
                    LanguageManager.Get("Service non démarré") ?? "Service non démarré",
                    yesText: LanguageManager.Get("Démarrer") ?? "Démarrer",
                    noText: LanguageManager.Get("Annuler") ?? "Annuler",
                    warning: true);

                if (!confirmed)
                    return;

                if (!StartRomMonitorService())
                    return;

                UpdateRomMonitorToggle();
            }

            try
            {
                string uiPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "MCEMonitor",
                    "RomMonitor.UI.exe"
                );

                if (!File.Exists(uiPath))
                {
                    uiPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "MCEMonitor",
                        "RomMonitor.UI.exe"
                    );
                }

                if (!File.Exists(uiPath))
                {
                    PopupHelper.ShowBottomPopup(
                        this,
                        "RomMonitor.UI.exe est introuvable.",
                        "Erreur"
                    );
                    return;
                }

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
                PopupHelper.ShowBottomPopup(this, "Erreur lors de l'ouverture de RomMonitor.UI : " + ex.Message);
            }
        }

        private void RomMonitorTimer_Tick(object sender, EventArgs e)
        {
            if (_closingInProgress || this.IsDisposed)
                return;

            UpdateRomMonitorToggle();
            UpdateRomTaskButtons();
        }

        private void UpdateRomTaskButtons()
        {
            bool exists = TaskSchedulerHelper.RomMonitorTaskExists();
            btnCreateRomTask.Enabled = !exists;
            btnDeleteRomTask.Enabled = exists;
        }

        private void BtnDeleteRomTask_Click(object sender, EventArgs e)
        {
            try
            {
                string result = TaskSchedulerHelper.DeleteRomMonitorTask();

                ServiceInstaller.DeleteRomTrayTask();

                PopupHelper.ShowBottomPopup(
                    this,
                    result,
                    LanguageManager.Get("Résultat suppression tâche RomMonitor") ?? "Résultat suppression tâche RomMonitor"
                );

                UpdateRomTaskButtons();
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(this, (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message);
            }
        }

        private void BtnCreateRomTask_Click(object sender, EventArgs e)
        {
            try
            {
                string result = TaskSchedulerHelper.CreateRomMonitorTask();

                if (!ServiceInstaller.RomTrayTaskExists())
                    ServiceInstaller.CreateRomTrayTask();

                PopupHelper.ShowBottomPopup(
                    this,
                    result,
                    LanguageManager.Get("Résultat création tâche RomMonitor") ?? "Résultat création tâche RomMonitor"
                );

                UpdateRomTaskButtons();
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(this, (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message);
            }
        }

        private bool StartRomMonitorService()
        {
            try
            {
                string servicePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "RomMonitor.Service.exe"
                );

                if (!File.Exists(servicePath))
                {
                    PopupHelper.ShowBottomPopup(
                        this,
                        LanguageManager.Get("RomMonitor.Service.exe introuvable.") ?? "RomMonitor.Service.exe introuvable.",
                        "Erreur"
                    );
                    return false;
                }

                string lang = LanguageManager.CurrentLanguage ?? "fr-FR";

                Process.Start(new ProcessStartInfo
                {
                    FileName = servicePath,
                    Arguments = $"-lang {lang}",
                    UseShellExecute = true
                });

                Thread.Sleep(1200);

                if (Process.GetProcessesByName("RomMonitor.Service").Length == 0)
                {
                    PopupHelper.ShowBottomPopup(
                        this,
                        "Le service RomMonitor n'a pas pu démarrer.",
                        "Erreur"
                    );
                    return false;
                }

                StartRomMonitorTray();

                return true;
            }
            catch (Exception ex)
            {
                PopupHelper.ShowBottomPopup(
                    this,
                    "Erreur lors du démarrage du service RomMonitor : " + ex.Message,
                    "Erreur"
                );
                return false;
            }
        }

        private void StartRomMonitorTray()
        {
            try
            {
                if (Process.GetProcessesByName("RomMonitor.Tray").Length > 0)
                    return;

                string trayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "MCEMonitor",
                    "RomMonitor.Tray.exe"
                );

                if (!File.Exists(trayPath))
                {
                    trayPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "MCEMonitor",
                        "RomMonitor.Tray.exe"
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
            catch { }
        }

        // ============================================================
        // GESTION SMTP : verrouillage / déverrouillage des onglets
        // ============================================================

        private void InitializeSmtpTabGating()
        {
            for (int i = 0; i < tabControl.Pages.Count; i++)
                _originalTabOrder[tabControl.Pages[i]] = i;

            _lockedPages.Add(tabMediaMonitor);
            _lockedPages.Add(tabRomMonitor);
            _lockedPages.Add(tabWakeMonitor);
            _lockedPages.Add(tabStopMonitor);

            LockTabs();
        }

        private void LockTabs()
        {
            _smtpTabsUnlocked = false;

            foreach (var page in _lockedPages)
            {
                if (tabControl.Pages.Contains(page))
                    tabControl.Pages.Remove(page);
            }
        }

        private void UnlockTabs()
        {
            if (_smtpTabsUnlocked)
                return;

            _smtpTabsUnlocked = true;

            foreach (var kvp in _originalTabOrder.OrderBy(x => x.Value))
            {
                var page = kvp.Key;

                if (!_lockedPages.Contains(page))
                    continue;

                if (tabControl.Pages.Contains(page))
                    continue;

                int targetIndex = Math.Min(kvp.Value, tabControl.Pages.Count);
                tabControl.Pages.Insert(targetIndex, page);
            }
        }

        private async Task CheckSmtpAtStartupAsync()
        {
            try
            {
                var status = await SmtpHealthChecker.CheckAsync(cacheMinutes: 0);

                if (status == SmtpStatus.Ready)
                {
                    UnlockTabs();
                    return;
                }

                string message = GetSmtpWarningMessage(status) +
                    "\n\n" +
                    (LanguageManager.Get("Vous pouvez configurer l'envoi d'emails dans l'onglet Email.")
                        ?? "Vous pouvez configurer l'envoi d'emails dans l'onglet Email.");

                // ? CORRECTION : plus de "this," en premier argument
                KryptonMessageBox.Show(
                    message,
                    "Configuration Email requise",
                    KryptonMessageBoxButtons.OK,
                    KryptonMessageBoxIcon.Information);

                tabControl.SelectedPage = tabEmail;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Erreur CheckSmtpAtStartup : " + ex.Message);
            }
        }

        private static string GetSmtpWarningMessage(SmtpStatus status)
        {
            return status switch
            {
                SmtpStatus.NotConfigured =>
                    LanguageManager.Get("La configuration email n'a pas encore été remplie.\nLes modules ne pourront pas envoyer de rapports tant que ce ne sera pas fait.")
                    ?? "La configuration email n'a pas encore été remplie.\nLes modules ne pourront pas envoyer de rapports tant que ce ne sera pas fait.",

                SmtpStatus.Unreachable =>
                    LanguageManager.Get("Le serveur SMTP est injoignable.\nVérifiez l'adresse et le port.")
                    ?? "Le serveur SMTP est injoignable.\nVérifiez l'adresse et le port.",

                SmtpStatus.AuthFailed =>
                    LanguageManager.Get("L'authentification SMTP a échoué.\nVérifiez l'adresse expéditeur et le mot de passe.")
                    ?? "L'authentification SMTP a échoué.\nVérifiez l'adresse expéditeur et le mot de passe.",

                _ =>
                    LanguageManager.Get("Impossible de vérifier la configuration SMTP.\nConsultez les logs pour plus de détails.")
                    ?? "Impossible de vérifier la configuration SMTP.\nConsultez les logs pour plus de détails."
            };
        }

        private void UpdateEmailStatusBadge(SmtpStatus status)
        {
            if (pnlSmtpStatusDot == null)
                return;

            Color color;

            switch (status)
            {
                case SmtpStatus.Ready:
                    color = Color.FromArgb(76, 175, 80);
                    break;

                case SmtpStatus.NotConfigured:
                    color = Color.FromArgb(160, 160, 160);
                    break;

                case SmtpStatus.Unreachable:
                case SmtpStatus.AuthFailed:
                    color = Color.FromArgb(220, 60, 60);
                    break;

                default:
                    color = Color.FromArgb(230, 150, 30);
                    break;
            }

            pnlSmtpStatusDot.BackColor = color;
        }

        private async Task RefreshSmtpStatusAsync()
        {
            var status = await SmtpHealthChecker.CheckAsync(cacheMinutes: 10);

            if (InvokeRequired)
                Invoke(new Action(() => UpdateEmailStatusBadge(status)));
            else
                UpdateEmailStatusBadge(status);

            if (toolTipSmtp != null)
            {
                string tooltip = status switch
                {
                    SmtpStatus.Ready         => LanguageManager.Get("Connexion SMTP opérationnelle.")
                                                ?? "Connexion SMTP opérationnelle.",
                    SmtpStatus.NotConfigured => LanguageManager.Get("Configuration email incomplète.")
                                                ?? "Configuration email incomplète.",
                    SmtpStatus.Unreachable   => LanguageManager.Get("Le serveur SMTP ne répond pas.")
                                                ?? "Le serveur SMTP ne répond pas.",
                    SmtpStatus.AuthFailed    => LanguageManager.Get("Identifiants SMTP refusés.")
                                                ?? "Identifiants SMTP refusés.",
                    _                        => LanguageManager.Get("Statut SMTP inconnu.")
                                                ?? "Statut SMTP inconnu."
                };

                toolTipSmtp.SetToolTip(pnlSmtpStatusDot, tooltip);
            }
        }

        // ============================================================
        // SÉLECTEUR DE THÈME
        // ============================================================

        private void BtnChooseTheme_Click(object sender, EventArgs e)
        {
            using var selector = new ThemeSelectorForm();
            selector.ShowDialog(this);
        }
    }
}