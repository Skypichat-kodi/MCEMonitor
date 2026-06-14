using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Autotrad
{
    public partial class FormMain : Form
    {
        private string _lastOpenedFile = "";
        private Dictionary<string, string> _existingKeys = new();
        private CheckBox _headerCheckBox;

        private string _langFolder = "";
        private string ConfigPath => Path.Combine(AppContext.BaseDirectory, "autotrad.config.json");

        public FormMain()
        {
            InitializeComponent();
            LoadConfig();

            // ---------------------------------------------------------
            // ?? DataGridView 100% stable : jamais en mode édition
            // ---------------------------------------------------------
            dataGridView1.ReadOnly = true;
            dataGridView1.EditMode = DataGridViewEditMode.EditProgrammatically;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.CellSelect;

            // ---------------------------------------------------------
            // ?? TextBox de prévisualisation (sélection de texte)
            // ---------------------------------------------------------
            txtPreview.Multiline = true;
            txtPreview.ReadOnly = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;

            // Quand on change de cellule ? on affiche le texte dans txtPreview
            dataGridView1.SelectionChanged += dataGridView1_SelectionChanged;
            
            // ?? AJOUT : gérer le clic sur les checkbox
            dataGridView1.CellContentClick += dataGridView1_CellContentClick;            
        }

        // ---------------------------------------------------------
        // ?? Prévisualisation du texte sélectionné
        // ---------------------------------------------------------
        private void dataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentCell != null)
                txtPreview.Text = dataGridView1.CurrentCell.Value?.ToString();
        }
        // ------------------------------
        // OUVRIR FICHIER
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

                // DataGridView toujours en lecture seule ? aucun crash
                SetupColumns(false);
                dataGridView1.DataSource = list;

                FillPreviewColumn();
            }
        }

        // ------------------------------
        // OUVRIR DOSSIER
        // ------------------------------
        private void OuvrirDossier_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                ChargerDossier(dlg.SelectedPath);
            }
        }

        // ------------------------------
        // CHARGER DOSSIER
        // ------------------------------
        private void ChargerDossier(string folder)
        {
            _lastOpenedFile = folder;
            LoadExistingJsonKeys();

            var allResults = new List<ScanResult>();

            var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                    (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                    && !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)
                    && !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
                );

            foreach (var file in files)
            {
                var list = Scanner.ScanFile(file, _existingKeys);
                allResults.AddRange(list);
            }

            SetupColumns(true);
            dataGridView1.DataSource = allResults
                .OrderBy(r => r.FileName)
                .ThenBy(r => r.LineNumber)
                .ToList();

            FillPreviewColumn();
        }

        // ------------------------------
        // CHARGER JSON
        // ------------------------------
        private void LoadExistingJsonKeys()
        {
            string frPath = Path.Combine(_langFolder, "fr-FR.json");

            Directory.CreateDirectory(_langFolder);

            if (!File.Exists(frPath) || new FileInfo(frPath).Length == 0)
            {
                _existingKeys = new Dictionary<string, string>();
                File.WriteAllText(frPath, "{}");
                return;
            }

            try
            {
                var json = File.ReadAllText(frPath);
                _existingKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                                 ?? new Dictionary<string, string>();
            }
            catch
            {
                _existingKeys = new Dictionary<string, string>();
                File.WriteAllText(frPath, "{}");
            }
        }

        // ------------------------------
        // COLONNES
        // ------------------------------
        private void SetupColumns(bool isFolderMode)
        {
            dataGridView1.AutoGenerateColumns = false;
            dataGridView1.Columns.Clear();

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

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "LineNumber",
                HeaderText = "Ligne",
                Width = 60,
                ReadOnly = true
            });

            var colPreview = new DataGridViewTextBoxColumn
            {
                HeaderText = "Aperçu",
                Name = "Preview",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50,
                ReadOnly = true,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
            };
            dataGridView1.Columns.Add(colPreview);

            var colText = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Text",
                HeaderText = "Texte détecté",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50,
                ReadOnly = true,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
            };
            dataGridView1.Columns.Add(colText);

            var colSelect = new DataGridViewCheckBoxColumn
            {
                DataPropertyName = "Selected",
                HeaderText = "",
                Name = "Selected",
                Width = 80
            };
            dataGridView1.Columns.Add(colSelect);

            AddHeaderCheckBox();
        }

        private void AddHeaderCheckBox()
        {
            if (_headerCheckBox != null)
            {
                dataGridView1.Controls.Remove(_headerCheckBox);
                _headerCheckBox.Dispose();
                _headerCheckBox = null;
            }

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

            var rect = dataGridView1.GetCellDisplayRectangle(col.Index, -1, true);
            _headerCheckBox.Location = new Point(rect.X + (rect.Width - _headerCheckBox.Width) / 2, rect.Y + 3);
        }

        private void FillPreviewColumn()
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.DataBoundItem is ScanResult item)
                    row.Cells["Preview"].Value = item.Preview;
            }
        }
        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            var col = dataGridView1.Columns[e.ColumnIndex];
            if (col == null || col.Name != "Selected")
                return;

            if (dataGridView1.Rows[e.RowIndex].DataBoundItem is ScanResult item)
            {
                item.Selected = !item.Selected;
                dataGridView1.Refresh();
            }
        }
        
        // ------------------------------
        // COLORATION
        // ------------------------------
        private void dataGridView1_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            var row = dataGridView1.Rows[e.RowIndex];
            if (row.DataBoundItem is not ScanResult item)
                return;

            if (item.IsMismatch)
            {
                row.DefaultCellStyle.BackColor = Color.LightPink;
                return;
            }

            if (item.IsMissingKey)
            {
                row.DefaultCellStyle.BackColor = Color.Moccasin;
                return;
            }

            if (item.IsTranslated)
            {
                row.DefaultCellStyle.BackColor = Color.LightGreen;
                return;
            }

            row.DefaultCellStyle.BackColor = Color.White;
        }

        // ------------------------------
        // DOUBLE-CLIC : ouvrir fichier à la ligne
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
        // EXPORT
        // ------------------------------
        private void btnExport_Click(object sender, EventArgs e)
        {
            if (dataGridView1.DataSource is not List<ScanResult> list)
                return;

            foreach (var item in list.Where(x => x.Selected))
            {
                bool isXaml = item.FilePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
                string module = Utils.GetModuleFromFilename(item.FilePath);

                if (!item.IsTranslated)
                {
                    string key = Utils.GenerateKeyFromText(module, item.Text);

                    // ---------------------------------------------------------
                    // XAML
                    // ---------------------------------------------------------
                    if (isXaml)
                    {
                        string escapedTag = Utils.EscapeForXamlAttribute(item.Text);

                        bool supportsTag = !Regex.IsMatch(item.FullLine,
                            @"^\s*<\s*(Run|Span|Hyperlink|Setter|Trigger|Style|ColumnDefinition|RowDefinition|GradientStop|Storyboard|Animation|DataGridTextColumn|DataGridCheckBoxColumn|DataGridTemplateColumn)");

                        string newLine = Regex.Replace(
                            item.FullLine,
                            @"\b(Text|Content|Header|ToolTip|Title|TextBlock\.Text|Label\.Content|MenuItem\.Header|Button\.Content|CheckBox\.Content|GroupBox\.Header|TabItem\.Header)\s*=\s*""[^""]+""",
                            m =>
                                supportsTag
                                ? $"{m.Groups[1].Value}=\"{{Binding '{key}', Converter={{StaticResource Lang}}}}\" Tag=\"{escapedTag}\""
                                : $"{m.Groups[1].Value}=\"{{Binding '{key}', Converter={{StaticResource Lang}}}}\""
                        );

                        Utils.ReplaceLineInFile(item.FilePath, item.LineNumber, newLine);

                        Utils.AddToJson(_langFolder, "fr-FR.json", key, item.Text);
                        Utils.AddToJson(_langFolder, "en-GB.json", key, "");
                        continue;
                    }

                    // ---------------------------------------------------------
                    // C# — CAS SPÉCIAL : MessageBox.Show
                    // ---------------------------------------------------------
                    if (item.FullLine.Contains("MessageBox.Show("))
                    {
                        string line = item.FullLine;

                        string newLine = Regex.Replace(
                            line,
                            @"""([^""]*)""",
                            m =>
                            {
                                string originalText = m.Groups[1].Value;
                                if (string.IsNullOrWhiteSpace(originalText))
                                    return m.Value;

                                string argKey = Utils.GenerateKeyFromText(module, originalText);
                                string escaped = Utils.EscapeForCSharpLiteral(originalText);

                                // JSON
                                Utils.AddToJson(_langFolder, "fr-FR.json", argKey, originalText);
                                Utils.AddToJson(_langFolder, "en-GB.json", argKey, "");

                                return $"LanguageManager.Get(\"{argKey}\") ?? \"{escaped}\"";
                            }
                        );

                        Utils.ReplaceLineInFile(item.FilePath, item.LineNumber, newLine);
                        continue;
                    }

                    // ---------------------------------------------------------
                    // C# — CAS NORMAL : un seul littéral
                    // ---------------------------------------------------------
                    string escapedNormal = Utils.EscapeForCSharpLiteral(item.Text);

                    string newLineNormal = Regex.Replace(
                        item.FullLine,
                        $"\"{Regex.Escape(item.Text)}\"",
                        $"LanguageManager.Get(\"{key}\") ?? \"{escapedNormal}\"",
                        RegexOptions.None
                    );

                    Utils.ReplaceLineInFile(item.FilePath, item.LineNumber, newLineNormal);

                    Utils.AddToJson(_langFolder, "fr-FR.json", key, item.Text);
                    Utils.AddToJson(_langFolder, "en-GB.json", key, "");
                }
                else
                {
                    // ---------------------------------------------------------
                    // Mise à jour d'une traduction existante
                    // ---------------------------------------------------------
                    string key = Utils.ExtractKeyFromLine(item.FullLine);
                    if (string.IsNullOrEmpty(key))
                        continue;

                    string fullText = item.Text;

                    Utils.AddToJson(_langFolder, "fr-FR.json", key, fullText);
                    Utils.AddToJson(_langFolder, "en-GB.json", key, "");
                }
            }

            RefreshView();
            MessageBox.Show("Traductions appliquées.");
        }

        // ------------------------------
        // REFRESH
        // ------------------------------
        private void RefreshView()
        {
            if (string.IsNullOrEmpty(_lastOpenedFile))
                return;

            if (File.Exists(_lastOpenedFile))
            {
                OuvrirFichier_Click(null, null);
            }
            else if (Directory.Exists(_lastOpenedFile))
            {
                ChargerDossier(_lastOpenedFile);
            }

            AddHeaderCheckBox();
        }

        // ------------------------------
        // CONFIG
        // ------------------------------
        private void LoadConfig()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (cfg != null && cfg.TryGetValue("LangFolder", out var folder))
                        _langFolder = folder;
                }
                catch { }
            }

            if (string.IsNullOrEmpty(_langFolder) || !Directory.Exists(_langFolder))
            {
                MessageBox.Show("Aucun dossier de langues n'est configuré. Veuillez en choisir un.");
                ChoisirDossierLangues_Click(null, null);
            }

            lblLangFolder.Text = $"Dossier langues : {_langFolder}";
            btnChangeLangFolder.Left = lblLangFolder.Right + 10;
        }

        private void SaveConfig()
        {
            var cfg = new Dictionary<string, string>
            {
                ["LangFolder"] = _langFolder
            };

            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void ChoisirDossierLangues_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();
            dlg.Description = "Choisissez le dossier contenant fr-FR.json et en-GB.json";

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _langFolder = dlg.SelectedPath;
                SaveConfig();
                LoadExistingJsonKeys();

                lblLangFolder.Text = $"Dossier langues : {_langFolder}";
                btnChangeLangFolder.Left = lblLangFolder.Right + 10;

                MessageBox.Show("Dossier des langues mis à jour.");
            }
        }
    }
}

