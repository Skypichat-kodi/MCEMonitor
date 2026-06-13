using System;
using System.Windows.Forms;

namespace Autotrad
{
    partial class FormMain : Form
    {
        private System.ComponentModel.IContainer components = null;

        private MenuStrip menuStrip1;
        private ToolStripMenuItem fichierToolStripMenuItem;
        private ToolStripMenuItem ouvrirToolStripMenuItem;
        private DataGridView dataGridView1;
        private Button btnExport;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

private void InitializeComponent()
{
    this.menuStrip1 = new MenuStrip();
    this.fichierToolStripMenuItem = new ToolStripMenuItem();
    this.ouvrirToolStripMenuItem = new ToolStripMenuItem();
    this.dataGridView1 = new DataGridView();
    this.btnExport = new Button();

    // MENU
    this.menuStrip1.Items.AddRange(new ToolStripItem[] {
        this.fichierToolStripMenuItem
    });

    this.fichierToolStripMenuItem.Text = "Fichier";

    this.ouvrirToolStripMenuItem.Text = "Ouvrir";

    var ouvrirFichier = new ToolStripMenuItem();
    ouvrirFichier.Text = "Ouvrir un fichier…";
    ouvrirFichier.Click += new EventHandler(this.OuvrirFichier_Click);

    var ouvrirDossier = new ToolStripMenuItem();
    ouvrirDossier.Text = "Ouvrir un dossier…";
    ouvrirDossier.Click += new EventHandler(this.OuvrirDossier_Click);

    this.ouvrirToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
        ouvrirFichier,
        ouvrirDossier
    });

    this.fichierToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
        this.ouvrirToolStripMenuItem
    });

    // DATAGRID
    this.dataGridView1.Anchor =
        AnchorStyles.Top |
        AnchorStyles.Bottom |
        AnchorStyles.Left |
        AnchorStyles.Right;

    this.dataGridView1.Location = new System.Drawing.Point(12, 40);
    this.dataGridView1.Size = new System.Drawing.Size(760, 380);

    this.dataGridView1.AllowUserToResizeColumns = true;

    // ?? important : pas de ligne vide
    this.dataGridView1.AllowUserToAddRows = false;

    // ?? important : on garde NOS colonnes, pas celles auto-générées
    this.dataGridView1.AutoGenerateColumns = false;

    // Affichage multi-ligne
    this.dataGridView1.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
    this.dataGridView1.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

    // Colonnes
    this.dataGridView1.Columns.Clear();

    var colFile = new DataGridViewTextBoxColumn();
    colFile.HeaderText = "Fichier";
    colFile.Width = 140;
    colFile.ReadOnly = true;
    // colFile.DataPropertyName = "File"; // adapte au nom de ta propriété

    var colLine = new DataGridViewTextBoxColumn();
    colLine.HeaderText = "Ligne";
    colLine.Width = 60;
    colLine.ReadOnly = true;
    // colLine.DataPropertyName = "LineNumber";

    var colPreview = new DataGridViewTextBoxColumn();
    colPreview.HeaderText = "Aperçu";
    colPreview.Width = 200;
    colPreview.ReadOnly = true;
    // colPreview.DataPropertyName = "Preview";

    var colText = new DataGridViewTextBoxColumn();
    colText.HeaderText = "Texte détecté";
    colText.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    colText.ReadOnly = true;
    colText.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
    // colText.DataPropertyName = "Text";

    var colTranslate = new DataGridViewCheckBoxColumn();
    colTranslate.HeaderText = "Traduire ?";
    colTranslate.Width = 80;
    // colTranslate.DataPropertyName = "Selected";

    this.dataGridView1.Columns.AddRange(new DataGridViewColumn[] {
        colFile, colLine, colPreview, colText, colTranslate
    });

    this.dataGridView1.CellDoubleClick += new DataGridViewCellEventHandler(this.dataGridView1_CellDoubleClick);
    this.dataGridView1.RowPrePaint += new DataGridViewRowPrePaintEventHandler(this.dataGridView1_RowPrePaint);

    // BOUTON EXPORT
    this.btnExport.Text = "Exporter la sélection";
    this.btnExport.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
    this.btnExport.Location = new System.Drawing.Point(600, 430);
    this.btnExport.Click += new EventHandler(this.btnExport_Click);

    // FORM
    this.ClientSize = new System.Drawing.Size(784, 461);
    this.Controls.Add(this.dataGridView1);
    this.Controls.Add(this.btnExport);
    this.Controls.Add(this.menuStrip1);
    this.MainMenuStrip = this.menuStrip1;
    this.Text = "Autotrad - Scanner de traduction";
}

    }
}

