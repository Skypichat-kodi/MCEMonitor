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
        private ContextMenuStrip menuFichier;

        private DataGridView dataGridView1;
        private Button btnExport;

        // ?? AJOUT
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
            this.menuFichier = new ContextMenuStrip(this.components);

            this.dataGridView1 = new DataGridView();
            this.btnExport = new Button();

            // ?? AJOUT
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
            this.btnChangeLangFolder.Click += new EventHandler(this.ChoisirDossierLangues_Click);
            this.btnChangeLangFolder.Location = new System.Drawing.Point(500, 4);

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

            this.topPanel.Controls.Add(this.btnMenuFichier);
            this.topPanel.Controls.Add(this.lblLangFolder);
            this.topPanel.Controls.Add(this.btnChangeLangFolder);

            // ------------------------------
            // DATAGRID
            // ------------------------------
            this.dataGridView1.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Bottom |
                AnchorStyles.Left |
                AnchorStyles.Right;

            this.dataGridView1.Location = new System.Drawing.Point(12, 50);
            this.dataGridView1.Size = new System.Drawing.Size(760, 300);
            this.dataGridView1.AllowUserToResizeColumns = true;
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AutoGenerateColumns = false;
            this.dataGridView1.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            this.dataGridView1.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

            this.dataGridView1.CellDoubleClick += new DataGridViewCellEventHandler(this.dataGridView1_CellDoubleClick);
            this.dataGridView1.RowPrePaint += new DataGridViewRowPrePaintEventHandler(this.dataGridView1_RowPrePaint);

            // ------------------------------
            // ?? TEXTBOX PREVIEW
            // ------------------------------
            this.txtPreview.Multiline = true;
            this.txtPreview.ReadOnly = true;
            this.txtPreview.ScrollBars = ScrollBars.Vertical;
            this.txtPreview.Anchor =
                AnchorStyles.Left |
                AnchorStyles.Right |
                AnchorStyles.Bottom;

            this.txtPreview.Location = new System.Drawing.Point(12, 355);
            this.txtPreview.Size = new System.Drawing.Size(760, 70);

            // ------------------------------
            // BOUTON EXPORT
            // ------------------------------
            this.btnExport.Text = "Exporter la sélection";
            this.btnExport.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.btnExport.Location = new System.Drawing.Point(600, 430);
            this.btnExport.Size = new System.Drawing.Size(160, 32);

            this.btnExport.BackColor = System.Drawing.Color.FromArgb(0, 160, 0);
            this.btnExport.ForeColor = System.Drawing.Color.White;
            this.btnExport.FlatStyle = FlatStyle.Flat;
            this.btnExport.FlatAppearance.BorderSize = 0;

            this.btnExport.Click += new EventHandler(this.btnExport_Click);

            // ------------------------------
            // FORM
            // ------------------------------
            this.ClientSize = new System.Drawing.Size(784, 461);
            this.Controls.Add(this.dataGridView1);
            this.Controls.Add(this.txtPreview);   // ?? AJOUT
            this.Controls.Add(this.btnExport);
            this.Controls.Add(this.topPanel);
            this.Text = "Autotrad - Scanner de traduction";
        }
    }
}

