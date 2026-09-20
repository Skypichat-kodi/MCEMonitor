using System;
using System.Drawing;
using System.Windows.Forms;

namespace Autotrad
{
    partial class FormMain : Form
    {
        private System.ComponentModel.IContainer components = null;

        // ============================================================
        //  COULEURS (thème Win11 Dark)
        // ============================================================
        private static readonly Color C_Bg        = Color.FromArgb(30, 30, 30);    // fond form
        private static readonly Color C_Bar       = Color.FromArgb(45, 45, 48);    // panneaux
        private static readonly Color C_Input     = Color.FromArgb(37, 37, 38);    // zone texte
        private static readonly Color C_Border    = Color.FromArgb(60, 60, 60);    // bordures
        private static readonly Color C_Text      = Color.FromArgb(229, 229, 229); // texte
        private static readonly Color C_TextDim   = Color.FromArgb(180, 180, 180); // texte secondaire
        private static readonly Color C_Accent    = Color.FromArgb(76, 194, 255);  // bleu électrique
        private static readonly Color C_Hover     = Color.FromArgb(58, 58, 61);    // hover

        // ============================================================
        //  CONTRÔLES
        // ============================================================

        // Barre du haut (actions)
        private Panel toolbarPanel;
        private Button btnOuvrirFichier;
        private Button btnOuvrirDossier;
        private Label lblLangFolder;
        private Button btnChangeLangFolder;
        private Label lblLang;
        private ComboBox cmbLang;

        // Barre API
        private Panel apiPanel;
        private Label lblApiKey;
        private TextBox txtApiKey;
        private Button btnSaveApiKey;
        private LinkLabel lnkApiKey;

        // Tableau principal
        private DataGridView dataGridView1;

        // Aperçu
        private Panel previewPanel;
        private Label lblPreview;
        private TextBox txtPreview;

        // Barre du bas
        private Panel statusPanel;
        private Label lblStatus;
        private Button btnApply;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();

            base.Dispose(disposing);
        }

        // ============================================================
        //  CRÉATION D'UN BOUTON STYLE FLAT DARK
        // ============================================================
        private Button MakeButton(string text, int width = 0, int height = 32)
        {
            var btn = new Button
            {
                Text = text,
                Height = height,
                BackColor = C_Bar,
                ForeColor = C_Text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand,
                Padding = new Padding(0)
            };

            if (width > 0)
                btn.Width = width;
            else
                btn.AutoSize = true;

            btn.FlatAppearance.BorderColor = C_Border;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = C_Hover;
            btn.FlatAppearance.MouseDownBackColor = C_Accent;

            return btn;
        }

