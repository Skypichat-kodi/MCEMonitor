using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;

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
            this.tabRomMonitor = new System.Windows.Forms.TabPage();
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
            this.Text = LanguageManager.Get("MCEMonitor") ?? "MCEMonitor";

            // ============================================================
            // TAB CONTROL
            // ============================================================
            this.tabControl.Location = new System.Drawing.Point(10, 10);
            this.tabControl.Size = new System.Drawing.Size(680, 450);
            this.tabControl.BackColor = Color.White;

            this.tabControl.Controls.Add(this.tabEmail);
            this.tabControl.Controls.Add(this.tabOnOff);
            this.tabControl.Controls.Add(this.tabMediaMonitor);
            this.tabControl.Controls.Add(this.tabRomMonitor);
            this.tabControl.Controls.Add(this.tabWakeMonitor);
            this.tabControl.Controls.Add(this.tabStopMonitor);            
            this.tabControl.Controls.Add(this.tabAbout);

            // ============================================================
            // CONFIGURATION DES ONGLETS
            // ============================================================
            this.tabEmail.Location = new System.Drawing.Point(4, 29);
            this.tabEmail.Size = new System.Drawing.Size(672, 467);
            this.tabEmail.Text = LanguageManager.Get("Email") ?? "Email";
            this.tabEmail.UseVisualStyleBackColor = true;

            this.tabMediaMonitor.Location = new System.Drawing.Point(4, 29);
            this.tabMediaMonitor.Size = new System.Drawing.Size(672, 467);
            this.tabMediaMonitor.Text = LanguageManager.Get("Media Monitor") ?? "Media Monitor";
            this.tabMediaMonitor.UseVisualStyleBackColor = true;

            this.tabRomMonitor.Location = new System.Drawing.Point(4, 29);
            this.tabRomMonitor.Size = new System.Drawing.Size(672, 467);
            this.tabRomMonitor.Text = LanguageManager.Get("Rom Monitor") ?? "Rom Monitor";
            this.tabRomMonitor.UseVisualStyleBackColor = true;

            this.tabWakeMonitor.Location = new System.Drawing.Point(4, 29);
            this.tabWakeMonitor.Size = new System.Drawing.Size(672, 467);
            this.tabWakeMonitor.Text = LanguageManager.Get("Wake Monitor") ?? "Wake Monitor";
            this.tabWakeMonitor.UseVisualStyleBackColor = true;

            this.tabStopMonitor.Location = new System.Drawing.Point(4, 29);
            this.tabStopMonitor.Size = new System.Drawing.Size(672, 467);
            this.tabStopMonitor.Text = LanguageManager.Get("Stop Monitor") ?? "Stop Monitor";
            this.tabStopMonitor.UseVisualStyleBackColor = true;

            this.tabOnOff.Location = new System.Drawing.Point(4, 29);
            this.tabOnOff.Size = new System.Drawing.Size(672, 467);
            this.tabOnOff.Text = LanguageManager.Get("On/Off") ?? "On/Off";
            this.tabOnOff.UseVisualStyleBackColor = true;

            this.tabAbout.Location = new System.Drawing.Point(4, 29);
            this.tabAbout.Size = new System.Drawing.Size(672, 467);
            this.tabAbout.Text = LanguageManager.Get("À propos") ?? "À propos";
            this.tabAbout.UseVisualStyleBackColor = true;

            // ============================================================
            // EMAIL — PANEL INFO
            // ============================================================
            this.pnlEmailInfo = new MCEMonitor.Controls.RoundedPanel();
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
            this.pnlEmailInfo.Location = new System.Drawing.Point(15, 10);
            this.pnlEmailInfo.Size = new System.Drawing.Size(640, 60);

            this.picEmailInfo.Location = new System.Drawing.Point(10, 15);
            this.picEmailInfo.Size = new System.Drawing.Size(28, 28);
            this.picEmailInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picEmailInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblEmailDescription.AutoSize = false;
            this.lblEmailDescription.Location = new System.Drawing.Point(45, 15);
            this.lblEmailDescription.Size = new System.Drawing.Size(580, 30);
            this.lblEmailDescription.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblEmailDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblEmailDescription.Text =
                LanguageManager.Get("Vous devez entrer vos informations d'envoi et un destinataire afin que les autres fonctionnalités du logiciel puissent envoyer des rapports automatiquement.") ??
                "Vous devez entrer vos informations d'envoi et un destinataire afin que les autres fonctionnalités du logiciel puissent envoyer des rapports automatiquement.";

            this.pnlEmailInfo.Controls.Add(this.picEmailInfo);
            this.pnlEmailInfo.Controls.Add(this.lblEmailDescription);
            this.tabEmail.Controls.Add(this.pnlEmailInfo);

            // TITRE EMAIL
            this.lblEmailTitle.AutoSize = true;
            this.lblEmailTitle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblEmailTitle.Location = new System.Drawing.Point(20, 80);
            this.lblEmailTitle.Text =
                LanguageManager.Get("Configuration Email") ?? "Configuration Email";
            this.tabEmail.Controls.Add(this.lblEmailTitle);

            // ALIGNEMENT DES CHAMPS
            int labelX = 40;
            int labelWidth = 150;
            int fieldX = 200;
            int fieldWidth = 300;
            int y = 100;
            int step = 28;

            // Serveur SMTP
            this.lblSmtpServer.Text = LanguageManager.Get("Serveur SMTP :") ?? "Serveur SMTP :";
            this.lblSmtpServer.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblSmtpServer.Location = new System.Drawing.Point(labelX, y);
            this.lblSmtpServer.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblSmtpServer.Font = normalFont;

            this.txtSmtpServer.Location = new System.Drawing.Point(fieldX, y);
            this.txtSmtpServer.Size = new System.Drawing.Size(fieldWidth, 20);
            this.txtSmtpServer.Font = normalFont;

            y += step;

            // Port
            this.lblSmtpPort.Text = LanguageManager.Get("Port :") ?? "Port :";
            this.lblSmtpPort.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblSmtpPort.Location = new System.Drawing.Point(labelX, y);
            this.lblSmtpPort.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblSmtpPort.Font = normalFont;

            this.txtSmtpPort.Location = new System.Drawing.Point(fieldX, y);
            this.txtSmtpPort.Size = new System.Drawing.Size(80, 20);
            this.txtSmtpPort.Font = normalFont;

            y += step;

            // Adresse expéditeur
            this.lblEmailFrom.Text = LanguageManager.Get("Adresse expéditeur :") ?? "Adresse expéditeur :";
            this.lblEmailFrom.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblEmailFrom.Location = new System.Drawing.Point(labelX, y);
            this.lblEmailFrom.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblEmailFrom.Font = normalFont;

            this.txtEmailFrom.Location = new System.Drawing.Point(fieldX, y);
            this.txtEmailFrom.Size = new System.Drawing.Size(fieldWidth, 20);
            this.txtEmailFrom.Font = normalFont;

            y += step;

            // Mot de passe
            this.lblEmailPassword.Text = LanguageManager.Get("Mot de passe :") ?? "Mot de passe :";
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
            this.btnTogglePassword.Text = LanguageManager.Get("Afficher") ?? "Afficher";
            this.btnTogglePassword.Font = normalFont;
            this.btnTogglePassword.Click += new System.EventHandler(this.BtnTogglePassword_Click);

            y += step;

            // Destinataire
            this.lblEmailTo.Text = LanguageManager.Get("Destinataire :") ?? "Destinataire :";
            this.lblEmailTo.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblEmailTo.Location = new System.Drawing.Point(labelX, y);
            this.lblEmailTo.Size = new System.Drawing.Size(labelWidth, 20);
            this.lblEmailTo.Font = normalFont;

            this.txtEmailTo.Location = new System.Drawing.Point(fieldX, y);
            this.txtEmailTo.Size = new System.Drawing.Size(fieldWidth, 20);
            this.txtEmailTo.Font = normalFont;

            y += step;

            // Sécurité
            this.lblSecurityMode.Text = LanguageManager.Get("Sécurité :") ?? "Sécurité :";
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
            // Bouton Enregistrer
            this.btnSaveEmail.Location = new System.Drawing.Point(100, 340);
            this.btnSaveEmail.Size = new System.Drawing.Size(220, 35);
            this.btnSaveEmail.Text = LanguageManager.Get("Enregistrer configuration") ?? "Enregistrer configuration";
            this.btnSaveEmail.Font = normalFont;

            // Style gris clair moderne
            this.btnSaveEmail.BackColor = Color.FromArgb(220, 220, 225);
            this.btnSaveEmail.FlatStyle = FlatStyle.Flat;
            this.btnSaveEmail.FlatAppearance.BorderSize = 1;
            this.btnSaveEmail.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);

            this.btnSaveEmail.Click += new System.EventHandler(this.BtnSaveEmail_Click);


            // Bouton Tester Email
            this.btnTestEmail.Location = new System.Drawing.Point(330, 340);
            this.btnTestEmail.Size = new System.Drawing.Size(200, 35);
            this.btnTestEmail.Text = LanguageManager.Get("Tester Email") ?? "Tester Email";
            this.btnTestEmail.Font = normalFont;

            // Style gris clair moderne
            this.btnTestEmail.BackColor = Color.FromArgb(220, 220, 225);
            this.btnTestEmail.FlatStyle = FlatStyle.Flat;
            this.btnTestEmail.FlatAppearance.BorderSize = 1;
            this.btnTestEmail.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);

            this.btnTestEmail.Click += new System.EventHandler(this.BtnTestEmail_Click);

            this.tabEmail.Controls.Add(this.btnSaveEmail);
            this.tabEmail.Controls.Add(this.btnTestEmail);

            // ============================================================
            // MEDIA MONITOR — CONTENU
            // ============================================================
            this.grpMediaInfo = new System.Windows.Forms.GroupBox();
            this.pnlMediaInfo = new MCEMonitor.Controls.RoundedPanel();
            this.picMediaInfo = new System.Windows.Forms.PictureBox();
            this.lblMediaInfo = new System.Windows.Forms.Label();

            this.grpMediaActions = new System.Windows.Forms.GroupBox();
            this.toggleMediaService = new MCEMonitor.Controls.Win11Toggle();
            this.lblMediaStatus = new System.Windows.Forms.Label();
            this.lblNextReport = new System.Windows.Forms.Label();
            this.lblLastReport = new System.Windows.Forms.Label();

            this.btnCreateMediaTask2 = new System.Windows.Forms.Button();
            this.btnDeleteMediaTask2 = new System.Windows.Forms.Button();
            this.btnOpenMediaUI = new System.Windows.Forms.Button();

            // GROUPBOX 1 — INFORMATIONS
            this.grpMediaInfo.Text = LanguageManager.Get("À propos de MediaMonitor") ?? "À propos de MediaMonitor";
            this.grpMediaInfo.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpMediaInfo.Location = new System.Drawing.Point(20, 20);
            this.grpMediaInfo.Size = new System.Drawing.Size(620, 120);

            // Panel info
            this.pnlMediaInfo.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.pnlMediaInfo.Location = new System.Drawing.Point(15, 25);
            this.pnlMediaInfo.Size = new System.Drawing.Size(590, 80);

            // Icône
            this.picMediaInfo.Location = new System.Drawing.Point(10, 10);
            this.picMediaInfo.Size = new System.Drawing.Size(28, 28);
            this.picMediaInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picMediaInfo.Image = SystemIcons.Information.ToBitmap();

            // Texte
            this.lblMediaInfo.AutoSize = false;
            this.lblMediaInfo.Location = new System.Drawing.Point(50, 15);
            this.lblMediaInfo.Size = new System.Drawing.Size(530, 45);
            this.lblMediaInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
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

            this.grpMediaActions.Text = LanguageManager.Get("Automatisation du rapport") ?? "Automatisation du rapport";
            this.grpMediaActions.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpMediaActions.Location = new System.Drawing.Point(20, 150);
            this.grpMediaActions.Size = new System.Drawing.Size(620, 160);

            // MediaMonitor — Toggle
            this.toggleMediaService.Location = new System.Drawing.Point(20, 29);
            this.toggleMediaService.Size = new System.Drawing.Size(44, 22);
            this.toggleMediaService.Click += new System.EventHandler(this.toggleMediaService_Click);

            this.lblMediaStatus.Text = LanguageManager.Get("Service MediaMonitor") ?? "Service MediaMonitor";
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

            // BOUTONS MEDIA MONITOR

            // Bouton Créer tâche
            this.btnCreateMediaTask2.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateMediaTask2.Font = normalFont;
            this.btnCreateMediaTask2.Size = new System.Drawing.Size(180, 32);
            this.btnCreateMediaTask2.Location = new System.Drawing.Point(30, 100);
            this.btnCreateMediaTask2.Click += new System.EventHandler(this.BtnCreateMediaTask_Click);

            // Style gris clair moderne
            this.btnCreateMediaTask2.BackColor = Color.FromArgb(220, 220, 225);
            this.btnCreateMediaTask2.FlatStyle = FlatStyle.Flat;
            this.btnCreateMediaTask2.FlatAppearance.BorderSize = 1;
            this.btnCreateMediaTask2.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // Bouton Supprimer tâche
            this.btnDeleteMediaTask2.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteMediaTask2.Font = normalFont;
            this.btnDeleteMediaTask2.Size = new System.Drawing.Size(180, 32);
            this.btnDeleteMediaTask2.Location = new System.Drawing.Point(220, 100);
            this.btnDeleteMediaTask2.Click += new System.EventHandler(this.BtnDeleteMediaTask_Click);

            // Style gris clair moderne
            this.btnDeleteMediaTask2.BackColor = Color.FromArgb(220, 220, 225);
            this.btnDeleteMediaTask2.FlatStyle = FlatStyle.Flat;
            this.btnDeleteMediaTask2.FlatAppearance.BorderSize = 1;
            this.btnDeleteMediaTask2.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // Bouton Ouvrir MediaMonitor UI
            this.btnOpenMediaUI.Text = LanguageManager.Get("Ouvrir MediaMonitor") ?? "Ouvrir MediaMonitor";
            this.btnOpenMediaUI.Font = normalFont;
            this.btnOpenMediaUI.Size = new System.Drawing.Size(180, 32);
            this.btnOpenMediaUI.Location = new System.Drawing.Point(410, 100);
            this.btnOpenMediaUI.Click += new System.EventHandler(this.BtnOpenUI_Click);

            // Style gris clair moderne
            this.btnOpenMediaUI.BackColor = Color.FromArgb(220, 220, 225);
            this.btnOpenMediaUI.FlatStyle = FlatStyle.Flat;
            this.btnOpenMediaUI.FlatAppearance.BorderSize = 1;
            this.btnOpenMediaUI.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);

            // Ajout dans le groupbox
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
            // ROM MONITOR — CONTENU
            // ============================================================

            // --- Instanciations ---
            this.lblRomTitle = new System.Windows.Forms.Label();
            this.pnlRomInfo = new MCEMonitor.Controls.RoundedPanel();
            this.picRomInfo = new System.Windows.Forms.PictureBox();
            this.lblRomDescription = new System.Windows.Forms.Label();
            this.grpRomActions = new System.Windows.Forms.GroupBox();
            this.toggleRomService = new MCEMonitor.Controls.Win11Toggle();
            this.lblRomStatus = new System.Windows.Forms.Label();
            this.grpRomSettings = new System.Windows.Forms.GroupBox();
            this.lblRomInterval = new System.Windows.Forms.Label();
            this.numRomInterval = new System.Windows.Forms.NumericUpDown();
            this.lblRomWarnPct = new System.Windows.Forms.Label();
            this.numRomWarnPct = new System.Windows.Forms.NumericUpDown();
            this.lblRomCritPct = new System.Windows.Forms.Label();
            this.numRomCritPct = new System.Windows.Forms.NumericUpDown();
            this.lblRomWarnGo = new System.Windows.Forms.Label();
            this.numRomWarnGo = new System.Windows.Forms.NumericUpDown();
            this.lblRomCritGo = new System.Windows.Forms.Label();
            this.numRomCritGo = new System.Windows.Forms.NumericUpDown();
            this.lblRomCooldown = new System.Windows.Forms.Label();
            this.numRomCooldown = new System.Windows.Forms.NumericUpDown();
            this.chkRomSmartAlert = new System.Windows.Forms.CheckBox();
            this.lblRomHint = new System.Windows.Forms.Label();
            this.btnCreateRomTask = new System.Windows.Forms.Button();
            this.btnDeleteRomTask = new System.Windows.Forms.Button();
            this.btnOpenRomUI = new System.Windows.Forms.Button();
            this.btnSaveRomConfig = new System.Windows.Forms.Button();
            this.romMonitorTimer = new System.Windows.Forms.Timer();

            // --- Titre (au-dessus) ---
            this.lblRomTitle.Text = LanguageManager.Get("À propos de RomMonitor") ?? "À propos de RomMonitor";
            this.lblRomTitle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblRomTitle.AutoSize = true;
            this.lblRomTitle.Location = new System.Drawing.Point(15, 10);
            this.tabRomMonitor.Controls.Add(this.lblRomTitle);

            // --- Panneau info grisé avec icône ---
            this.pnlRomInfo.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.pnlRomInfo.Location = new System.Drawing.Point(15, 40);
            this.pnlRomInfo.Size = new System.Drawing.Size(640, 55);

            this.picRomInfo.Location = new System.Drawing.Point(10, 10);
            this.picRomInfo.Size = new System.Drawing.Size(28, 28);
            this.picRomInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picRomInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblRomDescription.AutoSize = false;
            this.lblRomDescription.Location = new System.Drawing.Point(45, 5);
            this.lblRomDescription.Size = new System.Drawing.Size(580, 45);
            this.lblRomDescription.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblRomDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblRomDescription.Text =
                LanguageManager.Get("Rom.Description") ??
                "RomMonitor surveille l'espace disque et la santé SMART de vos disques.\n" +
                "En cas de défaillance SMART critique, un email d'alerte est envoyé.\n" +
                "Un log est tenu à jour pour chaque problème détecté.";

            this.pnlRomInfo.Controls.Add(this.picRomInfo);
            this.pnlRomInfo.Controls.Add(this.lblRomDescription);
            this.tabRomMonitor.Controls.Add(this.pnlRomInfo);

            // --- GroupBox Actions (Switch + Boutons) ---
            this.grpRomActions.Text = LanguageManager.Get("Automatisation RomMonitor") ?? "Automatisation RomMonitor";
            this.grpRomActions.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpRomActions.Location = new System.Drawing.Point(20, 110);
            this.grpRomActions.Size = new System.Drawing.Size(620, 100);

            // RomMonitor — Toggle
            this.toggleRomService.Location = new System.Drawing.Point(20, 29);
            this.toggleRomService.Size = new System.Drawing.Size(44, 22);
            this.toggleRomService.Click += new System.EventHandler(this.toggleRomService_Click);

            this.lblRomStatus.Text = LanguageManager.Get("Service RomMonitor") ?? "Service RomMonitor";
            this.lblRomStatus.Font = normalFont;
            this.lblRomStatus.Location = new System.Drawing.Point(70, 32);
            this.lblRomStatus.AutoSize = true;

            // Boutons
            this.btnCreateRomTask.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateRomTask.Font = normalFont;
            this.btnCreateRomTask.Size = new System.Drawing.Size(180, 32);
            this.btnCreateRomTask.Location = new System.Drawing.Point(30, 60);
            this.btnCreateRomTask.BackColor = Color.FromArgb(220, 220, 225);
            this.btnCreateRomTask.FlatStyle = FlatStyle.Flat;
            this.btnCreateRomTask.FlatAppearance.BorderSize = 1;
            this.btnCreateRomTask.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);
            this.btnCreateRomTask.Click += new System.EventHandler(this.BtnCreateRomTask_Click);

            this.btnDeleteRomTask.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteRomTask.Font = normalFont;
            this.btnDeleteRomTask.Size = new System.Drawing.Size(180, 32);
            this.btnDeleteRomTask.Location = new System.Drawing.Point(220, 60);
            this.btnDeleteRomTask.BackColor = Color.FromArgb(220, 220, 225);
            this.btnDeleteRomTask.FlatStyle = FlatStyle.Flat;
            this.btnDeleteRomTask.FlatAppearance.BorderSize = 1;
            this.btnDeleteRomTask.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);
            this.btnDeleteRomTask.Click += new System.EventHandler(this.BtnDeleteRomTask_Click);

            this.btnOpenRomUI.Text = LanguageManager.Get("Ouvrir RomMonitor") ?? "Ouvrir RomMonitor";
            this.btnOpenRomUI.Font = normalFont;
            this.btnOpenRomUI.Size = new System.Drawing.Size(180, 32);
            this.btnOpenRomUI.Location = new System.Drawing.Point(410, 60);
            this.btnOpenRomUI.BackColor = Color.FromArgb(220, 220, 225);
            this.btnOpenRomUI.FlatStyle = FlatStyle.Flat;
            this.btnOpenRomUI.FlatAppearance.BorderSize = 1;
            this.btnOpenRomUI.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);
            this.btnOpenRomUI.Click += new System.EventHandler(this.BtnOpenRomUI_Click);

            this.grpRomActions.Controls.Add(this.toggleRomService);
            this.grpRomActions.Controls.Add(this.lblRomStatus);
            this.grpRomActions.Controls.Add(this.btnCreateRomTask);
            this.grpRomActions.Controls.Add(this.btnDeleteRomTask);
            this.grpRomActions.Controls.Add(this.btnOpenRomUI);
            this.tabRomMonitor.Controls.Add(this.grpRomActions);

            // --- GroupBox Réglages ---
            this.grpRomSettings.Text = LanguageManager.Get("Réglages") ?? "Réglages";
            this.grpRomSettings.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpRomSettings.Location = new System.Drawing.Point(20, 220);
            this.grpRomSettings.Size = new System.Drawing.Size(620, 190);

            // Fréquence
            this.lblRomInterval.Text = LanguageManager.Get("Fréquence de contrôle (min) :") ?? "Fréquence de contrôle (min) :";
            this.lblRomInterval.Location = new System.Drawing.Point(20, 30);
            this.lblRomInterval.Size = new System.Drawing.Size(200, 20);
            this.lblRomInterval.Font = normalFont;

            this.numRomInterval.Location = new System.Drawing.Point(230, 30);
            this.numRomInterval.Size = new System.Drawing.Size(80, 20);
            this.numRomInterval.Minimum = 15;
            this.numRomInterval.Maximum = 1440;
            this.numRomInterval.Font = normalFont;

            // Seuil danger %
            this.lblRomWarnPct.Text = LanguageManager.Get("Seuil warning (% libre) :") ?? "Seuil de danger (% libre) :";
            this.lblRomWarnPct.Location = new System.Drawing.Point(20, 58);
            this.lblRomWarnPct.Size = new System.Drawing.Size(200, 20);
            this.lblRomWarnPct.Font = normalFont;

            this.numRomWarnPct.Location = new System.Drawing.Point(230, 58);
            this.numRomWarnPct.Size = new System.Drawing.Size(80, 20);
            this.numRomWarnPct.Minimum = 5;
            this.numRomWarnPct.Maximum = 100;
            this.numRomWarnPct.Font = normalFont;

            // Seuil critique %
            this.lblRomCritPct.Text = LanguageManager.Get("Seuil critique (% libre) :") ?? "Seuil critique (% libre) :";
            this.lblRomCritPct.Location = new System.Drawing.Point(20, 86);
            this.lblRomCritPct.Size = new System.Drawing.Size(200, 20);
            this.lblRomCritPct.Font = normalFont;

            this.numRomCritPct.Location = new System.Drawing.Point(230, 86);
            this.numRomCritPct.Size = new System.Drawing.Size(80, 20);
            this.numRomCritPct.Minimum = 1;
            this.numRomCritPct.Maximum = 100;
            this.numRomCritPct.Font = normalFont;

            // Cooldown
            this.lblRomCooldown.Text = LanguageManager.Get("Intervale d'envoi des alertes (h) :") ?? "Intervale d'envoi des alertes (h) :";
            this.lblRomCooldown.Location = new System.Drawing.Point(330, 30);
            this.lblRomCooldown.Size = new System.Drawing.Size(180, 20);
            this.lblRomCooldown.Font = normalFont;

            this.numRomCooldown.Location = new System.Drawing.Point(520, 30);
            this.numRomCooldown.Size = new System.Drawing.Size(70, 20);
            this.numRomCooldown.Minimum = 1;
            this.numRomCooldown.Maximum = 168;
            this.numRomCooldown.Font = normalFont;

            // ?? Champs Go masqués (valeurs fixées à 10 et 5 dans le code)
            this.lblRomWarnGo.Text = "Seuil warning (Go) :";
            this.lblRomWarnGo.Location = new System.Drawing.Point(20, 114);
            this.lblRomWarnGo.Size = new System.Drawing.Size(200, 20);
            this.lblRomWarnGo.Font = normalFont;
            this.lblRomWarnGo.Visible = false;

            this.numRomWarnGo.Location = new System.Drawing.Point(230, 114);
            this.numRomWarnGo.Size = new System.Drawing.Size(80, 20);
            this.numRomWarnGo.Minimum = 10;
            this.numRomWarnGo.Maximum = 10;
            this.numRomWarnGo.Value = 10;
            this.numRomWarnGo.Font = normalFont;
            this.numRomWarnGo.Visible = false;

            this.lblRomCritGo.Text = "Seuil critique (Go) :";
            this.lblRomCritGo.Location = new System.Drawing.Point(330, 58);
            this.lblRomCritGo.Size = new System.Drawing.Size(180, 20);
            this.lblRomCritGo.Font = normalFont;
            this.lblRomCritGo.Visible = false;

            this.numRomCritGo.Location = new System.Drawing.Point(520, 58);
            this.numRomCritGo.Size = new System.Drawing.Size(70, 20);
            this.numRomCritGo.Minimum = 5;
            this.numRomCritGo.Maximum = 5;
            this.numRomCritGo.Value = 5;
            this.numRomCritGo.Font = normalFont;
            this.numRomCritGo.Visible = false;

            // CheckBox alerte SMART
            this.chkRomSmartAlert.Text = LanguageManager.Get("Alerte email, SMART et disque plein") ?? "Alerte email, SMART et disque plein";
            this.chkRomSmartAlert.Location = new System.Drawing.Point(20, 120);
            this.chkRomSmartAlert.Size = new System.Drawing.Size(400, 20);
            this.chkRomSmartAlert.Font = normalFont;

            // Texte explicatif
            this.lblRomHint.Text = "Les seuils en Go sont fixés à 10 Go (warning) et 5 Go (critique) pour les petits disques.";
            this.lblRomHint.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Italic);
            this.lblRomHint.ForeColor = System.Drawing.Color.Gray;
            this.lblRomHint.Location = new System.Drawing.Point(20, 145);
            this.lblRomHint.Size = new System.Drawing.Size(580, 20);

            // Ajout des contrôles au GroupBox (les Go masqués ne sont PAS ajoutés)
            this.grpRomSettings.Controls.Add(this.lblRomInterval);
            this.grpRomSettings.Controls.Add(this.numRomInterval);
            this.grpRomSettings.Controls.Add(this.lblRomWarnPct);
            this.grpRomSettings.Controls.Add(this.numRomWarnPct);
            this.grpRomSettings.Controls.Add(this.lblRomCritPct);
            this.grpRomSettings.Controls.Add(this.numRomCritPct);
            this.grpRomSettings.Controls.Add(this.lblRomCooldown);
            this.grpRomSettings.Controls.Add(this.numRomCooldown);
            this.grpRomSettings.Controls.Add(this.chkRomSmartAlert);
            this.grpRomSettings.Controls.Add(this.lblRomHint);
            this.tabRomMonitor.Controls.Add(this.grpRomSettings);

            // --- Bouton Enregistrer ---
            this.btnSaveRomConfig.Text = LanguageManager.Get("Enregistrer les réglages") ?? "Enregistrer les réglages";
            this.btnSaveRomConfig.Font = normalFont;
            this.btnSaveRomConfig.Size = new System.Drawing.Size(200, 35);
            this.btnSaveRomConfig.Location = new System.Drawing.Point(400, 65);
            this.btnSaveRomConfig.BackColor = Color.FromArgb(220, 220, 225);
            this.btnSaveRomConfig.FlatStyle = FlatStyle.Flat;
            this.btnSaveRomConfig.FlatAppearance.BorderSize = 1;
            this.btnSaveRomConfig.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);
            this.btnSaveRomConfig.Click += new System.EventHandler(this.BtnSaveRomConfig_Click);
            this.grpRomSettings.Controls.Add(this.btnSaveRomConfig);

            // --- Timer RomMonitor ---
            this.romMonitorTimer.Interval = 3000;
            this.romMonitorTimer.Tick += new System.EventHandler(this.RomMonitorTimer_Tick);
            this.romMonitorTimer.Start();
            
            // ============================================================
            // WAKE MONITOR — CONTENU
            // ============================================================
            this.lblWakeTitle = new System.Windows.Forms.Label();
            this.pnlWakeInfo = new MCEMonitor.Controls.RoundedPanel();
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
            this.lblWakeTitle.Text = LanguageManager.Get("À propos de WakeMonitor") ?? "À propos de WakeMonitor";
            this.lblWakeTitle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblWakeTitle.AutoSize = true;
            this.lblWakeTitle.Location = new System.Drawing.Point(15, 10);
            this.tabWakeMonitor.Controls.Add(this.lblWakeTitle);

            // PANEL INFO
            this.pnlWakeInfo.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.pnlWakeInfo.Location = new System.Drawing.Point(15, 45);
            this.pnlWakeInfo.Size = new System.Drawing.Size(640, 55);

            this.picWakeInfo.Location = new System.Drawing.Point(10, 10);
            this.picWakeInfo.Size = new System.Drawing.Size(28, 28);
            this.picWakeInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picWakeInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblWakeDescription.AutoSize = false;
            this.lblWakeDescription.Location = new System.Drawing.Point(45, 5);
            this.lblWakeDescription.Size = new System.Drawing.Size(580, 45);
            this.lblWakeDescription.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
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
            this.grpWakeOptions.Text = LanguageManager.Get("Indications à donner dans le mail") ?? "Indications à donner dans le mail";
            this.grpWakeOptions.Location = new System.Drawing.Point(15, 110);
            this.grpWakeOptions.Size = new System.Drawing.Size(400, 200);
            this.grpWakeOptions.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);

            this.chkPublicIP.AutoSize = true;
            this.chkPublicIP.Location = new System.Drawing.Point(15, 30);
            this.chkPublicIP.Text = LanguageManager.Get("Inclure IP publique") ?? "Inclure IP publique";
            this.chkPublicIP.Font = normalFont;

            this.chkLocalIP.AutoSize = true;
            this.chkLocalIP.Location = new System.Drawing.Point(15, 60);
            this.chkLocalIP.Text = LanguageManager.Get("Inclure IP locale") ?? "Inclure IP locale";
            this.chkLocalIP.Font = normalFont;

            this.chkMAC.AutoSize = true;
            this.chkMAC.Location = new System.Drawing.Point(15, 90);
            this.chkMAC.Text = LanguageManager.Get("Inclure MAC") ?? "Inclure MAC";
            this.chkMAC.Font = normalFont;

            this.chkUSB.AutoSize = true;
            this.chkUSB.Location = new System.Drawing.Point(15, 120);
            this.chkUSB.Text = LanguageManager.Get("Inclure USB") ?? "Inclure USB";
            this.chkUSB.Font = normalFont;

            this.chkCause.AutoSize = true;
            this.chkCause.Location = new System.Drawing.Point(15, 150);
            this.chkCause.Text = LanguageManager.Get("Inclure cause") ?? "Inclure cause";
            this.chkCause.Font = normalFont;

            this.chkDuration.AutoSize = true;
            this.chkDuration.Location = new System.Drawing.Point(15, 180);
            this.chkDuration.Text = LanguageManager.Get("Inclure durée") ?? "Inclure durée";
            this.chkDuration.Font = normalFont;

            this.grpWakeOptions.Controls.Add(this.chkPublicIP);
            this.grpWakeOptions.Controls.Add(this.chkLocalIP);
            this.grpWakeOptions.Controls.Add(this.chkMAC);
            this.grpWakeOptions.Controls.Add(this.chkUSB);
            this.grpWakeOptions.Controls.Add(this.chkCause);
            this.grpWakeOptions.Controls.Add(this.chkDuration);

            this.tabWakeMonitor.Controls.Add(this.grpWakeOptions);

            // BOUTONS WAKE

            // Créer tâche
            this.btnCreateWakeTask.Location = new System.Drawing.Point(440, 120);
            this.btnCreateWakeTask.Size = new System.Drawing.Size(200, 35);
            this.btnCreateWakeTask.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateWakeTask.Font = normalFont;
            this.btnCreateWakeTask.Click += new System.EventHandler(this.BtnCreateWakeTask_Click);

            // Style gris clair moderne
            this.btnCreateWakeTask.BackColor = Color.FromArgb(220, 220, 225);
            this.btnCreateWakeTask.FlatStyle = FlatStyle.Flat;
            this.btnCreateWakeTask.FlatAppearance.BorderSize = 1;
            this.btnCreateWakeTask.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // Supprimer tâche
            this.btnDeleteWakeTask.Location = new System.Drawing.Point(440, 165);
            this.btnDeleteWakeTask.Size = new System.Drawing.Size(200, 35);
            this.btnDeleteWakeTask.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteWakeTask.Font = normalFont;
            this.btnDeleteWakeTask.Click += new System.EventHandler(this.BtnDeleteWakeTask_Click);

            // Style gris clair moderne
            this.btnDeleteWakeTask.BackColor = Color.FromArgb(220, 220, 225);
            this.btnDeleteWakeTask.FlatStyle = FlatStyle.Flat;
            this.btnDeleteWakeTask.FlatAppearance.BorderSize = 1;
            this.btnDeleteWakeTask.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // Gérer MAC autorisées
            this.btnManageWolMacs.Location = new System.Drawing.Point(440, 210);
            this.btnManageWolMacs.Size = new System.Drawing.Size(200, 35);
            this.btnManageWolMacs.Text = LanguageManager.Get("Gérer MAC autorisées") ?? "Gérer MAC autorisées";
            this.btnManageWolMacs.Font = normalFont;
            this.btnManageWolMacs.Click += new System.EventHandler(this.BtnManageWolMacs_Click);

            // Style gris clair moderne
            this.btnManageWolMacs.BackColor = Color.FromArgb(220, 220, 225);
            this.btnManageWolMacs.FlatStyle = FlatStyle.Flat;
            this.btnManageWolMacs.FlatAppearance.BorderSize = 1;
            this.btnManageWolMacs.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // Bouton Enregistrer configuration
            int wakeButtonY = 340;

            this.btnSaveWakeConfig.Size = new System.Drawing.Size(200, 35);
            this.btnSaveWakeConfig.Location = new System.Drawing.Point((672 - 200) / 2 - 110, wakeButtonY);
            this.btnSaveWakeConfig.Text = LanguageManager.Get("Enregistrer configuration") ?? "Enregistrer configuration";
            this.btnSaveWakeConfig.Font = normalFont;
            this.btnSaveWakeConfig.Click += new System.EventHandler(this.BtnSaveWakeConfig_Click);

            // Style gris clair moderne
            this.btnSaveWakeConfig.BackColor = Color.FromArgb(220, 220, 225);
            this.btnSaveWakeConfig.FlatStyle = FlatStyle.Flat;
            this.btnSaveWakeConfig.FlatAppearance.BorderSize = 1;
            this.btnSaveWakeConfig.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // Bouton Envoi test
            this.btnRunWake.Size = new System.Drawing.Size(200, 35);
            this.btnRunWake.Location = new System.Drawing.Point((672 - 200) / 2 + 110, wakeButtonY);
            this.btnRunWake.Text = LanguageManager.Get("Envoi d'un mail de test") ?? "Envoi d'un mail de test";
            this.btnRunWake.Font = normalFont;
            this.btnRunWake.Click += new System.EventHandler(this.BtnRunWake_Click);

            // Style gris clair moderne
            this.btnRunWake.BackColor = Color.FromArgb(220, 220, 225);
            this.btnRunWake.FlatStyle = FlatStyle.Flat;
            this.btnRunWake.FlatAppearance.BorderSize = 1;
            this.btnRunWake.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);

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
            this.lblStopTitle.Text = LanguageManager.Get("A propos de StopMonitor") ?? "A propos de StopMonitor";
            this.tabStopMonitor.Controls.Add(this.lblStopTitle);

            // ============================================================
            // STOP MONITOR — CONTENU
            // ============================================================
            this.pnlStopInfo = new MCEMonitor.Controls.RoundedPanel();
            this.picStopInfo = new System.Windows.Forms.PictureBox();
            this.lblStopDescription = new System.Windows.Forms.Label();

            this.pnlStopInfo.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.pnlStopInfo.Location = new System.Drawing.Point(15, 30);
            this.pnlStopInfo.Size = new System.Drawing.Size(640, 55);

            this.picStopInfo.Location = new System.Drawing.Point(10, 10);
            this.picStopInfo.Size = new System.Drawing.Size(28, 28);
            this.picStopInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picStopInfo.Image = SystemIcons.Information.ToBitmap();

            this.lblStopDescription.AutoSize = false;
            this.lblStopDescription.Location = new System.Drawing.Point(45, 15);
            this.lblStopDescription.Size = new System.Drawing.Size(580, 20);
            this.lblStopDescription.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
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

            // Bouton Créer tâche
            this.btnCreateStopTask.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateStopTask.Location = new System.Drawing.Point(20, 100);
            this.btnCreateStopTask.Size = new System.Drawing.Size(200, 35);
            this.btnCreateStopTask.Font = normalFont;
            this.btnCreateStopTask.Click += new System.EventHandler(this.BtnCreateStopTask_Click);

            // Style gris clair moderne
            this.btnCreateStopTask.BackColor = Color.FromArgb(220, 220, 225);
            this.btnCreateStopTask.FlatStyle = FlatStyle.Flat;
            this.btnCreateStopTask.FlatAppearance.BorderSize = 1;
            this.btnCreateStopTask.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // Bouton Supprimer tâche
            this.btnDeleteStopTask.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteStopTask.Location = new System.Drawing.Point(20, 145);
            this.btnDeleteStopTask.Size = new System.Drawing.Size(200, 35);
            this.btnDeleteStopTask.Font = normalFont;
            this.btnDeleteStopTask.Click += new System.EventHandler(this.BtnDeleteStopTask_Click);

            // Style gris clair moderne
            this.btnDeleteStopTask.BackColor = Color.FromArgb(220, 220, 225);
            this.btnDeleteStopTask.FlatStyle = FlatStyle.Flat;
            this.btnDeleteStopTask.FlatAppearance.BorderSize = 1;
            this.btnDeleteStopTask.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // Bouton Envoi d'un mail de test
            this.btnRunStopMonitor.Text = LanguageManager.Get("Envoi d'un mail de test") ?? "Envoi d'un mail de test";
            this.btnRunStopMonitor.Location = new System.Drawing.Point(20, 190);
            this.btnRunStopMonitor.Size = new System.Drawing.Size(200, 35);
            this.btnRunStopMonitor.Font = normalFont;
            this.btnRunStopMonitor.Click += new System.EventHandler(this.BtnRunStopMonitor_Click);

            // Style gris clair moderne
            this.btnRunStopMonitor.BackColor = Color.FromArgb(220, 220, 225);
            this.btnRunStopMonitor.FlatStyle = FlatStyle.Flat;
            this.btnRunStopMonitor.FlatAppearance.BorderSize = 1;
            this.btnRunStopMonitor.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);

            this.tabStopMonitor.Controls.Add(this.btnCreateStopTask);
            this.tabStopMonitor.Controls.Add(this.btnDeleteStopTask);
            this.tabStopMonitor.Controls.Add(this.btnRunStopMonitor);

            // ============================================================
            // ON / OFF — CONTENU
            // ============================================================
            // BLOC D'INFORMATION ON/OFF — même style que les autres onglets
            this.pnlOnOffInfo = new MCEMonitor.Controls.RoundedPanel();
            this.lblOnOffInfo = new System.Windows.Forms.Label();
            this.picOnOffInfo = new System.Windows.Forms.PictureBox();

            this.pnlOnOffInfo.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.pnlOnOffInfo.Location = new System.Drawing.Point(15, 10);
            this.pnlOnOffInfo.Size = new System.Drawing.Size(640, 110);

            // Icône info
            this.picOnOffInfo.Location = new System.Drawing.Point(10, 20);
            this.picOnOffInfo.Size = new System.Drawing.Size(28, 28);
            this.picOnOffInfo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picOnOffInfo.Image = SystemIcons.Information.ToBitmap();

            // Texte
            this.lblOnOffInfo.AutoSize = false;
            this.lblOnOffInfo.Location = new System.Drawing.Point(45, 25);
            this.lblOnOffInfo.Size = new System.Drawing.Size(580, 60);
            this.lblOnOffInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblOnOffInfo.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.lblOnOffInfo.Text =
                LanguageManager.Get("OnOff.Info.Description") ??
                "Le module On/Off permet de programmer l'arrêt ou la mise en veille de votre machine à une heure précise.\n" +
                "Cette heure est également utilisée par MediaMonitor pour envoyer un rapport par email 10 minutes avant l'arrêt ou la mise en veille.\n" +
                "Si vous ne souhaitez pas envoyer de rapport, vous pouvez désactiver cette fonctionnalité dans l'interface de MediaMonitor.";

            this.pnlOnOffInfo.Controls.Add(this.picOnOffInfo);
            this.pnlOnOffInfo.Controls.Add(this.lblOnOffInfo);
            this.tabOnOff.Controls.Add(this.pnlOnOffInfo);

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

            this.grpShutdown.Text = LanguageManager.Get("Arrêt programmé") ?? "Arrêt programmé";
            this.grpShutdown.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpShutdown.Location = new System.Drawing.Point(15, 135);
            this.grpShutdown.Size = new System.Drawing.Size(640, 150);

            this.lblShutdownHour.Text = LanguageManager.Get("Heure (0–23) :") ?? "Heure (0–23) :";
            this.lblShutdownHour.Location = new System.Drawing.Point(20, 35);
            this.lblShutdownHour.Font = normalFont;

            this.numShutdownHour.Minimum = 0;
            this.numShutdownHour.Maximum = 23;
            this.numShutdownHour.Location = new System.Drawing.Point(150, 30);
            this.numShutdownHour.Width = 60;
            this.numShutdownHour.Font = normalFont;

            this.lblShutdownMinute.Text = LanguageManager.Get("Minute (0–59) :") ?? "Minute (0–59) :";
            this.lblShutdownMinute.Location = new System.Drawing.Point(230, 35);
            this.lblShutdownMinute.Font = normalFont;

            this.numShutdownMinute.Minimum = 0;
            this.numShutdownMinute.Maximum = 59;
            this.numShutdownMinute.Location = new System.Drawing.Point(350, 30);
            this.numShutdownMinute.Width = 60;
            this.numShutdownMinute.Font = normalFont;

            this.lblShutdownType.Text = LanguageManager.Get("Choisir le type d'arrêt :") ?? "Choisir le type d'arrêt :";
            this.lblShutdownType.Location = new System.Drawing.Point(20, 65);
            this.lblShutdownType.Size = new System.Drawing.Size(150, 25);
            this.lblShutdownType.Font = normalFont;

            this.cmbShutdownType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbShutdownType.Items.AddRange(new object[] { "Arrêt", "Veille" });
            this.cmbShutdownType.Location = new System.Drawing.Point(180, 65);
            this.cmbShutdownType.Size = new System.Drawing.Size(150, 25);
            this.cmbShutdownType.SelectedIndex = 0;
            this.cmbShutdownType.Font = normalFont;

            // ============================================================
            // Bouton Sauvegarder configuration On/Off
            // ============================================================
            this.btnSaveOnOff = new System.Windows.Forms.Button();
            this.btnSaveOnOff.Text = LanguageManager.Get("Sauvegarder") ?? "Sauvegarder";
            this.btnSaveOnOff.Font = normalFont;
            this.btnSaveOnOff.Location = new System.Drawing.Point(10, 100);
            this.btnSaveOnOff.Size = new System.Drawing.Size(200, 35);
            this.btnSaveOnOff.Click += new System.EventHandler(this.BtnSaveOnOff_Click);

            // Style gris clair moderne
            this.btnSaveOnOff.BackColor = Color.FromArgb(220, 220, 225);
            this.btnSaveOnOff.FlatStyle = FlatStyle.Flat;
            this.btnSaveOnOff.FlatAppearance.BorderSize = 1;
            this.btnSaveOnOff.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);

            this.grpShutdown.Controls.Add(this.btnSaveOnOff);


            // ============================================================
            // Bouton Créer tâche planifiée
            // ============================================================
            this.btnCreateShutdownTask.Text = LanguageManager.Get("Créer tâche planifiée") ?? "Créer tâche planifiée";
            this.btnCreateShutdownTask.Location = new System.Drawing.Point(220, 100);
            this.btnCreateShutdownTask.Size = new System.Drawing.Size(200, 35);
            this.btnCreateShutdownTask.Font = normalFont;
            this.btnCreateShutdownTask.Click += new System.EventHandler(this.BtnCreateShutdownTask_Click);

            // Style gris clair moderne
            this.btnCreateShutdownTask.BackColor = Color.FromArgb(220, 220, 225);
            this.btnCreateShutdownTask.FlatStyle = FlatStyle.Flat;
            this.btnCreateShutdownTask.FlatAppearance.BorderSize = 1;
            this.btnCreateShutdownTask.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // ============================================================
            // Bouton Supprimer tâche planifiée
            // ============================================================
            this.btnDeleteShutdownTask.Text = LanguageManager.Get("Supprimer tâche planifiée") ?? "Supprimer tâche planifiée";
            this.btnDeleteShutdownTask.Location = new System.Drawing.Point(430, 100);
            this.btnDeleteShutdownTask.Size = new System.Drawing.Size(200, 35);
            this.btnDeleteShutdownTask.Font = normalFont;
            this.btnDeleteShutdownTask.Click += new System.EventHandler(this.BtnDeleteShutdownTask_Click);

            // Style gris clair moderne
            this.btnDeleteShutdownTask.BackColor = Color.FromArgb(220, 220, 225);
            this.btnDeleteShutdownTask.FlatStyle = FlatStyle.Flat;
            this.btnDeleteShutdownTask.FlatAppearance.BorderSize = 1;
            this.btnDeleteShutdownTask.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 185);


            // Ajout des autres contrôles
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

            this.grpWOL.Text = LanguageManager.Get("Démarrage automatique (Wake On Lan)") ?? "Démarrage automatique (Wake On Lan)";
            this.grpWOL.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpWOL.Location = new System.Drawing.Point(15, 290);
            this.grpWOL.Size = new System.Drawing.Size(640, 120);

            this.lblWOLInfo.AutoSize = false;
            this.lblWOLInfo.Location = new System.Drawing.Point(20, 30);
            this.lblWOLInfo.Size = new System.Drawing.Size(600, 80);
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
            
            // AJOUTS
            this.pnlAboutScroll.Controls.Add(this.lblAbout);
            this.tabAbout.Controls.Add(this.pnlAboutScroll); 
                       
            // BOUTON : Ouvrir le dossier Logs
            this.btnOpenLogs = new System.Windows.Forms.Button();
            this.btnOpenLogs.Text = LanguageManager.Get("Dossier Logs") ?? "Dossier Logs";
            this.btnOpenLogs.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnOpenLogs.Size = new System.Drawing.Size(150, 35);
            this.btnOpenLogs.Location = new System.Drawing.Point(10, this.lblAbout.Bottom + 20);
            this.btnOpenLogs.BackColor = Color.FromArgb(60, 60, 60);
            this.btnOpenLogs.ForeColor = Color.White;
            this.btnOpenLogs.FlatStyle = FlatStyle.Flat;
            this.btnOpenLogs.FlatAppearance.BorderSize = 0;
            
            // Action du bouton
            this.btnOpenLogs.Click += (s, e) =>
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                if (!Directory.Exists(logFolder))
                    Directory.CreateDirectory(logFolder);

                try
                {
                    Process.Start("explorer.exe", logFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Impossible d’ouvrir le dossier Logs.\n\n" + ex.Message,
                        "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            // Ajout au panneau défilant
            this.pnlAboutScroll.Controls.Add(this.btnOpenLogs);
            // BOUTON : Purger les logs
            this.btnPurgeLogs = new System.Windows.Forms.Button();
            this.btnPurgeLogs.Text = LanguageManager.Get("Purger les logs") ?? "Purger les logs";
            this.btnPurgeLogs.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnPurgeLogs.Size = new System.Drawing.Size(150, 35);

            // Positionné juste à droite du bouton "Dossier Logs"
            this.btnPurgeLogs.Location = new System.Drawing.Point(
                this.btnOpenLogs.Right + 10,
                this.btnOpenLogs.Top
            );

            this.btnPurgeLogs.BackColor = Color.FromArgb(120, 40, 40);
            this.btnPurgeLogs.ForeColor = Color.White;
            this.btnPurgeLogs.FlatStyle = FlatStyle.Flat;
            this.btnPurgeLogs.FlatAppearance.BorderSize = 0;

            // Action du bouton
            this.btnPurgeLogs.Click += (s, e) =>
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                try
                {
                    if (Directory.Exists(logFolder))
                    {
                        foreach (var file in Directory.GetFiles(logFolder))
                            File.Delete(file);
                    }
                    else
                    {
                        Directory.CreateDirectory(logFolder);
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

            // Ajout au panneau défilant
            this.pnlAboutScroll.Controls.Add(this.btnPurgeLogs);

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

        private MCEMonitor.Controls.RoundedPanel pnlEmailInfo;
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
        private MCEMonitor.Controls.RoundedPanel pnlMediaInfo;
        private System.Windows.Forms.PictureBox picMediaInfo;
        private System.Windows.Forms.Label lblMediaInfo;
        private System.Windows.Forms.Timer logRefreshTimer;

        private System.Windows.Forms.GroupBox grpMediaActions;
        private MCEMonitor.Controls.Win11Toggle toggleMediaService;
        private System.Windows.Forms.Label lblMediaStatus;
        private System.Windows.Forms.Label lblNextReport;
        private System.Windows.Forms.Label lblLastReport;
        private System.Windows.Forms.Timer mediaServiceTimer;

        private System.Windows.Forms.Button btnCreateMediaTask2;
        private System.Windows.Forms.Button btnDeleteMediaTask2;
        private System.Windows.Forms.Button btnOpenMediaUI;

        private System.Windows.Forms.Label lblWakeTitle;
        private MCEMonitor.Controls.RoundedPanel pnlWakeInfo;
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

        private MCEMonitor.Controls.RoundedPanel pnlStopInfo;
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
        private MCEMonitor.Controls.RoundedPanel pnlOnOffInfo;
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
        private System.Windows.Forms.Button btnOpenLogs;
        private System.Windows.Forms.Button btnPurgeLogs; 
        
        private System.Windows.Forms.TabPage tabRomMonitor;
        private System.Windows.Forms.Label lblRomTitle;
        private MCEMonitor.Controls.RoundedPanel pnlRomInfo;
        private System.Windows.Forms.PictureBox picRomInfo;
        private System.Windows.Forms.Label lblRomDescription;
        private System.Windows.Forms.GroupBox grpRomActions;
        private MCEMonitor.Controls.Win11Toggle toggleRomService;
        private System.Windows.Forms.Label lblRomStatus;
        private System.Windows.Forms.GroupBox grpRomSettings;
        private System.Windows.Forms.Label lblRomInterval;
        private System.Windows.Forms.NumericUpDown numRomInterval;
        private System.Windows.Forms.Label lblRomWarnPct;
        private System.Windows.Forms.NumericUpDown numRomWarnPct;
        private System.Windows.Forms.Label lblRomCritPct;
        private System.Windows.Forms.NumericUpDown numRomCritPct;
        private System.Windows.Forms.Label lblRomWarnGo;
        private System.Windows.Forms.NumericUpDown numRomWarnGo;
        private System.Windows.Forms.Label lblRomCritGo;
        private System.Windows.Forms.NumericUpDown numRomCritGo;
        private System.Windows.Forms.Label lblRomCooldown;
        private System.Windows.Forms.NumericUpDown numRomCooldown;
        private System.Windows.Forms.CheckBox chkRomSmartAlert;
        private System.Windows.Forms.Label lblRomHint;
        private System.Windows.Forms.Button btnCreateRomTask;
        private System.Windows.Forms.Button btnDeleteRomTask;
        private System.Windows.Forms.Button btnOpenRomUI;
        private System.Windows.Forms.Button btnSaveRomConfig;
        private System.Windows.Forms.Timer romMonitorTimer;                               
    }
}

