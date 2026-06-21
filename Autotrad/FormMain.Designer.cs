using System;
using System.Windows.Forms;

namespace Autotrad
{
    partial class FormMain : Form
    {
        private System.ComponentModel.IContainer components = null;

        private Panel topPanel;
        private Button btnMenuFichier;
        private Label lblLangFolder;
        private Button btnChangeLangFolder;
        private ComboBox cmbLang;
        private ContextMenuStrip menuFichier;
        private Button btnApply;
        private DataGridView dataGridView1;
        private TextBox txtPreview;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.topPanel = new Panel();
            this.btnMenuFichier = new Button();
            this.lblLangFolder = new Label();
            this.btnChangeLangFolder = new Button();
            this.cmbLang = new ComboBox();
            this.menuFichier = new ContextMenuStrip(this.components);
            this.btnApply = new Button();
            this.dataGridView1 = new DataGridView();
            this.txtPreview = new TextBox();

            // ------------------------------
            // PANEL DU HAUT
            // ------------------------------
            this.topPanel.Dock = DockStyle.Top;
            this.topPanel.Height = 36;
            this.topPanel.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.topPanel.Padding = new Padding(8, 6, 8, 6);

            // ------------------------------
            // BOUTON FICHIER
            // ------------------------------
            this.btnMenuFichier.Text = "Fichier ?";
            this.btnMenuFichier.AutoSize = true;
            this.btnMenuFichier.Location = new System.Drawing.Point(8, 4);
            this.btnMenuFichier.Click += (s, e) =>
            {
                menuFichier.Show(btnMenuFichier, 0, btnMenuFichier.Height);
            };

            // ------------------------------
            // LABEL DOSSIER LANGUES
            // ------------------------------
            this.lblLangFolder.AutoSize = true;
            this.lblLangFolder.Location = new System.Drawing.Point(120, 9);
            this.lblLangFolder.Text = "Dossier langues : (non défini)";

            // ------------------------------
            // BOUTON CHANGER
            // ------------------------------
            this.btnChangeLangFolder.Text = "Changer";
            this.btnChangeLangFolder.AutoSize = true;
            this.btnChangeLangFolder.Location = new System.Drawing.Point(500, 4);
            this.btnChangeLangFolder.Click += new EventHandler(this.ChoisirDossierLangues_Click);

            // ------------------------------
            // COMBOBOX LANGUE
            // ------------------------------
            this.cmbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbLang.Items.AddRange(new object[]
            {
                "Français (fr-FR)",
                "Anglais (en-GB)",
                "Allemand (de-DE)",
                "Espagnol (es-ES)"
            });
            this.cmbLang.SelectedIndex = 0;
            this.cmbLang.Width = 200;

            // Position FIXE + ancrage à droite
            this.cmbLang.Location = new System.Drawing.Point(1050, 4);
            this.cmbLang.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            this.cmbLang.SelectedIndexChanged += new EventHandler(this.cmbLang_SelectedIndexChanged);

            // ------------------------------
            // MENU FICHIER
            // ------------------------------
            var ouvrirFichier = new ToolStripMenuItem("Ouvrir un fichier…");
            ouvrirFichier.Click += new EventHandler(this.OuvrirFichier_Click);

            var ouvrirDossier = new ToolStripMenuItem("Ouvrir un dossier…");
            ouvrirDossier.Click += new EventHandler(this.OuvrirDossier_Click);

            var choisirLangFolder = new ToolStripMenuItem("Choisir dossier langues…");
            choisirLangFolder.Click += new EventHandler(this.ChoisirDossierLangues_Click);

            this.menuFichier.Items.AddRange(new ToolStripItem[]
            {
                ouvrirFichier,
                ouvrirDossier,
                choisirLangFolder
            });

            // Ajout des contrôles au panel
            this.topPanel.Controls.Add(this.btnMenuFichier);
            this.topPanel.Controls.Add(this.lblLangFolder);
            this.topPanel.Controls.Add(this.btnChangeLangFolder);
            this.topPanel.Controls.Add(this.cmbLang);

            // ------------------------------
            // DATAGRID
            // ------------------------------
            this.dataGridView1.Anchor =
                AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            this.dataGridView1.Location = new System.Drawing.Point(12, 50);
            this.dataGridView1.Size = new System.Drawing.Size(1276, 280);
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AutoGenerateColumns = false;
            this.dataGridView1.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

            this.dataGridView1.CellDoubleClick += new DataGridViewCellEventHandler(this.dataGridView1_CellDoubleClick);
            this.dataGridView1.RowPrePaint += new DataGridViewRowPrePaintEventHandler(this.dataGridView1_RowPrePaint);

            // ------------------------------
            // PREVIEW
            // ------------------------------
            this.txtPreview.Multiline = true;
            this.txtPreview.ReadOnly = true;
            this.txtPreview.ScrollBars = ScrollBars.Vertical;
            this.txtPreview.Anchor =
                AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            this.txtPreview.Location = new System.Drawing.Point(12, 335);
            this.txtPreview.Size = new System.Drawing.Size(1276, 60);

            // ------------------------------
            // BOUTON APPLIQUER
            // ------------------------------
            this.btnApply.Text = "Appliquer";
            this.btnApply.Width = 120;
            this.btnApply.Height = 32;
            this.btnApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.btnApply.Location = new System.Drawing.Point(
                1300 - 120 - 20,
                461 - 32 - 12
            );
            this.btnApply.Click += new EventHandler(this.btnApply_Click);

            // ------------------------------
            // FORM
            // ------------------------------
            this.ClientSize = new System.Drawing.Size(1300, 461);
            this.Controls.Add(this.dataGridView1);
            this.Controls.Add(this.txtPreview);
            this.Controls.Add(this.topPanel);
            this.Controls.Add(this.btnApply);
            this.Text = "Autotrad - Scanner de traduction";
        }
    }
}