        // ============================================================
        //  CRÉATION D'UN TEXTBOX STYLE DARK
        // ============================================================
        private TextBox MakeTextBox(int width = 200)
        {
            return new TextBox
            {
                Width = width,
                BackColor = C_Input,
                ForeColor = C_Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F)
            };
        }

        // ============================================================
        //  INITIALISATION
        // ============================================================
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            // ========================================================
            //  FORM
            // ========================================================
            this.ClientSize = new Size(1300, 750);
            this.MinimumSize = new Size(1000, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = C_Bg;
            this.ForeColor = C_Text;
            this.Font = new Font("Segoe UI", 9F);
            this.Text = "Autotrad - Scanner de traduction";

            // ========================================================
            //  BARRE OUTILS
            // ========================================================
            this.toolbarPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = C_Bar,
                Padding = new Padding(12, 12, 12, 12)
            };

            this.btnOuvrirFichier = MakeButton("Ouvrir un fichier", 150);
            this.btnOuvrirFichier.Location = new Point(12, 12);
            this.btnOuvrirFichier.Click += new EventHandler(this.OuvrirFichier_Click);

            this.btnOuvrirDossier = MakeButton("Ouvrir un dossier", 150);
            this.btnOuvrirDossier.Location = new Point(170, 12);
            this.btnOuvrirDossier.Click += new EventHandler(this.OuvrirDossier_Click);

            this.lblLangFolder = new Label
            {
                AutoSize = true,
                ForeColor = C_TextDim,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(340, 20),
                Text = "Dossier langues : (non défini)"
            };

            this.btnChangeLangFolder = MakeButton("Changer", 80);
            this.btnChangeLangFolder.Location = new Point(720, 12);
            this.btnChangeLangFolder.Click += new EventHandler(this.ChoisirDossierLangues_Click);

            this.lblLang = new Label
            {
                AutoSize = true,
                ForeColor = C_TextDim,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(830, 20),
                Text = "Langue :"
            };

            this.cmbLang = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 180,
                Height = 32,
                BackColor = C_Input,
                ForeColor = C_Text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F)
            };
            this.cmbLang.Items.AddRange(new object[]
            {
                "Français (fr-FR)",
                "Anglais (en-GB)",
                "Allemand (de-DE)",
                "Espagnol (es-ES)"
            });
            this.cmbLang.SelectedIndex = 0;
            this.cmbLang.SelectedIndexChanged += new EventHandler(this.cmbLang_SelectedIndexChanged);

            this.toolbarPanel.Controls.Add(this.btnOuvrirFichier);
            this.toolbarPanel.Controls.Add(this.btnOuvrirDossier);
            this.toolbarPanel.Controls.Add(this.lblLangFolder);
            this.toolbarPanel.Controls.Add(this.btnChangeLangFolder);
            this.toolbarPanel.Controls.Add(this.lblLang);
            this.toolbarPanel.Controls.Add(this.cmbLang);

            // ========================================================
            //  BARRE API
            // ========================================================
            this.apiPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,                                       // ? 36 ? 52
                BackColor = Color.FromArgb(40, 40, 43),
                Padding = new Padding(12, 12, 12, 12)
            };

            this.lblApiKey = new Label
            {
                AutoSize = true,
                ForeColor = C_TextDim,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(12, 18),
                Text = "Clé API :"
            };

            this.lnkApiKey = new LinkLabel
            {
                Text = "Obtenir une clé",
                AutoSize = true,
                Location = new Point(80, 32),
                Font = new Font("Segoe UI", 8F, FontStyle.Underline),
                LinkColor = C_Accent,
                ActiveLinkColor = Color.White,
                VisitedLinkColor = C_Accent,
                Cursor = Cursors.Hand
            };

            this.lnkApiKey.LinkClicked += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://translateapi.ai/",
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            
            this.txtApiKey = new TextBox
            {
                Location = new Point(80, 14),
                Width = 900,
                BackColor = C_Input,
                ForeColor = C_Text,
                BorderStyle = BorderStyle.FixedSingle,
                PasswordChar = '*',
                Font = new Font("Segoe UI", 9F)
            };

            this.btnSaveApiKey = MakeButton("Sauvegarder la clé", 160);
            this.btnSaveApiKey.Location = new Point(1000, 12);
            this.btnSaveApiKey.Click += new EventHandler(this.btnSaveApiKey_Click);

            this.apiPanel.Controls.Add(this.lblApiKey);
            this.apiPanel.Controls.Add(this.txtApiKey);
            this.apiPanel.Controls.Add(this.btnSaveApiKey);
            this.apiPanel.Controls.Add(this.lnkApiKey);

            // ========================================================
            //  DATAGRID
            // ========================================================
            this.dataGridView1 = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = C_Bg,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32,
                EnableHeadersVisualStyles = false,
                GridColor = C_Border,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnEnter,
                ReadOnly = false
            };

            // Style des cellules
            this.dataGridView1.DefaultCellStyle.BackColor = Color.FromArgb(37, 37, 38);
            this.dataGridView1.DefaultCellStyle.ForeColor = C_Text;
            this.dataGridView1.DefaultCellStyle.SelectionBackColor = C_Accent;
            this.dataGridView1.DefaultCellStyle.SelectionForeColor = Color.Black;
            this.dataGridView1.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
            this.dataGridView1.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);

            // Lignes alternées
            this.dataGridView1.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(32, 32, 34);

            // En-têtes
            this.dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 45, 48);
            this.dataGridView1.ColumnHeadersDefaultCellStyle.ForeColor = C_Text;
            this.dataGridView1.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.dataGridView1.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(45, 45, 48);

            // Événements
            this.dataGridView1.CellDoubleClick += new DataGridViewCellEventHandler(this.dataGridView1_CellDoubleClick);
            this.dataGridView1.RowPrePaint += new DataGridViewRowPrePaintEventHandler(this.dataGridView1_RowPrePaint);

            // ========================================================
            //  APERÇU
            // ========================================================
            this.previewPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 140,
                BackColor = C_Bar,
                Padding = new Padding(12, 8, 12, 12)
            };

            this.lblPreview = new Label
            {
                AutoSize = true,
                ForeColor = C_TextDim,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(12, 8),
                Text = "Aperçu :"
            };

            this.txtPreview = new TextBox
            {
                Location = new Point(12, 30),
                Width = 1260,
                Height = 96,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = C_Input,
                ForeColor = C_Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5F),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            this.previewPanel.Controls.Add(this.lblPreview);
            this.previewPanel.Controls.Add(this.txtPreview);

            // ========================================================
            //  BARRE DE STATUT (bas)
            // ========================================================
            this.statusPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = C_Bar,
                Padding = new Padding(12, 6, 12, 6)
            };

            this.lblStatus = new Label
            {
                AutoSize = true,
                ForeColor = C_TextDim,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(12, 12),
                Text = "Prêt."
            };

            this.btnApply = MakeButton("Appliquer", 140, 28);
            this.btnApply.Location = new Point(1140, 6);
            this.btnApply.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.btnApply.BackColor = C_Accent;
            this.btnApply.ForeColor = Color.Black;
            this.btnApply.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.btnApply.FlatAppearance.MouseOverBackColor = Color.FromArgb(93, 210, 255);
            this.btnApply.Click += new EventHandler(this.btnApply_Click);

            this.statusPanel.Controls.Add(this.lblStatus);
            this.statusPanel.Controls.Add(this.btnApply);

            // ========================================================
            //  AJOUT DES CONTRÔLES (ordre = empilement Dock)
            // ========================================================
            // Pour Dock : le DERNIER ajouté est le plus proche du bord.
            this.Controls.Add(this.dataGridView1);   // Fill
            this.Controls.Add(this.previewPanel);    // Bottom (au-dessus du status)
            this.Controls.Add(this.statusPanel);     // Bottom (tout en bas)
            this.Controls.Add(this.apiPanel);        // Top (sous la toolbar)
            this.Controls.Add(this.toolbarPanel);    // Top (tout en haut)
        }
    }
}