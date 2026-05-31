using System;
using System.Drawing;
using System.Windows.Forms;

namespace MCEMonitor
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.tabControl = new System.Windows.Forms.TabControl();

            // Onglets principaux
            this.tabEmail = new System.Windows.Forms.TabPage();
            this.tabMediaMonitor = new System.Windows.Forms.TabPage();
            this.tabWakeMonitor = new System.Windows.Forms.TabPage();
            this.tabStopMonitor = new System.Windows.Forms.TabPage();
            this.tabOnOff = new System.Windows.Forms.TabPage();
            this.tabAbout = new System.Windows.Forms.TabPage();
            this.logRefreshTimer = new System.Windows.Forms.Timer();
            this.logRefreshTimer.Interval = 2000; // 2 secondes
            this.logRefreshTimer.Tick += new System.EventHandler(this.LogRefreshTimer_Tick);
            this.logRefreshTimer.Start();           
            this.ResumeLayout(false);
            this.PerformLayout();
            

            // ============================================================
            // SUSPEND LAYOUT
            // ============================================================

            this.tabControl.SuspendLayout();
            this.SuspendLayout();

            // ============================================================
            // POLICE NORMALE
            // ============================================================

            var normalFont = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular);

            // ============================================================
            // MAIN WINDOW
            // ============================================================

            this.ClientSize = new System.Drawing.Size(700, 470);
            this.Text = LanguageManager.Get("App.Title") ?? "MCEMonitor";

            // ============================================================
            // TAB CONTROL
            // ============================================================

            this.tabControl.Location = new System.Drawing.Point(10, 10);
            this.tabControl.Size = new System.Drawing.Size(680, 450);

            this.tabControl.Controls.Add(this.tabEmail);
            this.tabControl.Controls.Add(this.tabMediaMonitor);
            this.tabControl.Controls.Add(this.tabWakeMonitor);
            this.tabControl.Controls.Add(this.tabStopMonitor);
            this.tabControl.Controls.Add(this.tabOnOff);
            this.tabControl.Controls.Add(this.tabAbout);

            // ============================================================
            // CONFIGURATION DES ONGLETS
            // ============================================================

            this.tabEmail.Location = new System.Drawing.Point(4, 29);
            this.tabEmail.Size = new System.Drawing.Size(672, 467);
            this.tabEmail.Text = LanguageManager.Get("Tab.Email") ?? "Email";
            this.tabEmail.UseVisualStyleBackColor = true;

            this.tabMediaMonitor.Location = new System.Drawing.Point(4, 29);
            this.tabMediaMonitor.Size = new System.Drawing.Size(672, 467);
            this.tabMediaMonitor.Text = LanguageManager.Get("Tab.MediaMonitor") ?? "Media Monitor";
            this.tabMediaMonitor.UseVisualStyleBackColor = true;

            this.tabWakeMonitor.Location = new System.Drawing.Point(4, 29);
            this.tabWakeMonitor.Size = new System.Drawing.Size(672, 467);
            this.tabWakeMonitor.Text = LanguageManager.Get("Tab.WakeMonitor") ?? "Wake Monitor";
            this.tabWakeMonitor.UseVisualStyleBackColor = true;

            this.tabStopMonitor.Location = new System.Drawing.Point(4, 29);
            this.tabStopMonitor.Size = new System.Drawing.Size(672, 467);
            this.tabStopMonitor.Text = LanguageManager.Get("Tab.StopMonitor") ?? "Stop Monitor";
            this.tabStopMonitor.UseVisualStyleBackColor = true;

            this.tabOnOff.Location = new System.Drawing.Point(4, 29);
            this.tabOnOff.Size = new System.Drawing.Size(672, 467);
            this.tabOnOff.Text = LanguageManager.Get("Tab.OnOff") ?? "On/Off";
            this.tabOnOff.UseVisualStyleBackColor = true;

            this.tabAbout.Location = new System.Drawing.Point(4, 29);
            this.tabAbout.Size = new System.Drawing.Size(672, 467);
            this.tabAbout.Text = LanguageManager.Get("Tab.About") ?? "À propos";
            this.tabAbout.UseVisualStyleBackColor = true;

            // ============================================================
            // EMAIL — PANEL INFO
            // ============================================================

            this.pnlEmailInfo = new System.Windows.Forms.Panel();
            this.picEmailInfo = new System.Windows.Forms.PictureBox();
            this.lblEmailDescription = new System.Windows.Forms.Label();
            this.lblEmailTitle = new System.Windows.Forms.Label();

            this.lblSmtpServer = new System.Windows.Forms.Label();
            this.lblSmtpPort = new System.Windows.Forms.Label();
            this.lblEmailFrom = new System.Windows.Forms.Label();
            this.lblEmailPassword = new System.Windows.Forms.Label();
            this.lblEmailTo = new System.Windows.Forms.Label();
            this.lblSecurityMode = new System.Windows.Forms.Label();

            this.txtSmtpServer = new System.Windows.Forms.TextBox();
            this.txtSmtpPort = new System.Windows.Forms.TextBox();
            this.txtEmailFrom = new System.Windows.Forms.TextBox();
            this.txtEmailPassword = new System.Windows.Forms.TextBox();
            this.txtEmailTo = new System.Windows.Forms.TextBox();

            this.cmbSecurityMode = new System.Windows.Forms.ComboBox();

            this.btnSaveEmail = new System.Windows.Forms.Button();
            this.btnTestEmail = new System.Windows.Forms.Button();
            this.btnTogglePassword = new System.Windows.Forms.Button();

            // PANEL INFO
            this.pnlEmailInfo.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.pnlEmailInfo.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlEmailInfo.Location = new System.Drawing.Point(15, 10);
            this.pnlEmailInfo.Size = new System.Drawing.Size(640, 40);

            this.picEmailInfo.Location = new System.Drawing.Point(10, 5);
            this.picEmailInfo.Size = new System.Drawing.Size(28, 28);
            this.picEmailInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picEmailInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblEmailDescription.AutoSize = false;
            this.lblEmailDescription.Location = new System.Drawing.Point(45, 5);
            this.lblEmailDescription.Size = new System.Drawing.Size(580, 30);
            this.lblEmailDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblEmailDescription.Text =
                LanguageManager.Get("Email.Description") ??
                "Vous devez entrer vos informations d'envoi et un destinataire afin que les autres fonctionnalités du logiciel puissent envoyer des rapports automatiquement.";

            this.pnlEmailInfo.Controls.Add(this.picEmailInfo);
            this.pnlEmailInfo.Controls.Add(this.lblEmailDescription);
            this.tabEmail.Controls.Add(this.pnlEmailInfo);

            // TITRE EMAIL
            this.lblEmailTitle.AutoSize = true;
            this.lblEmailTitle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblEmailTitle.Location = new System.Drawing.Point(20, 60);
            this.lblEmailTitle.Text =
                LanguageManager.Get("Email.Title") ?? "Configuration Email";
            this.tabEmail.Controls.Add(this.lblEmailTitle);

            // ALIGNEMENT DES CHAMPS
            int labelX = 40;
            int labelWidth = 150;
            int fieldX = 200;
            int fieldWidth = 300;
            int y = 100;
            int step = 28;

            // Serveur SMTP
            this.lblSmtpServer.Text = LanguageManager.Get("Email.SmtpServer") ?? "Serveur SMTP :";
            this.lblSmtpServer.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblSmtpServer.Location = new System.Drawing.Point(labelX, y);
            this.lblSmtpServer.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblSmtpServer.Font = normalFont;

            this.txtSmtpServer.Location = new System.Drawing.Point(fieldX, y);
            this.txtSmtpServer.Size = new System.Drawing.Size(fieldWidth, 20);
            this.txtSmtpServer.Font = normalFont;

            y += step;

            // Port
            this.lblSmtpPort.Text = LanguageManager.Get("Email.Port") ?? "Port :";
            this.lblSmtpPort.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblSmtpPort.Location = new System.Drawing.Point(labelX, y);
            this.lblSmtpPort.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblSmtpPort.Font = normalFont;

            this.txtSmtpPort.Location = new System.Drawing.Point(fieldX, y);
            this.txtSmtpPort.Size = new System.Drawing.Size(80, 20);
            this.txtSmtpPort.Font = normalFont;

            y += step;

            // Adresse expéditeur
            this.lblEmailFrom.Text = LanguageManager.Get("Email.From") ?? "Adresse expéditeur :";
            this.lblEmailFrom.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblEmailFrom.Location = new System.Drawing.Point(labelX, y);
            this.lblEmailFrom.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblEmailFrom.Font = normalFont;

            this.txtEmailFrom.Location = new System.Drawing.Point(fieldX, y);
            this.txtEmailFrom.Size = new System.Drawing.Size(fieldWidth, 20);
            this.txtEmailFrom.Font = normalFont;

            y += step;

            // Mot de passe
            this.lblEmailPassword.Text = LanguageManager.Get("Email.Password") ?? "Mot de passe :";
            this.lblEmailPassword.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblEmailPassword.Location = new System.Drawing.Point(labelX, y);
            this.lblEmailPassword.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblEmailPassword.Font = normalFont;

            this.txtEmailPassword.Location = new System.Drawing.Point(fieldX, y);
            this.txtEmailPassword.Size = new System.Drawing.Size(fieldWidth - 90, 20);
            this.txtEmailPassword.PasswordChar = '*';
            this.txtEmailPassword.Font = normalFont;

            this.btnTogglePassword.Location = new System.Drawing.Point(fieldX + fieldWidth - 80, y - 1);
            this.btnTogglePassword.Size = new System.Drawing.Size(80, 22);
            this.btnTogglePassword.Text = LanguageManager.Get("Email.Password.Show") ?? "Afficher";
            this.btnTogglePassword.Font = normalFont;
            this.btnTogglePassword.Click += new System.EventHandler(this.BtnTogglePassword_Click);

            y += step;

            // Destinataire
            this.lblEmailTo.Text = LanguageManager.Get("Email.To") ?? "Destinataire :";
            this.lblEmailTo.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblEmailTo.Location = new System.Drawing.Point(labelX, y);
            this.lblEmailTo.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblEmailTo.Font = normalFont;

            this.txtEmailTo.Location = new System.Drawing.Point(fieldX, y);
            this.txtEmailTo.Size = new System.Drawing.Size(fieldWidth, 20);
            this.txtEmailTo.Font = normalFont;

            y += step;

            // Sécurité
            this.lblSecurityMode.Text = LanguageManager.Get("Email.SecurityMode") ?? "Sécurité :";
            this.lblSecurityMode.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblSecurityMode.Location = new System.Drawing.Point(labelX, y);
            this.lblSecurityMode.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblSecurityMode.Font = normalFont;

            this.cmbSecurityMode.Location = new System.Drawing.Point(fieldX, y);
            this.cmbSecurityMode.Size = new System.Drawing.Size(150, 20);
            this.cmbSecurityMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbSecurityMode.Items.AddRange(new object[] { "SSL", "TLS", "STARTTLS", "NONE" });
            this.cmbSecurityMode.Font = normalFont;

            // Ajout des champs Email
            this.tabEmail.Controls.Add(this.lblSmtpServer);
            this.tabEmail.Controls.Add(this.txtSmtpServer);
            this.tabEmail.Controls.Add(this.lblSmtpPort);
            this.tabEmail.Controls.Add(this.txtSmtpPort);
            this.tabEmail.Controls.Add(this.lblEmailFrom);
            this.tabEmail.Controls.Add(this.txtEmailFrom);
            this.tabEmail.Controls.Add(this.lblEmailPassword);
            this.tabEmail.Controls.Add(this.txtEmailPassword);
            this.tabEmail.Controls.Add(this.btnTogglePassword);
            this.tabEmail.Controls.Add(this.lblEmailTo);
            this.tabEmail.Controls.Add(this.txtEmailTo);
            this.tabEmail.Controls.Add(this.lblSecurityMode);
            this.tabEmail.Controls.Add(this.cmbSecurityMode);

            // Boutons Email
            this.btnSaveEmail.Location = new System.Drawing.Point(100, 340);
            this.btnSaveEmail.Size = new System.Drawing.Size(220, 35);
            this.btnSaveEmail.Text = LanguageManager.Get("Email.SaveConfig") ?? "Enregistrer configuration";
            this.btnSaveEmail.Font = normalFont;
            this.btnSaveEmail.Click += new System.EventHandler(this.BtnSaveEmail_Click);

            this.btnTestEmail.Location = new System.Drawing.Point(330, 340);
            this.btnTestEmail.Size = new System.Drawing.Size(200, 35);
            this.btnTestEmail.Text = LanguageManager.Get("Email.Test") ?? "Tester Email";
            this.btnTestEmail.Font = normalFont;
            this.btnTestEmail.Click += new System.EventHandler(this.BtnTestEmail_Click);

            this.tabEmail.Controls.Add(this.btnSaveEmail);
            this.tabEmail.Controls.Add(this.btnTestEmail);

            // ============================================================
            // MEDIA MONITOR — CONTENU
            // ============================================================

            this.grpMediaInfo = new System.Windows.Forms.GroupBox();
            this.pnlMediaInfo = new System.Windows.Forms.Panel();
            this.picMediaInfo = new System.Windows.Forms.PictureBox();
            this.lblMediaInfo = new System.Windows.Forms.Label();

            this.grpMediaActions = new System.Windows.Forms.GroupBox();
            this.toggleMediaService = new System.Windows.Forms.Panel();
            this.toggleKnob = new System.Windows.Forms.Panel();
            this.lblMediaStatus = new System.Windows.Forms.Label();
            this.lblNextReport = new System.Windows.Forms.Label();
            this.lblLastReport = new System.Windows.Forms.Label();

            this.btnCreateMediaTask2 = new System.Windows.Forms.Button();
            this.btnDeleteMediaTask2 = new System.Windows.Forms.Button();
            this.btnOpenMediaUI = new System.Windows.Forms.Button();

            // GROUPBOX 1 — INFORMATIONS
            this.grpMediaInfo.Text = LanguageManager.Get("Media.Info.Title") ?? "À propos de MediaMonitor";
            this.grpMediaInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpMediaInfo.Location = new System.Drawing.Point(20, 20);
            this.grpMediaInfo.Size = new System.Drawing.Size(620, 120);

            // Panel info
            this.pnlMediaInfo.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.pnlMediaInfo.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlMediaInfo.Location = new System.Drawing.Point(15, 25);
            this.pnlMediaInfo.Size = new System.Drawing.Size(590, 80);

            // Icône
            this.picMediaInfo.Location = new System.Drawing.Point(10, 10);
            this.picMediaInfo.Size = new System.Drawing.Size(28, 28);
            this.picMediaInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picMediaInfo.Image = SystemIcons.Information.ToBitmap();

            // Texte
            this.lblMediaInfo.AutoSize = false;
            this.lblMediaInfo.Location = new System.Drawing.Point(45, 5);
            this.lblMediaInfo.Size = new System.Drawing.Size(530, 70);
            this.lblMediaInfo.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblMediaInfo.Text =
                LanguageManager.Get("Media.Info.Description") ??
                "MediaMonitor permet de savoir quels médias sont en cours de lecture ou ont été lus.\n" +
                "Le processus peut être automatisé selon la période d’activité de la machine.\n" +
                "Un rapport peut être envoyé automatiquement avant l’arrêt ou manuellement via l’interface.";

            this.pnlMediaInfo.Controls.Add(this.picMediaInfo);
            this.pnlMediaInfo.Controls.Add(this.lblMediaInfo);

            this.grpMediaInfo.Controls.Add(this.pnlMediaInfo);
            this.tabMediaMonitor.Controls.Add(this.grpMediaInfo);

            // GROUPBOX 2 — AUTOMATISATION & ACTIONS (AVEC TOGGLE)

            this.grpMediaActions.Text = LanguageManager.Get("Media.Actions.Title") ?? "Automatisation du rapport";
            this.grpMediaActions.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpMediaActions.Location = new System.Drawing.Point(20, 150);
            this.grpMediaActions.Size = new System.Drawing.Size(620, 160);

            // TOGGLE SWITCH
            this.toggleMediaService.Location = new System.Drawing.Point(20, 34);
            this.toggleMediaService.Size = new System.Drawing.Size(34, 16);
            this.toggleMediaService.BackColor = System.Drawing.Color.LightGray;
            this.toggleMediaService.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.toggleMediaService.Cursor = Cursors.Hand;

            this.toggleMediaService.Paint += (s, e) =>
            {
                var gp = new System.Drawing.Drawing2D.GraphicsPath();
                gp.AddArc(0, 0, 16, 16, 90, 180);
                gp.AddArc(18, 0, 16, 16, 270, 180);
                gp.CloseFigure();
                toggleMediaService.Region = new Region(gp);
            };

            this.toggleKnob.Size = new System.Drawing.Size(12, 12);
            this.toggleKnob.Location = new System.Drawing.Point(2, 2);
            this.toggleKnob.BackColor = System.Drawing.Color.White;
            this.toggleKnob.Cursor = Cursors.Hand;

            this.toggleKnob.Paint += (s, e) =>
            {
                var gp = new System.Drawing.Drawing2D.GraphicsPath();
                gp.AddEllipse(0, 0, toggleKnob.Width - 1, toggleKnob.Height - 1);
                toggleKnob.Region = new Region(gp);
            };

            this.toggleMediaService.Controls.Add(this.toggleKnob);

            this.lblMediaStatus.Text = LanguageManager.Get("Media.Service.Label") ?? "Service MediaMonitor";
            this.lblMediaStatus.Font = normalFont;
            this.lblMediaStatus.Location = new System.Drawing.Point(70, 32);
            this.lblMediaStatus.AutoSize = true;

            // lblNextReport
            this.lblNextReport = new System.Windows.Forms.Label();
            this.lblNextReport.AutoSize = true;
            this.lblNextReport.Font = normalFont;
            this.lblNextReport.Location = new System.Drawing.Point(300, 32);
            this.lblNextReport.Text = "";   // ? VIDE

            // lblLastReport
            this.lblLastReport = new System.Windows.Forms.Label();
            this.lblLastReport.AutoSize = true;
            this.lblLastReport.Font = normalFont;
            this.lblLastReport.Location = new System.Drawing.Point(300, 52);
            this.lblLastReport.Text = "";   // ? VIDE

            this.toggleMediaService.Click += new System.EventHandler(this.toggleMediaService_Click);
            this.toggleKnob.Click += new System.EventHandler(this.toggleMediaService_Click);

            // BOUTONS MEDIA MONITOR

            this.btnCreateMediaTask2.Text = LanguageManager.Get("Media.Task.Create") ?? "Créer tâche planifiée";
            this.btnCreateMediaTask2.Font = normalFont;
            this.btnCreateMediaTask2.Size = new System.Drawing.Size(180, 32);
            this.btnCreateMediaTask2.Location = new System.Drawing.Point(30, 100);
            this.btnCreateMediaTask2.Click += new System.EventHandler(this.BtnCreateMediaTask_Click);

            this.btnDeleteMediaTask2.Text = LanguageManager.Get("Media.Task.Delete") ?? "Supprimer tâche planifiée";
            this.btnDeleteMediaTask2.Font = normalFont;
            this.btnDeleteMediaTask2.Size = new System.Drawing.Size(180, 32);
            this.btnDeleteMediaTask2.Location = new System.Drawing.Point(220, 100);
            this.btnDeleteMediaTask2.Click += new System.EventHandler(this.BtnDeleteMediaTask_Click);

            this.btnOpenMediaUI.Text = LanguageManager.Get("Media.UI.Open") ?? "Ouvrir MediaMonitor";
            this.btnOpenMediaUI.Font = normalFont;
            this.btnOpenMediaUI.Size = new System.Drawing.Size(180, 32);
            this.btnOpenMediaUI.Location = new System.Drawing.Point(410, 100);
            this.btnOpenMediaUI.Click += new System.EventHandler(this.BtnOpenUI_Click);

            this.grpMediaActions.Controls.Add(this.toggleMediaService);
            this.grpMediaActions.Controls.Add(this.lblMediaStatus);
            this.grpMediaActions.Controls.Add(this.lblNextReport);
            this.grpMediaActions.Controls.Add(this.lblLastReport);
            this.grpMediaActions.Controls.Add(this.btnCreateMediaTask2);
            this.grpMediaActions.Controls.Add(this.btnDeleteMediaTask2);
            this.grpMediaActions.Controls.Add(this.btnOpenMediaUI);

            this.tabMediaMonitor.Controls.Add(this.grpMediaActions);

            // Timer MediaMonitor
            this.mediaServiceTimer = new System.Windows.Forms.Timer();
            this.mediaServiceTimer.Interval = 3000;
            this.mediaServiceTimer.Tick += new System.EventHandler(this.MediaServiceTimer_Tick);
            this.mediaServiceTimer.Start();

            // ============================================================
            // WAKE MONITOR — CONTENU
            // ============================================================

            this.lblWakeTitle = new System.Windows.Forms.Label();
            this.pnlWakeInfo = new System.Windows.Forms.Panel();
            this.picWakeInfo = new System.Windows.Forms.PictureBox();
            this.lblWakeDescription = new System.Windows.Forms.Label();

            this.grpWakeOptions = new System.Windows.Forms.GroupBox();
            this.chkPublicIP = new System.Windows.Forms.CheckBox();
            this.chkLocalIP = new System.Windows.Forms.CheckBox();
            this.chkMAC = new System.Windows.Forms.CheckBox();
            this.chkUSB = new System.Windows.Forms.CheckBox();
            this.chkCause = new System.Windows.Forms.CheckBox();
            this.chkDuration = new System.Windows.Forms.CheckBox();

            this.btnSaveWakeConfig = new System.Windows.Forms.Button();
            this.btnRunWake = new System.Windows.Forms.Button();
            this.btnCreateWakeTask = new System.Windows.Forms.Button();
            this.btnDeleteWakeTask = new System.Windows.Forms.Button();
            this.btnManageWolMacs = new System.Windows.Forms.Button();

            // Titre Wake
            this.lblWakeTitle.Text = "À propos de WakeMonitor";
            this.lblWakeTitle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblWakeTitle.AutoSize = true;
            this.lblWakeTitle.Location = new System.Drawing.Point(15, 10);
            this.tabWakeMonitor.Controls.Add(this.lblWakeTitle);

            // PANEL INFO
            this.pnlWakeInfo.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.pnlWakeInfo.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlWakeInfo.Location = new System.Drawing.Point(15, 45);
            this.pnlWakeInfo.Size = new System.Drawing.Size(640, 55);

            this.picWakeInfo.Location = new System.Drawing.Point(10, 10);
            this.picWakeInfo.Size = new System.Drawing.Size(28, 28);
            this.picWakeInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picWakeInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblWakeDescription.AutoSize = false;
            this.lblWakeDescription.Location = new System.Drawing.Point(45, 5);
            this.lblWakeDescription.Size = new System.Drawing.Size(580, 45);
            this.lblWakeDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblWakeDescription.Text =
                LanguageManager.Get("Wake.Description") ??
                "WakeMonitor peut vous remonter des informations qui ont provoqué le réveil de votre machine. " +
                "Ces informations sont envoyées par mail. Vous pouvez créer une tâche planifiée qui réagira " +
                "avec Power-Troubleshooter sur l’ID = 1.";

            this.pnlWakeInfo.Controls.Add(this.picWakeInfo);
            this.pnlWakeInfo.Controls.Add(this.lblWakeDescription);
            this.tabWakeMonitor.Controls.Add(this.pnlWakeInfo);

            // GROUPBOX OPTIONS
            this.grpWakeOptions.Text = LanguageManager.Get("Wake.Options.Title") ?? "Indications à donner dans le mail";
            this.grpWakeOptions.Location = new System.Drawing.Point(15, 110);
            this.grpWakeOptions.Size = new System.Drawing.Size(400, 200);
            this.grpWakeOptions.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);

            this.chkPublicIP.AutoSize = true;
            this.chkPublicIP.Location = new System.Drawing.Point(15, 30);
            this.chkPublicIP.Text = LanguageManager.Get("Wake.Option.PublicIP") ?? "Inclure IP publique";
            this.chkPublicIP.Font = normalFont;

            this.chkLocalIP.AutoSize = true;
            this.chkLocalIP.Location = new System.Drawing.Point(15, 60);
            this.chkLocalIP.Text = LanguageManager.Get("Wake.Option.LocalIP") ?? "Inclure IP locale";
            this.chkLocalIP.Font = normalFont;

            this.chkMAC.AutoSize = true;
            this.chkMAC.Location = new System.Drawing.Point(15, 90);
            this.chkMAC.Text = LanguageManager.Get("Wake.Option.MAC") ?? "Inclure MAC";
            this.chkMAC.Font = normalFont;

            this.chkUSB.AutoSize = true;
            this.chkUSB.Location = new System.Drawing.Point(15, 120);
            this.chkUSB.Text = LanguageManager.Get("Wake.Option.USB") ?? "Inclure USB";
            this.chkUSB.Font = normalFont;

            this.chkCause.AutoSize = true;
            this.chkCause.Location = new System.Drawing.Point(15, 150);
            this.chkCause.Text = LanguageManager.Get("Wake.Option.Cause") ?? "Inclure cause";
            this.chkCause.Font = normalFont;

            this.chkDuration.AutoSize = true;
            this.chkDuration.Location = new System.Drawing.Point(15, 180);
            this.chkDuration.Text = LanguageManager.Get("Wake.Option.Duration") ?? "Inclure durée";
            this.chkDuration.Font = normalFont;

            this.grpWakeOptions.Controls.Add(this.chkPublicIP);
            this.grpWakeOptions.Controls.Add(this.chkLocalIP);
            this.grpWakeOptions.Controls.Add(this.chkMAC);
            this.grpWakeOptions.Controls.Add(this.chkUSB);
            this.grpWakeOptions.Controls.Add(this.chkCause);
            this.grpWakeOptions.Controls.Add(this.chkDuration);

            this.tabWakeMonitor.Controls.Add(this.grpWakeOptions);

            // BOUTONS WAKE
            this.btnCreateWakeTask.Location = new System.Drawing.Point(440, 120);
            this.btnCreateWakeTask.Size = new System.Drawing.Size(200, 35);
            this.btnCreateWakeTask.Text = LanguageManager.Get("Wake.Task.Create") ?? "Créer tâche planifiée";
            this.btnCreateWakeTask.Font = normalFont;
            this.btnCreateWakeTask.Click += new System.EventHandler(this.BtnCreateWakeTask_Click);

            this.btnDeleteWakeTask.Location = new System.Drawing.Point(440, 165);
            this.btnDeleteWakeTask.Size = new System.Drawing.Size(200, 35);
            this.btnDeleteWakeTask.Text = LanguageManager.Get("Wake.Task.Delete") ?? "Supprimer tâche planifiée";
            this.btnDeleteWakeTask.Font = normalFont;
            this.btnDeleteWakeTask.Click += new System.EventHandler(this.BtnDeleteWakeTask_Click);

            this.btnManageWolMacs.Location = new System.Drawing.Point(440, 210);
            this.btnManageWolMacs.Size = new System.Drawing.Size(200, 35);
            this.btnManageWolMacs.Text = "Gérer MAC autorisées";
            this.btnManageWolMacs.Font = normalFont;
            this.btnManageWolMacs.Click += new System.EventHandler(this.BtnManageWolMacs_Click);

            int wakeButtonY = 340;

            this.btnSaveWakeConfig.Size = new System.Drawing.Size(200, 35);
            this.btnSaveWakeConfig.Location = new System.Drawing.Point((672 - 200) / 2 - 110, wakeButtonY);
            this.btnSaveWakeConfig.Text = LanguageManager.Get("Wake.Config.Save") ?? "Enregistrer configuration";
            this.btnSaveWakeConfig.Font = normalFont;
            this.btnSaveWakeConfig.Click += new System.EventHandler(this.BtnSaveWakeConfig_Click);

            this.btnRunWake.Size = new System.Drawing.Size(200, 35);
            this.btnRunWake.Location = new System.Drawing.Point((672 - 200) / 2 + 110, wakeButtonY);
            this.btnRunWake.Text = LanguageManager.Get("Wake.Run") ?? "Envoi d'un mail de test";
            this.btnRunWake.Font = normalFont;
            this.btnRunWake.Click += new System.EventHandler(this.BtnRunWake_Click);

            this.tabWakeMonitor.Controls.Add(this.btnSaveWakeConfig);
            this.tabWakeMonitor.Controls.Add(this.btnRunWake);
            this.tabWakeMonitor.Controls.Add(this.btnCreateWakeTask);
            this.tabWakeMonitor.Controls.Add(this.btnDeleteWakeTask);
            this.tabWakeMonitor.Controls.Add(this.btnManageWolMacs);

            // ============================================================
            // STOP MONITOR — TITRE
            // ============================================================

            this.lblStopTitle = new System.Windows.Forms.Label();
            this.lblStopTitle.AutoSize = true;
            this.lblStopTitle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblStopTitle.Location = new System.Drawing.Point(15, 5);
            this.lblStopTitle.Text = "A propos de StopMonitor";
            this.tabStopMonitor.Controls.Add(this.lblStopTitle);

            // ============================================================
            // STOP MONITOR — CONTENU
            // ============================================================

            this.pnlStopInfo = new System.Windows.Forms.Panel();
            this.picStopInfo = new System.Windows.Forms.PictureBox();
            this.lblStopDescription = new System.Windows.Forms.Label();

            this.pnlStopInfo.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.pnlStopInfo.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlStopInfo.Location = new System.Drawing.Point(15, 30);   // ? descendu proprement sous le titre
            this.pnlStopInfo.Size = new System.Drawing.Size(640, 55);

            this.picStopInfo.Location = new System.Drawing.Point(10, 10);
            this.picStopInfo.Size = new System.Drawing.Size(28, 28);
            this.picStopInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picStopInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblStopDescription.AutoSize = false;
            this.lblStopDescription.Location = new System.Drawing.Point(45, 5);
            this.lblStopDescription.Size = new System.Drawing.Size(580, 45);
            this.lblStopDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblStopDescription.Text =
                LanguageManager.Get("Stop.Description") ??
                "StopMonitor vous permet de savoir pourquoi votre machine a démarré ou redémarré via une tâche planifiée.";

            this.pnlStopInfo.Controls.Add(this.picStopInfo);
            this.pnlStopInfo.Controls.Add(this.lblStopDescription);
            this.tabStopMonitor.Controls.Add(this.pnlStopInfo);

            // ============================================================
            // STOP MONITOR — BOUTONS
            // ============================================================

            this.btnCreateStopTask = new System.Windows.Forms.Button();
            this.btnDeleteStopTask = new System.Windows.Forms.Button();
            this.btnRunStopMonitor = new System.Windows.Forms.Button();

            this.btnCreateStopTask.Text = LanguageManager.Get("Stop.Task.Create") ?? "Créer tâche planifiée";
            this.btnCreateStopTask.Location = new System.Drawing.Point(20, 100);
            this.btnCreateStopTask.Size = new System.Drawing.Size(200, 35);
            this.btnCreateStopTask.Font = normalFont;
            this.btnCreateStopTask.Click += new System.EventHandler(this.BtnCreateStopTask_Click);

            this.btnDeleteStopTask.Text = LanguageManager.Get("Stop.Task.Delete") ?? "Supprimer tâche planifiée";
            this.btnDeleteStopTask.Location = new System.Drawing.Point(20, 145);
            this.btnDeleteStopTask.Size = new System.Drawing.Size(200, 35);
            this.btnDeleteStopTask.Font = normalFont;
            this.btnDeleteStopTask.Click += new System.EventHandler(this.BtnDeleteStopTask_Click);

            this.btnRunStopMonitor.Text = LanguageManager.Get("Stop.Run") ?? "Envoi d'un mail de test";
            this.btnRunStopMonitor.Location = new System.Drawing.Point(20, 190);
            this.btnRunStopMonitor.Size = new System.Drawing.Size(200, 35);
            this.btnRunStopMonitor.Font = normalFont;
            this.btnRunStopMonitor.Click += new System.EventHandler(this.BtnRunStopMonitor_Click);

            this.tabStopMonitor.Controls.Add(this.btnCreateStopTask);
            this.tabStopMonitor.Controls.Add(this.btnDeleteStopTask);
            this.tabStopMonitor.Controls.Add(this.btnRunStopMonitor);

            // ============================================================
            // ON / OFF — CONTENU
            // ============================================================

            // BLOC D’INFORMATION ON/OFF
            this.grpOnOffInfo = new System.Windows.Forms.GroupBox();
            this.lblOnOffInfo = new System.Windows.Forms.Label();
            this.picOnOffInfo = new System.Windows.Forms.PictureBox();

            this.grpOnOffInfo.Text = LanguageManager.Get("OnOff.Info.Title") ?? "Information";
            this.grpOnOffInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpOnOffInfo.Location = new System.Drawing.Point(15, 10);
            this.grpOnOffInfo.Size = new System.Drawing.Size(640, 120);

            // Icône info
            this.picOnOffInfo.Location = new System.Drawing.Point(15, 35);
            this.picOnOffInfo.Size = new System.Drawing.Size(28, 28);
            this.picOnOffInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picOnOffInfo.Image = SystemIcons.Information.ToBitmap();

            // Texte
            this.lblOnOffInfo.AutoSize = false;
            this.lblOnOffInfo.Location = new System.Drawing.Point(50, 30);
            this.lblOnOffInfo.Size = new System.Drawing.Size(580, 90);
            this.lblOnOffInfo.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblOnOffInfo.Text =
                LanguageManager.Get("OnOff.Info.Description") ??
                "Le module On/Off permet de programmer l’arrêt ou la mise en veille de votre machine à une heure précise.\n" +
                "Cette heure est également utilisée par MediaMonitor pour envoyer un rapport par email 10 minutes avant l’arrêt ou la mise en veille.\n" +
                "Si vous ne souhaitez pas envoyer de rapport, vous pouvez désactiver cette fonctionnalité dans l’interface de MediaMonitor.";

            this.grpOnOffInfo.Controls.Add(this.picOnOffInfo);
            this.grpOnOffInfo.Controls.Add(this.lblOnOffInfo);
            this.tabOnOff.Controls.Add(this.grpOnOffInfo);

            // ARRÊT PROGRAMMÉ
            this.grpShutdown = new System.Windows.Forms.GroupBox();
            this.lblShutdownHour = new System.Windows.Forms.Label();
            this.numShutdownHour = new System.Windows.Forms.NumericUpDown();
            this.lblShutdownMinute = new System.Windows.Forms.Label();
            this.numShutdownMinute = new System.Windows.Forms.NumericUpDown();
            this.lblShutdownType = new System.Windows.Forms.Label();
            this.cmbShutdownType = new System.Windows.Forms.ComboBox();
            this.btnCreateShutdownTask = new System.Windows.Forms.Button();
            this.btnDeleteShutdownTask = new System.Windows.Forms.Button();

            this.grpShutdown.Text = LanguageManager.Get("Shutdown.Title") ?? "Arrêt programmé";
            this.grpShutdown.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpShutdown.Location = new System.Drawing.Point(15, 135);
            this.grpShutdown.Size = new System.Drawing.Size(640, 150);

            this.lblShutdownHour.Text = LanguageManager.Get("Shutdown.Hour") ?? "Heure (0–23) :";
            this.lblShutdownHour.Location = new System.Drawing.Point(20, 35);
            this.lblShutdownHour.Font = normalFont;

            this.numShutdownHour.Minimum = 0;
            this.numShutdownHour.Maximum = 23;
            this.numShutdownHour.Location = new System.Drawing.Point(150, 30);
            this.numShutdownHour.Width = 60;
            this.numShutdownHour.Font = normalFont;

            this.lblShutdownMinute.Text = LanguageManager.Get("Shutdown.Minute") ?? "Minute (0–59) :";
            this.lblShutdownMinute.Location = new System.Drawing.Point(230, 35);
            this.lblShutdownMinute.Font = normalFont;

            this.numShutdownMinute.Minimum = 0;
            this.numShutdownMinute.Maximum = 59;
            this.numShutdownMinute.Location = new System.Drawing.Point(350, 30);
            this.numShutdownMinute.Width = 60;
            this.numShutdownMinute.Font = normalFont;

            this.lblShutdownType.Text = LanguageManager.Get("Shutdown.Type") ?? "Choisir le type d'arrêt :";
            this.lblShutdownType.Location = new System.Drawing.Point(20, 65);
            this.lblShutdownType.Size = new System.Drawing.Size(150, 25);
            this.lblShutdownType.Font = normalFont;

            this.cmbShutdownType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbShutdownType.Items.AddRange(new object[] { "Arrêt", "Veille" });
            this.cmbShutdownType.Location = new System.Drawing.Point(180, 65);
            this.cmbShutdownType.Size = new System.Drawing.Size(150, 25);
            this.cmbShutdownType.SelectedIndex = 0;
            this.cmbShutdownType.Font = normalFont;

            // Bouton Sauvegarder configuration On/Off
            this.btnSaveOnOff = new System.Windows.Forms.Button();
            this.btnSaveOnOff.Text = LanguageManager.Get("OnOff.Save") ?? "Sauvegarder";
            this.btnSaveOnOff.Font = normalFont;
            this.btnSaveOnOff.Location = new System.Drawing.Point(10, 100);
            this.btnSaveOnOff.Size = new System.Drawing.Size(200, 35);
            this.btnSaveOnOff.Click += new System.EventHandler(this.BtnSaveOnOff_Click);

            this.grpShutdown.Controls.Add(this.btnSaveOnOff);

            this.btnCreateShutdownTask.Text = LanguageManager.Get("Shutdown.Task.Create") ?? "Créer tâche planifiée";
            this.btnCreateShutdownTask.Location = new System.Drawing.Point(220, 100);
            this.btnCreateShutdownTask.Size = new System.Drawing.Size(200, 35);
            this.btnCreateShutdownTask.Font = normalFont;
            this.btnCreateShutdownTask.Click += new System.EventHandler(this.BtnCreateShutdownTask_Click);

            this.btnDeleteShutdownTask.Text = LanguageManager.Get("Shutdown.Task.Delete") ?? "Supprimer tâche planifiée";
            this.btnDeleteShutdownTask.Location = new System.Drawing.Point(430, 100);
            this.btnDeleteShutdownTask.Size = new System.Drawing.Size(200, 35);
            this.btnDeleteShutdownTask.Font = normalFont;
            this.btnDeleteShutdownTask.Click += new System.EventHandler(this.BtnDeleteShutdownTask_Click);

            this.grpShutdown.Controls.Add(this.lblShutdownHour);
            this.grpShutdown.Controls.Add(this.numShutdownHour);
            this.grpShutdown.Controls.Add(this.lblShutdownMinute);
            this.grpShutdown.Controls.Add(this.numShutdownMinute);
            this.grpShutdown.Controls.Add(this.lblShutdownType);
            this.grpShutdown.Controls.Add(this.cmbShutdownType);
            this.grpShutdown.Controls.Add(this.btnCreateShutdownTask);
            this.grpShutdown.Controls.Add(this.btnDeleteShutdownTask);

            this.tabOnOff.Controls.Add(this.grpShutdown);

            // WOL
            this.grpWOL = new System.Windows.Forms.GroupBox();
            this.lblWOLInfo = new System.Windows.Forms.Label();

            this.grpWOL.Text = LanguageManager.Get("WOL.Title") ?? "Démarrage automatique (Wake On Lan)";
            this.grpWOL.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpWOL.Location = new System.Drawing.Point(15, 290);
            this.grpWOL.Size = new System.Drawing.Size(640, 120);

            this.lblWOLInfo.AutoSize = false;
            this.lblWOLInfo.Location = new System.Drawing.Point(20, 30);
            this.lblWOLInfo.Size = new System.Drawing.Size(600, 135);
            this.lblWOLInfo.Font = normalFont;
            this.lblWOLInfo.Text =
                LanguageManager.Get("WOL.Description") ??
                "Pour démarrer votre machine automatiquement vous devrez utiliser la méthode Wake On Lan.\n\n"
              + "Vous devez activer la fonctionnalité dans le BIOS, mais aussi dans le gestionnaire "
              + "de périphériques de Windows.\n\n"
              + "Si vous ne souhaitez pas utiliser WOL, vous devrez démarrer votre machine manuellement.";

            this.grpWOL.Controls.Add(this.lblWOLInfo);
            this.tabOnOff.Controls.Add(this.grpWOL);

            // ============================================================
            // À PROPOS — CONTENU
            // ============================================================

            // PANEL DÉFILANT
            this.pnlAboutScroll = new System.Windows.Forms.Panel();
            this.pnlAboutScroll.Location = new System.Drawing.Point(10, 10);
            this.pnlAboutScroll.Size = new System.Drawing.Size(620, 400);
            this.pnlAboutScroll.AutoScroll = true;
            this.pnlAboutScroll.BackColor = Color.FromArgb(245, 245, 245);

            // LABEL À L’INTÉRIEUR
            this.lblAbout = new System.Windows.Forms.Label();
            this.lblAbout.AutoSize = true;
            this.lblAbout.MaximumSize = new System.Drawing.Size(600, 0);
            this.lblAbout.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblAbout.Text =
                LanguageManager.Get("About.Text") ??
                "MCEMonitor (Version 1.1)\n" +
                "Outil de supervision et d'automatisation pour Media Server (KODI).\n" +
                "\n" +
                "MIT License\n" +
                "Copyright (c) 2026 Skypichat-kodi\n" +
                "\n" +
                "Permission is hereby granted, free of charge, to any person obtaining a copy\n" +
                "of this software and associated documentation files (the \"Software\"), to deal\n" +
                "in the Software without restriction, including without limitation the rights\n" +
                "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell\n" +
                "copies of the Software, and to permit persons to whom the Software is\n" +
                "furnished to do so, subject to the following conditions:\n" +
                "\n" +
                "The above copyright notice and this permission notice shall be included in all\n" +
                "copies or substantial portions of the Software.\n" +
                "\n" +
                "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR\n" +
                "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,\n" +
                "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE\n" +
                "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER\n" +
                "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,\n" +
                "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.";

            // AJOUTS
            this.pnlAboutScroll.Controls.Add(this.lblAbout);
            this.tabAbout.Controls.Add(this.pnlAboutScroll);

            // ============================================================
            // FINALISATION DU FORMULAIRE
            // ============================================================

            this.Controls.Add(this.tabControl);

            this.tabControl.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        // ============================================================
        // DÉCLARATIONS DES CONTRÔLES (FIN DU FICHIER)
        // ============================================================

        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage tabEmail;
        private System.Windows.Forms.TabPage tabMediaMonitor;
        private System.Windows.Forms.TabPage tabWakeMonitor;
        private System.Windows.Forms.TabPage tabStopMonitor;
        private System.Windows.Forms.TabPage tabOnOff;
        private System.Windows.Forms.TabPage tabAbout;

        private System.Windows.Forms.Panel pnlEmailInfo;
        private System.Windows.Forms.PictureBox picEmailInfo;
        private System.Windows.Forms.Label lblEmailDescription;
        private System.Windows.Forms.Label lblEmailTitle;
        private System.Windows.Forms.Label lblSmtpServer;
        private System.Windows.Forms.Label lblSmtpPort;
        private System.Windows.Forms.Label lblEmailFrom;
        private System.Windows.Forms.Label lblEmailPassword;
        private System.Windows.Forms.Label lblEmailTo;
        private System.Windows.Forms.Label lblSecurityMode;
        private System.Windows.Forms.TextBox txtSmtpServer;
        private System.Windows.Forms.TextBox txtSmtpPort;
        private System.Windows.Forms.TextBox txtEmailFrom;
        private System.Windows.Forms.TextBox txtEmailPassword;
        private System.Windows.Forms.TextBox txtEmailTo;
        private System.Windows.Forms.ComboBox cmbSecurityMode;
        private System.Windows.Forms.Button btnSaveEmail;
        private System.Windows.Forms.Button btnTestEmail;
        private System.Windows.Forms.Button btnTogglePassword;

        private System.Windows.Forms.GroupBox grpMediaInfo;
        private System.Windows.Forms.Panel pnlMediaInfo;
        private System.Windows.Forms.PictureBox picMediaInfo;
        private System.Windows.Forms.Label lblMediaInfo;
        private System.Windows.Forms.Timer logRefreshTimer;

        private System.Windows.Forms.GroupBox grpMediaActions;
        private System.Windows.Forms.Panel toggleMediaService;
        private System.Windows.Forms.Panel toggleKnob;
        private System.Windows.Forms.Label lblMediaStatus;
        private System.Windows.Forms.Label lblNextReport;
        private System.Windows.Forms.Label lblLastReport;
        private System.Windows.Forms.Timer mediaServiceTimer;

        private System.Windows.Forms.Button btnCreateMediaTask2;
        private System.Windows.Forms.Button btnDeleteMediaTask2;
        private System.Windows.Forms.Button btnOpenMediaUI;

        private System.Windows.Forms.Label lblWakeTitle;
        private System.Windows.Forms.Panel pnlWakeInfo;
        private System.Windows.Forms.PictureBox picWakeInfo;
        private System.Windows.Forms.Label lblWakeDescription;
        private System.Windows.Forms.GroupBox grpWakeOptions;
        private System.Windows.Forms.CheckBox chkPublicIP;
        private System.Windows.Forms.CheckBox chkLocalIP;
        private System.Windows.Forms.CheckBox chkMAC;
        private System.Windows.Forms.CheckBox chkUSB;
        private System.Windows.Forms.CheckBox chkCause;
        private System.Windows.Forms.CheckBox chkDuration;
        private System.Windows.Forms.Button btnSaveWakeConfig;
        private System.Windows.Forms.Button btnRunWake;
        private System.Windows.Forms.Button btnCreateWakeTask;
        private System.Windows.Forms.Button btnDeleteWakeTask;
        private System.Windows.Forms.Button btnManageWolMacs;

        private System.Windows.Forms.Panel pnlStopInfo;
        private System.Windows.Forms.PictureBox picStopInfo;
        private System.Windows.Forms.Label lblStopDescription;
        private System.Windows.Forms.Button btnSaveOnOff;
        private System.Windows.Forms.Button btnCreateStopTask;
        private System.Windows.Forms.Button btnDeleteStopTask;
        private System.Windows.Forms.Button btnRunStopMonitor;

        private System.Windows.Forms.GroupBox grpShutdown;
        private System.Windows.Forms.Label lblShutdownHour;
        private System.Windows.Forms.NumericUpDown numShutdownHour;
        private System.Windows.Forms.Label lblShutdownMinute;
        private System.Windows.Forms.NumericUpDown numShutdownMinute;
        private System.Windows.Forms.Label lblShutdownType;
        private System.Windows.Forms.GroupBox grpOnOffInfo;
        private System.Windows.Forms.Label lblOnOffInfo;
        private System.Windows.Forms.PictureBox picOnOffInfo;

        private System.Windows.Forms.ComboBox cmbShutdownType;
        private System.Windows.Forms.Button btnCreateShutdownTask;
        private System.Windows.Forms.Button btnDeleteShutdownTask;
        private System.Windows.Forms.Label lblStopTitle;

        private System.Windows.Forms.GroupBox grpWOL;
        private System.Windows.Forms.Label lblWOLInfo;

        private System.Windows.Forms.Label lblAbout;
        private System.Windows.Forms.Panel pnlAboutScroll;
    }
}

