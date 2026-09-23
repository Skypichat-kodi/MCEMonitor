using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using Krypton.Toolkit;
using Krypton.Navigator;

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
            // ============================================================
            // KRYPTON NAVIGATOR
            // ============================================================
            this.tabControl = new KryptonNavigator();
            this.tabControl.NavigatorMode = NavigatorMode.BarTabGroup;
            this.tabControl.Button.CloseButtonDisplay = ButtonDisplay.Hide;
            this.tabControl.Button.ButtonDisplayLogic = ButtonDisplayLogic.None;

            this.tabEmail = new KryptonPage();
            this.tabMediaMonitor = new KryptonPage();
            this.tabRomMonitor = new KryptonPage();
            this.tabWakeMonitor = new KryptonPage();
            this.tabStopMonitor = new KryptonPage();
            this.tabOnOff = new KryptonPage();
            this.tabAbout = new KryptonPage();

            this.logRefreshTimer = new System.Windows.Forms.Timer();
            this.logRefreshTimer.Interval = 2000;
            this.logRefreshTimer.Tick += new System.EventHandler(this.LogRefreshTimer_Tick);
            this.logRefreshTimer.Start();

            this.tabControl.SuspendLayout();
            this.SuspendLayout();

            var normalFont = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular);

            this.ClientSize = new System.Drawing.Size(700, 540);
            this.Text = LanguageManager.Get("MCEMonitor") ?? "MCEMonitor";

            // ============================================================
            // TAB CONTROL
            // ============================================================
            this.tabControl.Location = new System.Drawing.Point(10, 10);
            this.tabControl.Size = new System.Drawing.Size(680, 490);

            this.tabControl.Pages.Add(this.tabEmail);
            this.tabControl.Pages.Add(this.tabOnOff);
            this.tabControl.Pages.Add(this.tabMediaMonitor);
            this.tabControl.Pages.Add(this.tabRomMonitor);
            this.tabControl.Pages.Add(this.tabWakeMonitor);
            this.tabControl.Pages.Add(this.tabStopMonitor);
            this.tabControl.Pages.Add(this.tabAbout);

            this.tabEmail.Text = LanguageManager.Get("Email") ?? "Email";
            this.tabEmail.Name = "tabEmail";
            this.tabMediaMonitor.Text = LanguageManager.Get("Media Monitor") ?? "Media Monitor";
            this.tabMediaMonitor.Name = "tabMediaMonitor";
            this.tabRomMonitor.Text = LanguageManager.Get("Rom Monitor") ?? "Rom Monitor";
            this.tabRomMonitor.Name = "tabRomMonitor";
            this.tabWakeMonitor.Text = LanguageManager.Get("Wake Monitor") ?? "Wake Monitor";
            this.tabWakeMonitor.Name = "tabWakeMonitor";
            this.tabStopMonitor.Text = LanguageManager.Get("Stop Monitor") ?? "Stop Monitor";
            this.tabStopMonitor.Name = "tabStopMonitor";
            this.tabOnOff.Text = LanguageManager.Get("On/Off") ?? "On/Off";
            this.tabOnOff.Name = "tabOnOff";
            this.tabAbout.Text = LanguageManager.Get("À propos") ?? "À propos";
            this.tabAbout.Name = "tabAbout";

            // ============================================================
            // EMAIL — PANEL INFO
            // ============================================================
            this.pnlEmailInfo = new KryptonPanel();
            this.picEmailInfo = new KryptonPictureBox();
            this.lblEmailDescription = new KryptonLabel();
            this.lblEmailTitle = new KryptonLabel();
            this.lblSmtpStatusTitle = new KryptonLabel();
            this.pnlSmtpStatusDot = new System.Windows.Forms.Panel();
            this.toolTipSmtp = new System.Windows.Forms.ToolTip();

            this.lblSmtpServer = new KryptonLabel();
            this.lblSmtpPort = new KryptonLabel();
            this.lblEmailFrom = new KryptonLabel();
            this.lblEmailPassword = new KryptonLabel();
            this.lblEmailTo = new KryptonLabel();
            this.lblSecurityMode = new KryptonLabel();

            this.txtSmtpServer = new KryptonTextBox();
            this.txtSmtpPort = new KryptonTextBox();
            this.txtEmailFrom = new KryptonTextBox();
            this.txtEmailPassword = new KryptonTextBox();
            this.txtEmailTo = new KryptonTextBox();

            this.cmbSecurityMode = new KryptonComboBox();

            this.btnSaveEmail = new KryptonButton();
            this.btnTestEmail = new KryptonButton();
            this.btnTogglePassword = new KryptonButton();

            this.grpEmailInfo = new KryptonGroupBox();
            this.grpEmailInfo.Text = LanguageManager.Get("À propos de Email") ?? "À propos de Email";
            this.grpEmailInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpEmailInfo.Location = new System.Drawing.Point(20, 20);
            this.grpEmailInfo.Size = new System.Drawing.Size(640, 130);

            this.pnlEmailInfo.Location = new System.Drawing.Point(15, 8);
            this.pnlEmailInfo.Size = new System.Drawing.Size(610, 80);
            this.pnlEmailInfo.StateCommon.Color1 = System.Drawing.Color.FromArgb(240, 240, 240);

            this.picEmailInfo.Location = new System.Drawing.Point(10, 10);
            this.picEmailInfo.Size = new System.Drawing.Size(28, 28);
            this.picEmailInfo.SizeMode = PictureBoxSizeMode.StretchImage;
            this.picEmailInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblEmailDescription.AutoSize = false;
            this.lblEmailDescription.Location = new System.Drawing.Point(50, 15);
            this.lblEmailDescription.Size = new System.Drawing.Size(550, 50);
            this.lblEmailDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblEmailDescription.StateCommon.ShortText.Color1 = System.Drawing.Color.FromArgb(60, 60, 60);
            this.lblEmailDescription.Text =
                LanguageManager.Get("Vous devez entrer vos informations d'envoi et un destinataire afin que les autres fonctionnalités du logiciel puissent envoyer des rapports automatiquement.") ??
                "Vous devez entrer vos informations d'envoi et un destinataire afin que les autres fonctionnalités du logiciel puissent envoyer des rapports automatiquement.";

            this.pnlEmailInfo.Controls.Add(this.picEmailInfo);
            this.pnlEmailInfo.Controls.Add(this.lblEmailDescription);
            this.grpEmailInfo.Panel.Controls.Add(this.pnlEmailInfo);
            this.tabEmail.Controls.Add(this.grpEmailInfo);

            this.lblEmailTitle.AutoSize = true;
            this.lblEmailTitle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblEmailTitle.Location = new System.Drawing.Point(20, 165);
            this.lblEmailTitle.Text = LanguageManager.Get("Configuration Email") ?? "Configuration Email";
            this.tabEmail.Controls.Add(this.lblEmailTitle);

            this.lblSmtpStatusTitle.Text = LanguageManager.Get("Statut du serveur SMTP :") ?? "Statut du serveur SMTP :";
            this.lblSmtpStatusTitle.Location = new System.Drawing.Point(40, 365);
            this.lblSmtpStatusTitle.Size = new System.Drawing.Size(150, 20);
            this.lblSmtpStatusTitle.Font = normalFont;

            this.pnlSmtpStatusDot.BackColor = System.Drawing.Color.FromArgb(160, 160, 160);
            this.pnlSmtpStatusDot.Location = new System.Drawing.Point(202, 366);
            this.pnlSmtpStatusDot.Size = new System.Drawing.Size(18, 18);
            this.pnlSmtpStatusDot.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

            this.tabEmail.Controls.Add(this.lblSmtpStatusTitle);
            this.tabEmail.Controls.Add(this.pnlSmtpStatusDot);

            int labelX = 40;
            int labelWidth = 150;
            int fieldX = 200;
            int fieldWidth = 340;
            int y = 190;
            int step = 28;

            this.lblSmtpServer.Text = LanguageManager.Get("Serveur SMTP :") ?? "Serveur SMTP :";
            this.lblSmtpServer.Location = new System.Drawing.Point(labelX, y);
            this.lblSmtpServer.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblSmtpServer.Font = normalFont;

            this.txtSmtpServer.Location = new System.Drawing.Point(fieldX, y);
            this.txtSmtpServer.Size = new System.Drawing.Size(fieldWidth, 20);
            this.txtSmtpServer.Font = normalFont;
            y += step;

            this.lblSmtpPort.Text = LanguageManager.Get("Port :") ?? "Port :";
            this.lblSmtpPort.Location = new System.Drawing.Point(labelX, y);
            this.lblSmtpPort.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblSmtpPort.Font = normalFont;

            this.txtSmtpPort.Location = new System.Drawing.Point(fieldX, y);
            this.txtSmtpPort.Size = new System.Drawing.Size(80, 20);
            this.txtSmtpPort.Font = normalFont;
            y += step;

            this.lblEmailFrom.Text = LanguageManager.Get("Adresse expéditeur :") ?? "Adresse expéditeur :";
            this.lblEmailFrom.Location = new System.Drawing.Point(labelX, y);
            this.lblEmailFrom.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblEmailFrom.Font = normalFont;

            this.txtEmailFrom.Location = new System.Drawing.Point(fieldX, y);
            this.txtEmailFrom.Size = new System.Drawing.Size(fieldWidth, 20);
            this.txtEmailFrom.Font = normalFont;
            y += step;

            this.lblEmailPassword.Text = LanguageManager.Get("Mot de passe :") ?? "Mot de passe :";
            this.lblEmailPassword.Location = new System.Drawing.Point(labelX, y);
            this.lblEmailPassword.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblEmailPassword.Font = normalFont;

            this.txtEmailPassword.Location = new System.Drawing.Point(fieldX, y);
            this.txtEmailPassword.Size = new System.Drawing.Size(fieldWidth - 90, 20);
            this.txtEmailPassword.PasswordChar = '*';
            this.txtEmailPassword.Font = normalFont;

            this.btnTogglePassword.Location = new System.Drawing.Point(fieldX + fieldWidth - 80, y - 1);
            this.btnTogglePassword.Size = new System.Drawing.Size(80, 22);
            this.btnTogglePassword.Text = LanguageManager.Get("Afficher") ?? "Afficher";
            this.btnTogglePassword.Font = normalFont;
            this.btnTogglePassword.Click += new System.EventHandler(this.BtnTogglePassword_Click);
            y += step;

            this.lblEmailTo.Text = LanguageManager.Get("Destinataire :") ?? "Destinataire :";
            this.lblEmailTo.Location = new System.Drawing.Point(labelX, y);
            this.lblEmailTo.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblEmailTo.Font = normalFont;

            this.txtEmailTo.Location = new System.Drawing.Point(fieldX, y);
            this.txtEmailTo.Size = new System.Drawing.Size(fieldWidth, 20);
            this.txtEmailTo.Font = normalFont;
            y += step;

            this.lblSecurityMode.Text = LanguageManager.Get("Sécurité :") ?? "Sécurité :";
            this.lblSecurityMode.Location = new System.Drawing.Point(labelX, y);
            this.lblSecurityMode.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblSecurityMode.Font = normalFont;

            this.cmbSecurityMode.Location = new System.Drawing.Point(fieldX, y);
            this.cmbSecurityMode.Size = new System.Drawing.Size(150, 20);
            this.cmbSecurityMode.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbSecurityMode.Items.AddRange(new object[] { "SSL", "TLS", "STARTTLS", "NONE" });
            this.cmbSecurityMode.Font = normalFont;

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

            this.btnSaveEmail.Location = new System.Drawing.Point(120, 410);
            this.btnSaveEmail.Size = new System.Drawing.Size(220, 35);
            this.btnSaveEmail.Text = LanguageManager.Get("Enregistrer configuration") ?? "Enregistrer configuration";
            this.btnSaveEmail.Font = normalFont;
            this.btnSaveEmail.Click += new System.EventHandler(this.BtnSaveEmail_Click);

            this.btnTestEmail.Location = new System.Drawing.Point(360, 410);
            this.btnTestEmail.Size = new System.Drawing.Size(200, 35);
            this.btnTestEmail.Text = LanguageManager.Get("Tester Email") ?? "Tester Email";
            this.btnTestEmail.Font = normalFont;
            this.btnTestEmail.Click += new System.EventHandler(this.BtnTestEmail_Click);

            this.tabEmail.Controls.Add(this.btnSaveEmail);
            this.tabEmail.Controls.Add(this.btnTestEmail);

            // ============================================================
            // MEDIA MONITOR
            // ============================================================
            this.grpMediaInfo = new KryptonGroupBox();
            this.pnlMediaInfo = new KryptonPanel();
            this.picMediaInfo = new KryptonPictureBox();
            this.lblMediaInfo = new KryptonLabel();

            this.grpMediaActions = new KryptonGroupBox();
            this.toggleMediaService = new KryptonCheckButton();
            this.lblMediaStatus = new KryptonLabel();
            this.lblNextReport = new KryptonLabel();
            this.lblLastReport = new KryptonLabel();

            this.btnCreateMediaTask2 = new KryptonButton();
            this.btnDeleteMediaTask2 = new KryptonButton();
            this.btnOpenMediaUI = new KryptonButton();

            this.grpMediaInfo.Text = LanguageManager.Get("À propos de MediaMonitor") ?? "À propos de MediaMonitor";
            this.grpMediaInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpMediaInfo.Location = new System.Drawing.Point(20, 20);
            this.grpMediaInfo.Size = new System.Drawing.Size(640, 130);

            this.pnlMediaInfo.Location = new System.Drawing.Point(15, 8);
            this.pnlMediaInfo.Size = new System.Drawing.Size(610, 80);
            this.pnlMediaInfo.StateCommon.Color1 = System.Drawing.Color.FromArgb(240, 240, 240);

            this.picMediaInfo.Location = new System.Drawing.Point(10, 10);
            this.picMediaInfo.Size = new System.Drawing.Size(28, 28);
            this.picMediaInfo.SizeMode = PictureBoxSizeMode.StretchImage;
            this.picMediaInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblMediaInfo.AutoSize = false;
            this.lblMediaInfo.Location = new System.Drawing.Point(50, 15);
            this.lblMediaInfo.Size = new System.Drawing.Size(550, 50);
            this.lblMediaInfo.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblMediaInfo.StateCommon.ShortText.Color1 = System.Drawing.Color.FromArgb(60, 60, 60);
            this.lblMediaInfo.Text =
                LanguageManager.Get("Media.Info.Description") ??
                "MediaMonitor permet de savoir quels médias sont en cours de lecture ou ont été lus.\n" +
                "Le processus peut être automatisé selon la période d'activité de la machine.\n" +
                "Un rapport peut être envoyé automatiquement avant l'arrêt ou manuellement via l'interface.";

            this.pnlMediaInfo.Controls.Add(this.picMediaInfo);
            this.pnlMediaInfo.Controls.Add(this.lblMediaInfo);
            this.grpMediaInfo.Panel.Controls.Add(this.pnlMediaInfo);
            this.tabMediaMonitor.Controls.Add(this.grpMediaInfo);

            this.grpMediaActions.Text = LanguageManager.Get("Automatisation du rapport") ?? "Automatisation du rapport";
            this.grpMediaActions.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpMediaActions.Location = new System.Drawing.Point(20, 160);
            this.grpMediaActions.Size = new System.Drawing.Size(640, 160);

            this.toggleMediaService.Text = "ON / OFF";
            this.toggleMediaService.AutoSize = false;
            this.toggleMediaService.Location = new System.Drawing.Point(20, 5);
            this.toggleMediaService.Size = new System.Drawing.Size(90, 28);
            this.toggleMediaService.CheckedChanged += new System.EventHandler(this.toggleMediaService_Click);

            this.lblMediaStatus.Text = LanguageManager.Get("Service MediaMonitor") ?? "Service MediaMonitor";
            this.lblMediaStatus.Font = normalFont;
            this.lblMediaStatus.Location = new System.Drawing.Point(120, 10);
            this.lblMediaStatus.AutoSize = true;

            this.lblNextReport.AutoSize = true;
            this.lblNextReport.Font = normalFont;
            this.lblNextReport.Location = new System.Drawing.Point(330, 8);
            this.lblNextReport.Text = "";

            this.lblLastReport.AutoSize = true;
            this.lblLastReport.Font = normalFont;
            this.lblLastReport.Location = new System.Drawing.Point(330, 28);
            this.lblLastReport.Text = "";

            this.btnCreateMediaTask2.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateMediaTask2.Font = normalFont;
            this.btnCreateMediaTask2.Size = new System.Drawing.Size(190, 32);
            this.btnCreateMediaTask2.Location = new System.Drawing.Point(30, 75);
            this.btnCreateMediaTask2.Click += new System.EventHandler(this.BtnCreateMediaTask_Click);

            this.btnDeleteMediaTask2.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteMediaTask2.Font = normalFont;
            this.btnDeleteMediaTask2.Size = new System.Drawing.Size(190, 32);
            this.btnDeleteMediaTask2.Location = new System.Drawing.Point(225, 75);
            this.btnDeleteMediaTask2.Click += new System.EventHandler(this.BtnDeleteMediaTask_Click);

            this.btnOpenMediaUI.Text = LanguageManager.Get("Ouvrir MediaMonitor") ?? "Ouvrir MediaMonitor";
            this.btnOpenMediaUI.Font = normalFont;
            this.btnOpenMediaUI.Size = new System.Drawing.Size(190, 32);
            this.btnOpenMediaUI.Location = new System.Drawing.Point(420, 75);
            this.btnOpenMediaUI.Click += new System.EventHandler(this.BtnOpenUI_Click);

            this.grpMediaActions.Panel.Controls.Add(this.toggleMediaService);
            this.grpMediaActions.Panel.Controls.Add(this.lblMediaStatus);
            this.grpMediaActions.Panel.Controls.Add(this.lblNextReport);
            this.grpMediaActions.Panel.Controls.Add(this.lblLastReport);
            this.grpMediaActions.Panel.Controls.Add(this.btnCreateMediaTask2);
            this.grpMediaActions.Panel.Controls.Add(this.btnDeleteMediaTask2);
            this.grpMediaActions.Panel.Controls.Add(this.btnOpenMediaUI);
            this.tabMediaMonitor.Controls.Add(this.grpMediaActions);

            this.mediaServiceTimer = new System.Windows.Forms.Timer();
            this.mediaServiceTimer.Interval = 3000;
            this.mediaServiceTimer.Tick += new System.EventHandler(this.MediaServiceTimer_Tick);
            this.mediaServiceTimer.Start();

            // ============================================================
            // ROM MONITOR
            // ============================================================
            this.grpRomInfo = new KryptonGroupBox();
            this.pnlRomInfo = new KryptonPanel();
            this.picRomInfo = new KryptonPictureBox();
            this.lblRomDescription = new KryptonLabel();
            this.grpRomActions = new KryptonGroupBox();
            this.toggleRomService = new KryptonCheckButton();
            this.lblRomStatus = new KryptonLabel();
            this.grpRomSettings = new KryptonGroupBox();
            this.lblRomInterval = new KryptonLabel();
            this.numRomInterval = new KryptonNumericUpDown();
            this.lblRomWarnPct = new KryptonLabel();
            this.numRomWarnPct = new KryptonNumericUpDown();
            this.lblRomCritPct = new KryptonLabel();
            this.numRomCritPct = new KryptonNumericUpDown();
            this.lblRomWarnGo = new KryptonLabel();
            this.numRomWarnGo = new KryptonNumericUpDown();
            this.lblRomCritGo = new KryptonLabel();
            this.numRomCritGo = new KryptonNumericUpDown();
            this.lblRomCooldown = new KryptonLabel();
            this.numRomCooldown = new KryptonNumericUpDown();
            this.chkRomSmartAlert = new KryptonCheckBox();
            this.lblRomHint = new KryptonLabel();
            this.btnCreateRomTask = new KryptonButton();
            this.btnDeleteRomTask = new KryptonButton();
            this.btnOpenRomUI = new KryptonButton();
            this.btnSaveRomConfig = new KryptonButton();
            this.romMonitorTimer = new System.Windows.Forms.Timer();

            this.grpRomInfo.Text = LanguageManager.Get("À propos de RomMonitor") ?? "À propos de RomMonitor";
            this.grpRomInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpRomInfo.Location = new System.Drawing.Point(20, 20);
            this.grpRomInfo.Size = new System.Drawing.Size(640, 130);

            this.pnlRomInfo.Location = new System.Drawing.Point(15, 8);
            this.pnlRomInfo.Size = new System.Drawing.Size(610, 80);
            this.pnlRomInfo.StateCommon.Color1 = System.Drawing.Color.FromArgb(240, 240, 240);

            this.picRomInfo.Location = new System.Drawing.Point(10, 10);
            this.picRomInfo.Size = new System.Drawing.Size(28, 28);
            this.picRomInfo.SizeMode = PictureBoxSizeMode.StretchImage;
            this.picRomInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblRomDescription.AutoSize = false;
            this.lblRomDescription.Location = new System.Drawing.Point(50, 15);
            this.lblRomDescription.Size = new System.Drawing.Size(550, 50);
            this.lblRomDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblRomDescription.StateCommon.ShortText.Color1 = System.Drawing.Color.FromArgb(60, 60, 60);
            this.lblRomDescription.Text =
                LanguageManager.Get("Rom.Description") ??
                "RomMonitor surveille l'espace disque et la santé SMART de vos disques.\n" +
                "En cas de défaillance SMART critique, un email d'alerte est envoyé.\n" +
                "Un log est tenu à jour pour chaque problème détecté.";

            this.pnlRomInfo.Controls.Add(this.picRomInfo);
            this.pnlRomInfo.Controls.Add(this.lblRomDescription);
            this.grpRomInfo.Panel.Controls.Add(this.pnlRomInfo);
            this.tabRomMonitor.Controls.Add(this.grpRomInfo);

            this.grpRomActions.Text = LanguageManager.Get("Automatisation RomMonitor") ?? "Automatisation RomMonitor";
            this.grpRomActions.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpRomActions.Location = new System.Drawing.Point(20, 160);
            this.grpRomActions.Size = new System.Drawing.Size(640, 100);

            this.toggleRomService.Text = "ON / OFF";
            this.toggleRomService.AutoSize = false;
            this.toggleRomService.Location = new System.Drawing.Point(20, 5);
            this.toggleRomService.Size = new System.Drawing.Size(90, 28);
            this.toggleRomService.CheckedChanged += new System.EventHandler(this.toggleRomService_Click);

            this.lblRomStatus.Text = LanguageManager.Get("Service RomMonitor") ?? "Service RomMonitor";
            this.lblRomStatus.Font = normalFont;
            this.lblRomStatus.Location = new System.Drawing.Point(120, 10);
            this.lblRomStatus.AutoSize = true;

            this.btnCreateRomTask.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateRomTask.Font = normalFont;
            this.btnCreateRomTask.Size = new System.Drawing.Size(190, 32);
            this.btnCreateRomTask.Location = new System.Drawing.Point(30, 45);
            this.btnCreateRomTask.Click += new System.EventHandler(this.BtnCreateRomTask_Click);

            this.btnDeleteRomTask.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteRomTask.Font = normalFont;
            this.btnDeleteRomTask.Size = new System.Drawing.Size(190, 32);
            this.btnDeleteRomTask.Location = new System.Drawing.Point(225, 45);
            this.btnDeleteRomTask.Click += new System.EventHandler(this.BtnDeleteRomTask_Click);

            this.btnOpenRomUI.Text = LanguageManager.Get("Ouvrir RomMonitor") ?? "Ouvrir RomMonitor";
            this.btnOpenRomUI.Font = normalFont;
            this.btnOpenRomUI.Size = new System.Drawing.Size(190, 32);
            this.btnOpenRomUI.Location = new System.Drawing.Point(420, 45);
            this.btnOpenRomUI.Click += new System.EventHandler(this.BtnOpenRomUI_Click);

            this.grpRomActions.Panel.Controls.Add(this.toggleRomService);
            this.grpRomActions.Panel.Controls.Add(this.lblRomStatus);
            this.grpRomActions.Panel.Controls.Add(this.btnCreateRomTask);
            this.grpRomActions.Panel.Controls.Add(this.btnDeleteRomTask);
            this.grpRomActions.Panel.Controls.Add(this.btnOpenRomUI);
            this.tabRomMonitor.Controls.Add(this.grpRomActions);

            this.grpRomSettings.Text = LanguageManager.Get("Réglages") ?? "Réglages";
            this.grpRomSettings.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpRomSettings.Location = new System.Drawing.Point(20, 270);
            this.grpRomSettings.Size = new System.Drawing.Size(640, 190);

            this.lblRomInterval.Text = LanguageManager.Get("Fréquence de contrôle (min) :") ?? "Fréquence de contrôle (min) :";
            this.lblRomInterval.Location = new System.Drawing.Point(20, 10);
            this.lblRomInterval.Size = new System.Drawing.Size(200, 20);
            this.lblRomInterval.Font = normalFont;

            this.numRomInterval.Location = new System.Drawing.Point(230, 10);
            this.numRomInterval.Size = new System.Drawing.Size(80, 20);
            this.numRomInterval.Minimum = 15;
            this.numRomInterval.Maximum = 1440;
            this.numRomInterval.Font = normalFont;

            this.lblRomWarnPct.Text = LanguageManager.Get("Seuil warning (% libre) :") ?? "Seuil de danger (% libre) :";
            this.lblRomWarnPct.Location = new System.Drawing.Point(20, 38);
            this.lblRomWarnPct.Size = new System.Drawing.Size(200, 20);
            this.lblRomWarnPct.Font = normalFont;

            this.numRomWarnPct.Location = new System.Drawing.Point(230, 38);
            this.numRomWarnPct.Size = new System.Drawing.Size(80, 20);
            this.numRomWarnPct.Minimum = 5;
            this.numRomWarnPct.Maximum = 100;
            this.numRomWarnPct.Font = normalFont;

            this.lblRomCritPct.Text = LanguageManager.Get("Seuil critique (% libre) :") ?? "Seuil critique (% libre) :";
            this.lblRomCritPct.Location = new System.Drawing.Point(20, 66);
            this.lblRomCritPct.Size = new System.Drawing.Size(200, 20);
            this.lblRomCritPct.Font = normalFont;

            this.numRomCritPct.Location = new System.Drawing.Point(230, 66);
            this.numRomCritPct.Size = new System.Drawing.Size(80, 20);
            this.numRomCritPct.Minimum = 1;
            this.numRomCritPct.Maximum = 100;
            this.numRomCritPct.Font = normalFont;

            this.lblRomCooldown.Text = LanguageManager.Get("Intervale d'envoi des alertes (h) :") ?? "Intervale d'envoi des alertes (h) :";
            this.lblRomCooldown.Location = new System.Drawing.Point(340, 10);
            this.lblRomCooldown.Size = new System.Drawing.Size(180, 20);
            this.lblRomCooldown.Font = normalFont;

            this.numRomCooldown.Location = new System.Drawing.Point(540, 10);
            this.numRomCooldown.Size = new System.Drawing.Size(70, 20);
            this.numRomCooldown.Minimum = 1;
            this.numRomCooldown.Maximum = 168;
            this.numRomCooldown.Font = normalFont;

            this.lblRomWarnGo.Text = "Seuil warning (Go) :";
            this.lblRomWarnGo.Location = new System.Drawing.Point(20, 94);
            this.lblRomWarnGo.Size = new System.Drawing.Size(200, 20);
            this.lblRomWarnGo.Font = normalFont;
            this.lblRomWarnGo.Visible = false;

            this.numRomWarnGo.Location = new System.Drawing.Point(230, 94);
            this.numRomWarnGo.Size = new System.Drawing.Size(80, 20);
            this.numRomWarnGo.Minimum = 10;
            this.numRomWarnGo.Maximum = 10;
            this.numRomWarnGo.Value = 10;
            this.numRomWarnGo.Font = normalFont;
            this.numRomWarnGo.Visible = false;

            this.lblRomCritGo.Text = "Seuil critique (Go) :";
            this.lblRomCritGo.Location = new System.Drawing.Point(340, 38);
            this.lblRomCritGo.Size = new System.Drawing.Size(180, 20);
            this.lblRomCritGo.Font = normalFont;
            this.lblRomCritGo.Visible = false;

            this.numRomCritGo.Location = new System.Drawing.Point(540, 38);
            this.numRomCritGo.Size = new System.Drawing.Size(70, 20);
            this.numRomCritGo.Minimum = 5;
            this.numRomCritGo.Maximum = 5;
            this.numRomCritGo.Value = 5;
            this.numRomCritGo.Font = normalFont;
            this.numRomCritGo.Visible = false;

            this.chkRomSmartAlert.Text = LanguageManager.Get("Alerte email, SMART et disque plein") ?? "Alerte email, SMART et disque plein";
            this.chkRomSmartAlert.Location = new System.Drawing.Point(20, 100);
            this.chkRomSmartAlert.Size = new System.Drawing.Size(400, 20);
            this.chkRomSmartAlert.Font = normalFont;

            this.lblRomHint.Text = "Les seuils en Go sont fixés à 10 Go (warning) et 5 Go (critique) pour les petits disques.";
            this.lblRomHint.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Italic);
            this.lblRomHint.Location = new System.Drawing.Point(20, 125);
            this.lblRomHint.Size = new System.Drawing.Size(600, 20);

            this.grpRomSettings.Panel.Controls.Add(this.lblRomInterval);
            this.grpRomSettings.Panel.Controls.Add(this.numRomInterval);
            this.grpRomSettings.Panel.Controls.Add(this.lblRomWarnPct);
            this.grpRomSettings.Panel.Controls.Add(this.numRomWarnPct);
            this.grpRomSettings.Panel.Controls.Add(this.lblRomCritPct);
            this.grpRomSettings.Panel.Controls.Add(this.numRomCritPct);
            this.grpRomSettings.Panel.Controls.Add(this.lblRomCooldown);
            this.grpRomSettings.Panel.Controls.Add(this.numRomCooldown);
            this.grpRomSettings.Panel.Controls.Add(this.chkRomSmartAlert);
            this.grpRomSettings.Panel.Controls.Add(this.lblRomHint);
            this.tabRomMonitor.Controls.Add(this.grpRomSettings);

            this.btnSaveRomConfig.Text = LanguageManager.Get("Enregistrer les réglages") ?? "Enregistrer les réglages";
            this.btnSaveRomConfig.Font = normalFont;
            this.btnSaveRomConfig.Size = new System.Drawing.Size(200, 35);
            this.btnSaveRomConfig.Location = new System.Drawing.Point(420, 45);
            this.btnSaveRomConfig.Click += new System.EventHandler(this.BtnSaveRomConfig_Click);
            this.grpRomSettings.Panel.Controls.Add(this.btnSaveRomConfig);

            this.romMonitorTimer.Interval = 3000;
            this.romMonitorTimer.Tick += new System.EventHandler(this.RomMonitorTimer_Tick);
            this.romMonitorTimer.Start();

            // ============================================================
            // WAKE MONITOR
            // ============================================================
            this.grpWakeInfo = new KryptonGroupBox();
            this.pnlWakeInfo = new KryptonPanel();
            this.picWakeInfo = new KryptonPictureBox();
            this.lblWakeDescription = new KryptonLabel();

            this.grpWakeOptions = new KryptonGroupBox();
            this.chkPublicIP = new KryptonCheckBox();
            this.chkLocalIP = new KryptonCheckBox();
            this.chkMAC = new KryptonCheckBox();
            this.chkUSB = new KryptonCheckBox();
            this.chkCause = new KryptonCheckBox();
            this.chkDuration = new KryptonCheckBox();

            this.btnSaveWakeConfig = new KryptonButton();
            this.btnRunWake = new KryptonButton();
            this.btnCreateWakeTask = new KryptonButton();
            this.btnDeleteWakeTask = new KryptonButton();
            this.btnManageWolMacs = new KryptonButton();

            this.grpWakeInfo.Text = LanguageManager.Get("À propos de WakeMonitor") ?? "À propos de WakeMonitor";
            this.grpWakeInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpWakeInfo.Location = new System.Drawing.Point(20, 20);
            this.grpWakeInfo.Size = new System.Drawing.Size(640, 130);

            this.pnlWakeInfo.Location = new System.Drawing.Point(15, 8);
            this.pnlWakeInfo.Size = new System.Drawing.Size(610, 80);
            this.pnlWakeInfo.StateCommon.Color1 = System.Drawing.Color.FromArgb(240, 240, 240);

            this.picWakeInfo.Location = new System.Drawing.Point(10, 10);
            this.picWakeInfo.Size = new System.Drawing.Size(28, 28);
            this.picWakeInfo.SizeMode = PictureBoxSizeMode.StretchImage;
            this.picWakeInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblWakeDescription.AutoSize = false;
            this.lblWakeDescription.Location = new System.Drawing.Point(50, 15);
            this.lblWakeDescription.Size = new System.Drawing.Size(550, 50);
            this.lblWakeDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblWakeDescription.StateCommon.ShortText.Color1 = System.Drawing.Color.FromArgb(60, 60, 60);
            this.lblWakeDescription.Text =
                LanguageManager.Get("Wake.Description") ??
                "WakeMonitor peut vous remonter des informations qui ont provoqué le réveil de votre machine. " +
                "Ces informations sont envoyées par mail. Vous pouvez créer une tâche planifiée qui réagira " +
                "avec Power-Troubleshooter sur l'ID = 1.";

            this.pnlWakeInfo.Controls.Add(this.picWakeInfo);
            this.pnlWakeInfo.Controls.Add(this.lblWakeDescription);
            this.grpWakeInfo.Panel.Controls.Add(this.pnlWakeInfo);
            this.tabWakeMonitor.Controls.Add(this.grpWakeInfo);

            this.grpWakeOptions.Text = LanguageManager.Get("Indications à donner dans le mail") ?? "Indications à donner dans le mail";
            this.grpWakeOptions.Location = new System.Drawing.Point(20, 160);
            this.grpWakeOptions.Size = new System.Drawing.Size(420, 230);
            this.grpWakeOptions.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);

            this.chkPublicIP.AutoSize = true;
            this.chkPublicIP.Location = new System.Drawing.Point(15, 5);
            this.chkPublicIP.Text = LanguageManager.Get("Inclure IP publique") ?? "Inclure IP publique";
            this.chkPublicIP.Font = normalFont;

            this.chkLocalIP.AutoSize = true;
            this.chkLocalIP.Location = new System.Drawing.Point(15, 35);
            this.chkLocalIP.Text = LanguageManager.Get("Inclure IP locale") ?? "Inclure IP locale";
            this.chkLocalIP.Font = normalFont;

            this.chkMAC.AutoSize = true;
            this.chkMAC.Location = new System.Drawing.Point(15, 65);
            this.chkMAC.Text = LanguageManager.Get("Inclure MAC") ?? "Inclure MAC";
            this.chkMAC.Font = normalFont;

            this.chkUSB.AutoSize = true;
            this.chkUSB.Location = new System.Drawing.Point(15, 95);
            this.chkUSB.Text = LanguageManager.Get("Inclure USB") ?? "Inclure USB";
            this.chkUSB.Font = normalFont;

            this.chkCause.AutoSize = true;
            this.chkCause.Location = new System.Drawing.Point(15, 125);
            this.chkCause.Text = LanguageManager.Get("Inclure cause") ?? "Inclure cause";
            this.chkCause.Font = normalFont;

            this.chkDuration.AutoSize = true;
            this.chkDuration.Location = new System.Drawing.Point(15, 155);
            this.chkDuration.Text = LanguageManager.Get("Inclure durée") ?? "Inclure durée";
            this.chkDuration.Font = normalFont;

            this.grpWakeOptions.Panel.Controls.Add(this.chkPublicIP);
            this.grpWakeOptions.Panel.Controls.Add(this.chkLocalIP);
            this.grpWakeOptions.Panel.Controls.Add(this.chkMAC);
            this.grpWakeOptions.Panel.Controls.Add(this.chkUSB);
            this.grpWakeOptions.Panel.Controls.Add(this.chkCause);
            this.grpWakeOptions.Panel.Controls.Add(this.chkDuration);
            this.tabWakeMonitor.Controls.Add(this.grpWakeOptions);

            this.btnCreateWakeTask.Location = new System.Drawing.Point(460, 170);
            this.btnCreateWakeTask.Size = new System.Drawing.Size(200, 35);
            this.btnCreateWakeTask.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateWakeTask.Font = normalFont;
            this.btnCreateWakeTask.Click += new System.EventHandler(this.BtnCreateWakeTask_Click);

            this.btnDeleteWakeTask.Location = new System.Drawing.Point(460, 215);
            this.btnDeleteWakeTask.Size = new System.Drawing.Size(200, 35);
            this.btnDeleteWakeTask.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteWakeTask.Font = normalFont;
            this.btnDeleteWakeTask.Click += new System.EventHandler(this.BtnDeleteWakeTask_Click);

            this.btnManageWolMacs.Location = new System.Drawing.Point(460, 260);
            this.btnManageWolMacs.Size = new System.Drawing.Size(200, 35);
            this.btnManageWolMacs.Text = LanguageManager.Get("Gérer MAC autorisées") ?? "Gérer MAC autorisées";
            this.btnManageWolMacs.Font = normalFont;
            this.btnManageWolMacs.Click += new System.EventHandler(this.BtnManageWolMacs_Click);

            int wakeButtonY = 420;

            this.btnSaveWakeConfig.Size = new System.Drawing.Size(200, 35);
            this.btnSaveWakeConfig.Location = new System.Drawing.Point((672 - 200) / 2 - 110, wakeButtonY);
            this.btnSaveWakeConfig.Text = LanguageManager.Get("Enregistrer configuration") ?? "Enregistrer configuration";
            this.btnSaveWakeConfig.Font = normalFont;
            this.btnSaveWakeConfig.Click += new System.EventHandler(this.BtnSaveWakeConfig_Click);

            this.btnRunWake.Size = new System.Drawing.Size(200, 35);
            this.btnRunWake.Location = new System.Drawing.Point((672 - 200) / 2 + 110, wakeButtonY);
            this.btnRunWake.Text = LanguageManager.Get("Envoi d'un mail de test") ?? "Envoi d'un mail de test";
            this.btnRunWake.Font = normalFont;
            this.btnRunWake.Click += new System.EventHandler(this.BtnRunWake_Click);

            this.tabWakeMonitor.Controls.Add(this.btnSaveWakeConfig);
            this.tabWakeMonitor.Controls.Add(this.btnRunWake);
            this.tabWakeMonitor.Controls.Add(this.btnCreateWakeTask);
            this.tabWakeMonitor.Controls.Add(this.btnDeleteWakeTask);
            this.tabWakeMonitor.Controls.Add(this.btnManageWolMacs);

            // ============================================================
            // STOP MONITOR
            // ============================================================
            this.grpStopInfo = new KryptonGroupBox();
            this.pnlStopInfo = new KryptonPanel();
            this.picStopInfo = new KryptonPictureBox();
            this.lblStopDescription = new KryptonLabel();

            this.grpStopInfo.Text = LanguageManager.Get("A propos de StopMonitor") ?? "A propos de StopMonitor";
            this.grpStopInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpStopInfo.Location = new System.Drawing.Point(20, 20);
            this.grpStopInfo.Size = new System.Drawing.Size(640, 130);

            this.pnlStopInfo.Location = new System.Drawing.Point(15, 8);
            this.pnlStopInfo.Size = new System.Drawing.Size(610, 80);
            this.pnlStopInfo.StateCommon.Color1 = System.Drawing.Color.FromArgb(240, 240, 240);

            this.picStopInfo.Location = new System.Drawing.Point(10, 10);
            this.picStopInfo.Size = new System.Drawing.Size(28, 28);
            this.picStopInfo.SizeMode = PictureBoxSizeMode.StretchImage;
            this.picStopInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblStopDescription.AutoSize = false;
            this.lblStopDescription.Location = new System.Drawing.Point(50, 15);
            this.lblStopDescription.Size = new System.Drawing.Size(550, 50);
            this.lblStopDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblStopDescription.StateCommon.ShortText.Color1 = System.Drawing.Color.FromArgb(60, 60, 60);
            this.lblStopDescription.Text =
                LanguageManager.Get("Stop.Description") ??
                "StopMonitor vous permet de savoir pourquoi votre machine a démarré ou redémarré via une tâche planifiée.";

            this.pnlStopInfo.Controls.Add(this.picStopInfo);
            this.pnlStopInfo.Controls.Add(this.lblStopDescription);
            this.grpStopInfo.Panel.Controls.Add(this.pnlStopInfo);
            this.tabStopMonitor.Controls.Add(this.grpStopInfo);

            this.btnCreateStopTask = new KryptonButton();
            this.btnDeleteStopTask = new KryptonButton();
            this.btnRunStopMonitor = new KryptonButton();

            this.btnCreateStopTask.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateStopTask.Location = new System.Drawing.Point(20, 170);
            this.btnCreateStopTask.Size = new System.Drawing.Size(200, 35);
            this.btnCreateStopTask.Font = normalFont;
            this.btnCreateStopTask.Click += new System.EventHandler(this.BtnCreateStopTask_Click);

            this.btnDeleteStopTask.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteStopTask.Location = new System.Drawing.Point(20, 215);
            this.btnDeleteStopTask.Size = new System.Drawing.Size(200, 35);
            this.btnDeleteStopTask.Font = normalFont;
            this.btnDeleteStopTask.Click += new System.EventHandler(this.BtnDeleteStopTask_Click);

            this.btnRunStopMonitor.Text = LanguageManager.Get("Envoi d'un mail de test") ?? "Envoi d'un mail de test";
            this.btnRunStopMonitor.Location = new System.Drawing.Point(20, 260);
            this.btnRunStopMonitor.Size = new System.Drawing.Size(200, 35);
            this.btnRunStopMonitor.Font = normalFont;
            this.btnRunStopMonitor.Click += new System.EventHandler(this.BtnRunStopMonitor_Click);

            this.tabStopMonitor.Controls.Add(this.btnCreateStopTask);
            this.tabStopMonitor.Controls.Add(this.btnDeleteStopTask);
            this.tabStopMonitor.Controls.Add(this.btnRunStopMonitor);

            // ============================================================
            // ON / OFF
            // ============================================================
            this.grpOnOffInfo = new KryptonGroupBox();
            this.grpOnOffInfo.Text = LanguageManager.Get("À propos de On/Off") ?? "À propos de On/Off";
            this.grpOnOffInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpOnOffInfo.Location = new System.Drawing.Point(20, 20);
            this.grpOnOffInfo.Size = new System.Drawing.Size(640, 130);

            this.pnlOnOffInfo = new KryptonPanel();
            this.lblOnOffInfo = new KryptonLabel();
            this.picOnOffInfo = new KryptonPictureBox();

            this.pnlOnOffInfo.Location = new System.Drawing.Point(15, 8);
            this.pnlOnOffInfo.Size = new System.Drawing.Size(610, 80);
            this.pnlOnOffInfo.StateCommon.Color1 = System.Drawing.Color.FromArgb(240, 240, 240);

            this.picOnOffInfo.Location = new System.Drawing.Point(10, 10);
            this.picOnOffInfo.Size = new System.Drawing.Size(28, 28);
            this.picOnOffInfo.SizeMode = PictureBoxSizeMode.StretchImage;
            this.picOnOffInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblOnOffInfo.AutoSize = false;
            this.lblOnOffInfo.Location = new System.Drawing.Point(50, 15);
            this.lblOnOffInfo.Size = new System.Drawing.Size(550, 50);
            this.lblOnOffInfo.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblOnOffInfo.StateCommon.ShortText.Color1 = System.Drawing.Color.FromArgb(60, 60, 60);
            this.lblOnOffInfo.Text =
                LanguageManager.Get("OnOff.Info.Description") ??
                "Le module On/Off permet de programmer l'arrêt ou la mise en veille de votre machine à une heure précise.\n" +
                "Cette heure est également utilisée par MediaMonitor pour envoyer un rapport par email 10 minutes avant l'arrêt ou la mise en veille.\n" +
                "Si vous ne souhaitez pas envoyer de rapport, vous pouvez désactiver cette fonctionnalité dans l'interface de MediaMonitor.";

            this.pnlOnOffInfo.Controls.Add(this.picOnOffInfo);
            this.pnlOnOffInfo.Controls.Add(this.lblOnOffInfo);
            this.grpOnOffInfo.Panel.Controls.Add(this.pnlOnOffInfo);
            this.tabOnOff.Controls.Add(this.grpOnOffInfo);

            this.grpShutdown = new KryptonGroupBox();
            this.lblShutdownHour = new KryptonLabel();
            this.numShutdownHour = new KryptonNumericUpDown();
            this.lblShutdownMinute = new KryptonLabel();
            this.numShutdownMinute = new KryptonNumericUpDown();
            this.lblShutdownType = new KryptonLabel();
            this.cmbShutdownType = new KryptonComboBox();
            this.btnCreateShutdownTask = new KryptonButton();
            this.btnDeleteShutdownTask = new KryptonButton();

            this.grpShutdown.Text = LanguageManager.Get("Arrêt programmé") ?? "Arrêt programmé";
            this.grpShutdown.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpShutdown.Location = new System.Drawing.Point(20, 165);
            this.grpShutdown.Size = new System.Drawing.Size(640, 150);

            this.lblShutdownHour.Text = LanguageManager.Get("Heure (0–23) :") ?? "Heure (0–23) :";
            this.lblShutdownHour.Location = new System.Drawing.Point(20, 10);
            this.lblShutdownHour.Font = normalFont;

            this.numShutdownHour.Minimum = 0;
            this.numShutdownHour.Maximum = 23;
            this.numShutdownHour.Location = new System.Drawing.Point(150, 5);
            this.numShutdownHour.Width = 60;
            this.numShutdownHour.Font = normalFont;

            this.lblShutdownMinute.Text = LanguageManager.Get("Minute (0–59) :") ?? "Minute (0–59) :";
            this.lblShutdownMinute.Location = new System.Drawing.Point(230, 10);
            this.lblShutdownMinute.Font = normalFont;

            this.numShutdownMinute.Minimum = 0;
            this.numShutdownMinute.Maximum = 59;
            this.numShutdownMinute.Location = new System.Drawing.Point(350, 5);
            this.numShutdownMinute.Width = 60;
            this.numShutdownMinute.Font = normalFont;

            this.lblShutdownType.Text = LanguageManager.Get("Choisir le type d'arrêt :") ?? "Choisir le type d'arrêt :";
            this.lblShutdownType.Location = new System.Drawing.Point(20, 40);
            this.lblShutdownType.Size = new System.Drawing.Size(150, 25);
            this.lblShutdownType.Font = normalFont;

            this.cmbShutdownType.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbShutdownType.Items.AddRange(new object[] { "Arrêt", "Veille" });
            this.cmbShutdownType.Location = new System.Drawing.Point(180, 40);
            this.cmbShutdownType.Size = new System.Drawing.Size(150, 25);
            this.cmbShutdownType.SelectedIndex = 0;
            this.cmbShutdownType.Font = normalFont;

            this.btnSaveOnOff = new KryptonButton();
            this.btnSaveOnOff.Text = LanguageManager.Get("Sauvegarder") ?? "Sauvegarder";
            this.btnSaveOnOff.Font = normalFont;
            this.btnSaveOnOff.Location = new System.Drawing.Point(10, 75);
            this.btnSaveOnOff.Size = new System.Drawing.Size(200, 35);
            this.btnSaveOnOff.Click += new System.EventHandler(this.BtnSaveOnOff_Click);

            this.grpShutdown.Panel.Controls.Add(this.btnSaveOnOff);

            this.btnCreateShutdownTask.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateShutdownTask.Location = new System.Drawing.Point(220, 75);
            this.btnCreateShutdownTask.Size = new System.Drawing.Size(200, 35);
            this.btnCreateShutdownTask.Font = normalFont;
            this.btnCreateShutdownTask.Click += new System.EventHandler(this.BtnCreateShutdownTask_Click);

            this.btnDeleteShutdownTask.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteShutdownTask.Location = new System.Drawing.Point(430, 75);
            this.btnDeleteShutdownTask.Size = new System.Drawing.Size(200, 35);
            this.btnDeleteShutdownTask.Font = normalFont;
            this.btnDeleteShutdownTask.Click += new System.EventHandler(this.BtnDeleteShutdownTask_Click);

            this.grpShutdown.Panel.Controls.Add(this.lblShutdownHour);
            this.grpShutdown.Panel.Controls.Add(this.numShutdownHour);
            this.grpShutdown.Panel.Controls.Add(this.lblShutdownMinute);
            this.grpShutdown.Panel.Controls.Add(this.numShutdownMinute);
            this.grpShutdown.Panel.Controls.Add(this.lblShutdownType);
            this.grpShutdown.Panel.Controls.Add(this.cmbShutdownType);
            this.grpShutdown.Panel.Controls.Add(this.btnCreateShutdownTask);
            this.grpShutdown.Panel.Controls.Add(this.btnDeleteShutdownTask);

            this.tabOnOff.Controls.Add(this.grpShutdown);

            this.grpWOL = new KryptonGroupBox();
            this.lblWOLInfo = new KryptonLabel();

            this.grpWOL.Text = LanguageManager.Get("Démarrage automatique (Wake On Lan)") ?? "Démarrage automatique (Wake On Lan)";
            this.grpWOL.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpWOL.Location = new System.Drawing.Point(20, 320);
            this.grpWOL.Size = new System.Drawing.Size(640, 120);

            this.lblWOLInfo.AutoSize = false;
            this.lblWOLInfo.Location = new System.Drawing.Point(20, 15);
            this.lblWOLInfo.Size = new System.Drawing.Size(600, 80);
            this.lblWOLInfo.Font = normalFont;
            this.lblWOLInfo.Text =
                LanguageManager.Get("WOL.Description") ??
                "Pour démarrer votre machine automatiquement vous devrez utiliser la méthode Wake On Lan.\n\n"
              + "Vous devez activer la fonctionnalité dans le BIOS, mais aussi dans le gestionnaire "
              + "de périphériques de Windows.\n\n"
              + "Si vous ne souhaitez pas utiliser WOL, vous devrez démarrer votre machine manuellement.";

            this.grpWOL.Panel.Controls.Add(this.lblWOLInfo);
            this.tabOnOff.Controls.Add(this.grpWOL);

            // ============================================================
            // À PROPOS
            // ============================================================
            this.grpAboutInfo = new KryptonGroupBox();
            this.grpAboutInfo.Text = LanguageManager.Get("À propos de MCEMonitor") ?? "À propos de MCEMonitor";
            this.grpAboutInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpAboutInfo.Location = new System.Drawing.Point(20, 20);
            this.grpAboutInfo.Size = new System.Drawing.Size(640, 440);

            this.pnlAboutScroll = new KryptonPanel();
            this.pnlAboutScroll.Location = new System.Drawing.Point(15, 8);
            this.pnlAboutScroll.Size = new System.Drawing.Size(610, 360);
            this.pnlAboutScroll.AutoScroll = true;
            this.pnlAboutScroll.StateCommon.Color1 = System.Drawing.Color.White;

            this.lblAbout = new KryptonLabel();
            this.lblAbout.AutoSize = true;
            this.lblAbout.MaximumSize = new System.Drawing.Size(560, 0);
            this.lblAbout.Location = new System.Drawing.Point(15, 15);
            this.lblAbout.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblAbout.StateCommon.ShortText.Color1 = System.Drawing.Color.FromArgb(60, 60, 60);
            this.lblAbout.Text =
                (LanguageManager.Get("À propos de MCEMonitor") ?? "À propos de MCEMonitor") +
                "\n\n" +
                "----------------------------------------\n" +
                "LICENCE MIT\n" +
                "----------------------------------------\n\n" +
            @"MCEMonitor (Version 1.7.5)
            Outil de supervision et d'automatisation pour Media Server (KODI).

            MIT License
            Copyright (c) 2026 Skypichat-kodi

            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files (the ""Software""), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

            The above copyright notice and this permission notice shall be included in all
            copies or substantial portions of the Software.

            THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
            AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.";

            this.pnlAboutScroll.Controls.Add(this.lblAbout);
            this.grpAboutInfo.Panel.Controls.Add(this.pnlAboutScroll);

            this.btnOpenLogs = new KryptonButton();
            this.btnOpenLogs.Text = LanguageManager.Get("Dossier Logs") ?? "Dossier Logs";
            this.btnOpenLogs.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnOpenLogs.Size = new System.Drawing.Size(150, 32);
            this.btnOpenLogs.Location = new System.Drawing.Point(15, 375);
            this.btnOpenLogs.Click += (s, e) =>
            {
                string logFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                if (!System.IO.Directory.Exists(logFolder))
                    System.IO.Directory.CreateDirectory(logFolder);

                try
                {
                    Process.Start("explorer.exe", logFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Impossible d'ouvrir le dossier Logs.\n\n" + ex.Message,
                        "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            this.btnPurgeLogs = new KryptonButton();
            this.btnPurgeLogs.Text = LanguageManager.Get("Purger les logs") ?? "Purger les logs";
            this.btnPurgeLogs.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnPurgeLogs.Size = new System.Drawing.Size(150, 32);
            this.btnPurgeLogs.Location = new System.Drawing.Point(175, 375);
            this.btnPurgeLogs.Click += (s, e) =>
            {
                string logFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                try
                {
                    if (System.IO.Directory.Exists(logFolder))
                    {
                        foreach (var file in System.IO.Directory.GetFiles(logFolder))
                            System.IO.File.Delete(file);
                    }
                    else
                    {
                        System.IO.Directory.CreateDirectory(logFolder);
                    }

                    MessageBox.Show(
                        LanguageManager.Get("Tous les logs ont été purgés.") ?? "Tous les logs ont été purgés.",
                        LanguageManager.Get("Succès") ?? "Succès",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        (LanguageManager.Get("Erreur lors de la purge des logs.") ?? "Erreur lors de la purge des logs.")
                        + "\n\n" + ex.Message,
                        LanguageManager.Get("Erreur") ?? "Erreur",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            };

            this.grpAboutInfo.Panel.Controls.Add(this.btnOpenLogs);
            this.grpAboutInfo.Panel.Controls.Add(this.btnPurgeLogs);

            // ✅ Bouton : changer de thème
            this.btnChooseTheme = new KryptonButton();
            this.btnChooseTheme.Text = LanguageManager.Get("Changer de thème") ?? "Changer de thème";
            this.btnChooseTheme.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnChooseTheme.Size = new System.Drawing.Size(150, 32);
            this.btnChooseTheme.Location = new System.Drawing.Point(335, 375);
            this.btnChooseTheme.Click += new System.EventHandler(this.BtnChooseTheme_Click);
            this.grpAboutInfo.Panel.Controls.Add(this.btnChooseTheme);

            this.tabAbout.Controls.Add(this.grpAboutInfo);

            // ============================================================
            // FINALISATION
            // ============================================================
            this.Controls.Add(this.tabControl);

            this.tabControl.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        // ============================================================
        // DÉCLARATIONS DES CONTRÔLES
        // ============================================================
        private KryptonNavigator tabControl;
        private KryptonPage tabEmail;
        private KryptonPage tabMediaMonitor;
        private KryptonPage tabRomMonitor;
        private KryptonPage tabWakeMonitor;
        private KryptonPage tabStopMonitor;
        private KryptonPage tabOnOff;
        private KryptonPage tabAbout;

        // ---------- Email ----------
        private KryptonGroupBox grpEmailInfo;
        private KryptonPanel pnlEmailInfo;
        private KryptonPictureBox picEmailInfo;
        private KryptonLabel lblEmailDescription;
        private KryptonLabel lblEmailTitle;
        private KryptonLabel lblSmtpServer;
        private KryptonLabel lblSmtpPort;
        private KryptonLabel lblEmailFrom;
        private KryptonLabel lblEmailPassword;
        private KryptonLabel lblEmailTo;
        private KryptonLabel lblSecurityMode;
        private KryptonTextBox txtSmtpServer;
        private KryptonTextBox txtSmtpPort;
        private KryptonTextBox txtEmailFrom;
        private KryptonTextBox txtEmailPassword;
        private KryptonTextBox txtEmailTo;
        private KryptonComboBox cmbSecurityMode;
        private KryptonButton btnSaveEmail;
        private KryptonButton btnTestEmail;
        private KryptonButton btnTogglePassword;
        private KryptonLabel lblSmtpStatusTitle;
        private System.Windows.Forms.ToolTip toolTipSmtp;
        private System.Windows.Forms.Panel pnlSmtpStatusDot;

        // ---------- On / Off ----------
        private KryptonGroupBox grpOnOffInfo;
        private KryptonPanel pnlOnOffInfo;
        private KryptonLabel lblOnOffInfo;
        private KryptonPictureBox picOnOffInfo;
        private KryptonGroupBox grpShutdown;
        private KryptonLabel lblShutdownHour;
        private KryptonNumericUpDown numShutdownHour;
        private KryptonLabel lblShutdownMinute;
        private KryptonNumericUpDown numShutdownMinute;
        private KryptonLabel lblShutdownType;
        private KryptonComboBox cmbShutdownType;
        private KryptonButton btnSaveOnOff;
        private KryptonButton btnCreateShutdownTask;
        private KryptonButton btnDeleteShutdownTask;
        private KryptonGroupBox grpWOL;
        private KryptonLabel lblWOLInfo;

        // ---------- Media Monitor ----------
        private KryptonGroupBox grpMediaInfo;
        private KryptonPanel pnlMediaInfo;
        private KryptonPictureBox picMediaInfo;
        private KryptonLabel lblMediaInfo;
        private KryptonGroupBox grpMediaActions;
        private KryptonCheckButton toggleMediaService;
        private KryptonLabel lblMediaStatus;
        private KryptonLabel lblNextReport;
        private KryptonLabel lblLastReport;
        private KryptonButton btnCreateMediaTask2;
        private KryptonButton btnDeleteMediaTask2;
        private KryptonButton btnOpenMediaUI;
        private System.Windows.Forms.Timer logRefreshTimer;
        private System.Windows.Forms.Timer mediaServiceTimer;

        // ---------- Rom Monitor ----------
        private KryptonGroupBox grpRomInfo;
        private KryptonPanel pnlRomInfo;
        private KryptonPictureBox picRomInfo;
        private KryptonLabel lblRomDescription;
        private KryptonGroupBox grpRomActions;
        private KryptonCheckButton toggleRomService;
        private KryptonLabel lblRomStatus;
        private KryptonGroupBox grpRomSettings;
        private KryptonLabel lblRomInterval;
        private KryptonNumericUpDown numRomInterval;
        private KryptonLabel lblRomWarnPct;
        private KryptonNumericUpDown numRomWarnPct;
        private KryptonLabel lblRomCritPct;
        private KryptonNumericUpDown numRomCritPct;
        private KryptonLabel lblRomWarnGo;
        private KryptonNumericUpDown numRomWarnGo;
        private KryptonLabel lblRomCritGo;
        private KryptonNumericUpDown numRomCritGo;
        private KryptonLabel lblRomCooldown;
        private KryptonNumericUpDown numRomCooldown;
        private KryptonCheckBox chkRomSmartAlert;
        private KryptonLabel lblRomHint;
        private KryptonButton btnCreateRomTask;
        private KryptonButton btnDeleteRomTask;
        private KryptonButton btnOpenRomUI;
        private KryptonButton btnSaveRomConfig;
        private System.Windows.Forms.Timer romMonitorTimer;

        // ---------- Wake Monitor ----------
        private KryptonGroupBox grpWakeInfo;
        private KryptonPanel pnlWakeInfo;
        private KryptonPictureBox picWakeInfo;
        private KryptonLabel lblWakeDescription;
        private KryptonGroupBox grpWakeOptions;
        private KryptonCheckBox chkPublicIP;
        private KryptonCheckBox chkLocalIP;
        private KryptonCheckBox chkMAC;
        private KryptonCheckBox chkUSB;
        private KryptonCheckBox chkCause;
        private KryptonCheckBox chkDuration;
        private KryptonButton btnSaveWakeConfig;
        private KryptonButton btnRunWake;
        private KryptonButton btnCreateWakeTask;
        private KryptonButton btnDeleteWakeTask;
        private KryptonButton btnManageWolMacs;

        // ---------- Stop Monitor ----------
        private KryptonGroupBox grpStopInfo;
        private KryptonPanel pnlStopInfo;
        private KryptonPictureBox picStopInfo;
        private KryptonLabel lblStopDescription;
        private KryptonButton btnCreateStopTask;
        private KryptonButton btnDeleteStopTask;
        private KryptonButton btnRunStopMonitor;

        // ---------- À propos ----------
        private KryptonGroupBox grpAboutInfo;
        private KryptonLabel lblAbout;
        private KryptonPanel pnlAboutScroll;
        private KryptonButton btnOpenLogs;
        private KryptonButton btnPurgeLogs;
        private KryptonButton btnChooseTheme;
    }
}