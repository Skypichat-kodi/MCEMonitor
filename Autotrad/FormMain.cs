using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace Autotrad
{
    public partial class FormMain : Form
    {
        private string _lastOpenedFile = "";
        private Dictionary<string, string> _existingKeys = new();
        private CheckBox _headerCheckBox;

        public FormMain()
        {
            InitializeComponent();
        }

        // ------------------------------
        // Ouvrir un fichier
        // ------------------------------
private void OuvrirFichier_Click(object sender, EventArgs e)
{
    using var dlg = new OpenFileDialog();
    dlg.Filter = "Fichiers C# (*.cs)|*.cs";

    if (dlg.ShowDialog() == DialogResult.OK)
    {
        _lastOpenedFile = dlg.FileName;

        LoadExistingJsonKeys();

        var list = Scanner.ScanFile(dlg.FileName, _existingKeys);
        foreach (var item in list)
            item.FilePath = dlg.FileName;

        // 1?? Colonnes d’abord
        SetupColumns(isFolderMode: false);

        // 2?? Puis la source
        dataGridView1.DataSource = list;

        FillPreviewColumn();
    }
}


        // ------------------------------
        // Ouvrir un dossier
        // ------------------------------
private void OuvrirDossier_Click(object sender, EventArgs e)
{
    using var dlg = new FolderBrowserDialog();

    if (dlg.ShowDialog() == DialogResult.OK)
    {
        string folder = dlg.SelectedPath;

        LoadExistingJsonKeys();

        var allResults = new List<ScanResult>();

        var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                             .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                      || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var list = Scanner.ScanFile(file, _existingKeys);

            foreach (var item in list)
                item.FilePath = file;

            allResults.AddRange(list);
        }

        // 1?? Colonnes d’abord
        SetupColumns(isFolderMode: true);

        // 2?? Puis la source
        dataGridView1.DataSource = allResults
            .OrderBy(r => r.FileName)
            .ThenBy(r => r.LineNumber)
            .ToList();

        FillPreviewColumn();
    }
}

private void LoadExistingJsonKeys()
{
    string langDir = Path.Combine(AppContext.BaseDirectory, "languages");
    string frPath = Path.Combine(langDir, "fr-FR.json");

    Directory.CreateDirectory(langDir);

    // 1?? Si le fichier n'existe pas ou est vide ? on crée un JSON vide
    if (!File.Exists(frPath) || new FileInfo(frPath).Length == 0)
    {
        _existingKeys = new Dictionary<string, string>();
        File.WriteAllText(frPath, "{}");
        return;
    }

    // 2?? S'il existe et contient quelque chose ? on tente de le lire
    try
    {
        var json = File.ReadAllText(frPath);
        _existingKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                         ?? new Dictionary<string, string>();
    }
    catch (JsonException)
    {
        // 3?? Fichier corrompu / pas du JSON valide ? on repart propre
        _existingKeys = new Dictionary<string, string>();
        File.WriteAllText(frPath, "{}");
    }
}


        // ------------------------------
        // Colonnes dynamiques
        // ------------------------------
private void SetupColumns(bool isFolderMode)
{
    dataGridView1.AutoGenerateColumns = false;
    dataGridView1.Columns.Clear();

    // Colonne fichier (mode dossier uniquement)
    if (isFolderMode)
    {
        dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "FileName",
            HeaderText = "Fichier",
            Width = 150,
            ReadOnly = true
        });
    }

    // Ligne
    dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
    {
        DataPropertyName = "LineNumber",
        HeaderText = "Ligne",
        Width = 60,
        ReadOnly = true
    });

    // Aperçu
    var colPreview = new DataGridViewTextBoxColumn
    {
        HeaderText = "Aperçu",
        Name = "Preview",
        Width = 200,
        ReadOnly = true,
        DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
    };
    dataGridView1.Columns.Add(colPreview);

    // ?? Texte détecté ? colonne qui s’étire
    var colText = new DataGridViewTextBoxColumn
    {
        DataPropertyName = "Text",
        HeaderText = "Texte détecté",
        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        ReadOnly = true,
        DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
    };
    dataGridView1.Columns.Add(colText);

    // Traduire ?
    var colSelect = new DataGridViewCheckBoxColumn
    {
        DataPropertyName = "Selected",
        HeaderText = "Traduire ?",
        Name = "Selected",
        Width = 80
    };
    dataGridView1.Columns.Add(colSelect);

    AddHeaderCheckBox();
}

        // ------------------------------
        // Case à cocher dans l’en-tête
        // ------------------------------
        private void AddHeaderCheckBox()
        {
            _headerCheckBox = new CheckBox();
            _headerCheckBox.Size = new Size(15, 15);
            _headerCheckBox.BackColor = Color.Transparent;

            _headerCheckBox.CheckedChanged += (s, e) =>
            {
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    if (row.DataBoundItem is ScanResult item)
                        item.Selected = _headerCheckBox.Checked;
                }
                dataGridView1.Refresh();
            };

            dataGridView1.Controls.Add(_headerCheckBox);
            dataGridView1.ColumnWidthChanged += (s, e) => PositionHeaderCheckBox();
            dataGridView1.Scroll += (s, e) => PositionHeaderCheckBox();

            PositionHeaderCheckBox();
        }

        private void PositionHeaderCheckBox()
        {
            var col = dataGridView1.Columns["Selected"];
            if (col == null) return;

            Rectangle rect = dataGridView1.GetCellDisplayRectangle(col.Index, -1, true);
            _headerCheckBox.Location = new Point(rect.X + (rect.Width - _headerCheckBox.Width) / 2, rect.Y + 3);
        }

        private void FillPreviewColumn()
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.DataBoundItem is ScanResult item)
                {
                    row.Cells["Preview"].Value = item.Preview;
                }
            }
        }

        // ------------------------------
        // Coloration
        // ------------------------------
        private void dataGridView1_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            var row = dataGridView1.Rows[e.RowIndex];
            if (row.DataBoundItem is ScanResult item)
            {
                if (item.IsMismatch)
                {
                    row.DefaultCellStyle.BackColor = Color.LightPink;
                    return;
                }

                if (item.IsTranslated)
                {
                    row.DefaultCellStyle.BackColor = Color.LightGreen;
                }
            }
        }

        // ------------------------------
        // Double-clic
        // ------------------------------
        private void dataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            if (dataGridView1.Rows[e.RowIndex].DataBoundItem is not ScanResult item)
                return;

            string file = item.FilePath;
            int line = item.LineNumber;

            if (TryOpen("code", $"\"{file}\" -g {line}"))
                return;

            if (TryOpen("notepad++", $"\"{file}\" -n{line}"))
                return;

            TryOpen("notepad", $"\"{file}\"");
        }

        private bool TryOpen(string exe, string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------
        // Export
        // ------------------------------
        private void btnExport_Click(object sender, EventArgs e)
        {
            if (dataGridView1.DataSource is not List<ScanResult> list)
                return;

            string langDir = Path.Combine(AppContext.BaseDirectory, "languages");
            Directory.CreateDirectory(langDir);

            string frPath = Path.Combine(langDir, "fr-FR.json");
            string enPath = Path.Combine(langDir, "en-GB.json");

            foreach (var item in list.Where(x => x.Selected))
            {
                string module = Utils.GetModuleFromFilename(item.FilePath);

                if (!item.IsTranslated)
                {
                    string key = Utils.GenerateKeyFromText(module, item.Text);

                    string left = Utils.ExtractLeftPart(item.FullLine);
                    string newLine =
                        $"{left}LanguageManager.Get(\"{key}\") ?? \"{item.Text}\";";

                    Utils.ReplaceLineInFile(item.FilePath, item.LineNumber, newLine);

                    Utils.AddToJson(frPath, key, item.Text);
                    Utils.AddToJson(enPath, key, "");
                }
                else
                {
                    string key = Utils.ExtractKeyFromLine(item.FullLine);
                    string fallback = Utils.ExtractFallbackText(item.FullLine);

                    Utils.AddToJson(frPath, key, fallback);
                    Utils.AddToJson(enPath, key, "");
                }
            }

            MessageBox.Show("Traductions appliquées.");
        }
private void RefreshView()
{
    if (string.IsNullOrEmpty(_lastOpenedFile))
        return;

    // Mode fichier
    if (File.Exists(_lastOpenedFile))
    {
        OuvrirFichier_Click(null, null);
        return;
    }

    // Mode dossier
    string? folder = Path.GetDirectoryName(_lastOpenedFile);
    if (folder != null && Directory.Exists(folder))
    {
        OuvrirDossier_Click(null, null);
    }
}
        
    }
}

