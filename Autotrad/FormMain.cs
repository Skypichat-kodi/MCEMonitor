using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Autotrad
{
    public partial class FormMain : Form
    {
        private string _lastOpenedFile = "";
        private Dictionary<string, string> _existingKeys = new();
        private string _langFolder = "";

        private string _currentJsonPath = "";

        private static readonly HttpClient http = new HttpClient();

        private string ConfigPath => Path.Combine(AppContext.BaseDirectory, "autotrad.config.json");

        public FormMain()
        {
            InitializeComponent();
            LoadConfig();

            dataGridView1.Width = this.ClientSize.Width - 24;
            txtPreview.Width = this.ClientSize.Width - 24;

            dataGridView1.ReadOnly = false;
            dataGridView1.EditMode = DataGridViewEditMode.EditOnEnter;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.CellSelect;

            txtPreview.Multiline = true;
            txtPreview.ReadOnly = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;

            dataGridView1.SelectionChanged += dataGridView1_SelectionChanged;

            // ?? Ajout pour gérer le bouton Trad
            dataGridView1.CellClick += dataGridView1_CellClick;

            cmbLang.Location = new Point(topPanel.Width - cmbLang.Width - 20, 4);
        }

        private void dataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentCell != null)
                txtPreview.Text = dataGridView1.CurrentCell.Value?.ToString();
        }

        private void OuvrirFichier_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "Fichiers C# (*.cs)|*.cs";

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _lastOpenedFile = dlg.FileName;
                LoadExistingJsonKeys();

                var list = Scanner.ScanFile(dlg.FileName, _existingKeys);

                SetupColumns(false);
                dataGridView1.DataSource = list;

                FillPreviewColumn();
            }
        }

        private void OuvrirDossier_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                ChargerDossier(dlg.SelectedPath);
            }
        }

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

        private string GetSelectedLangCode()
        {
            if (cmbLang.SelectedItem == null)
                return "fr-FR";

            string txt = cmbLang.SelectedItem.ToString();

            if (txt.Contains("(fr-FR)")) return "fr-FR";
            if (txt.Contains("(en-GB)")) return "en-GB";
            if (txt.Contains("(de-DE)")) return "de-DE";
            if (txt.Contains("(es-ES)")) return "es-ES";

            return "fr-FR";
        }

        private void LoadExistingJsonKeys()
        {
            string lang = GetSelectedLangCode();
            string path = Path.Combine(_langFolder, $"{lang}.json");

            Directory.CreateDirectory(_langFolder);

            _currentJsonPath = path;

            if (!File.Exists(path))
            {
                File.WriteAllText(path, "{}");
                _existingKeys = new Dictionary<string, string>();
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                _existingKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                                 ?? new Dictionary<string, string>();
            }
            catch
            {
                _existingKeys = new Dictionary<string, string>();
                File.WriteAllText(path, "{}");
            }
        }

        private void cmbLang_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadExistingJsonKeys();

            if (Directory.Exists(_lastOpenedFile))
                ChargerDossier(_lastOpenedFile);
            else if (File.Exists(_lastOpenedFile))
            {
                var list = Scanner.ScanFile(_lastOpenedFile, _existingKeys);
                SetupColumns(false);
                dataGridView1.DataSource = list;
                FillPreviewColumn();
            }
        }

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
                FillWeight = 40,
                ReadOnly = true,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
            };
            dataGridView1.Columns.Add(colPreview);

            var colText = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Text",
                HeaderText = "Texte détecté",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 40,
                ReadOnly = true,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
            };
            dataGridView1.Columns.Add(colText);

            var colTrad = new DataGridViewButtonColumn
            {
                HeaderText = "Trad",
                Text = "Trad",
                UseColumnTextForButtonValue = true,
                Width = 60
            };
            dataGridView1.Columns.Add(colTrad);

            var colJson = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "JsonValue",
                HeaderText = "Traduction JSON",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50,
                ReadOnly = false,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True }
            };
            dataGridView1.Columns.Add(colJson);
        }

        private void FillPreviewColumn()
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.DataBoundItem is ScanResult item)
                    row.Cells["Preview"].Value = item.Preview;
            }
        }

        private void dataGridView1_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            var row = dataGridView1.Rows[e.RowIndex];
            if (row.DataBoundItem is not ScanResult item)
                return;

            if (item.IsMissingKey)
            {
                row.DefaultCellStyle.BackColor = Color.Moccasin;
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.JsonValue))
            {
                row.DefaultCellStyle.BackColor = Color.LightGreen;
                return;
            }

            row.DefaultCellStyle.BackColor = Color.White;
        }

        private void SaveJson()
        {
            if (string.IsNullOrEmpty(_currentJsonPath))
                return;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string json = JsonSerializer.Serialize(_existingKeys, options);

            File.WriteAllText(_currentJsonPath, json, new UTF8Encoding(true));
        }

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

        private void btnApply_Click(object sender, EventArgs e)
        {
            if (dataGridView1.DataSource is not IEnumerable<ScanResult> list)
                return;

            foreach (var item in list)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                    continue;

                if (string.IsNullOrWhiteSpace(item.JsonValue))
                    continue;

                _existingKeys[item.Key] = item.JsonValue;
            }

            SaveJson();

            if (Directory.Exists(_lastOpenedFile))
            {
                ChargerDossier(_lastOpenedFile);
            }
            else if (File.Exists(_lastOpenedFile))
            {
                var refreshed = Scanner.ScanFile(_lastOpenedFile, _existingKeys);
                SetupColumns(false);
                dataGridView1.DataSource = refreshed;
                FillPreviewColumn();
            }

            MessageBox.Show("Modifications appliquées au fichier JSON.", "Succès",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ?? MÉTHODE DE TRADUCTION LIBRETRANSLATE
private async Task<string> TranslateTextAsync(string text, string lang)
{
    string target = lang switch
    {
        "fr-FR" => "fr",
        "en-GB" => "en",
        "de-DE" => "de",
        "es-ES" => "es",
        _ => "en"
    };

    string apiKey = "ta_8ed30e2b2252b02d349477accd1664d9a30f35b1269004c1120d25f2";

    var payload = new
    {
        text = text,
        target_language = target
    };

    var json = JsonSerializer.Serialize(payload);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.translateapi.ai/api/v1/translate/");
    request.Headers.Add("Authorization", $"Bearer {apiKey}");
    request.Content = content;

    var response = await http.SendAsync(request);
    string result = await response.Content.ReadAsStringAsync();

    using var doc = JsonDocument.Parse(result);
    return doc.RootElement.GetProperty("translated_text").GetString();
}

        // ?? GESTION DU CLIC SUR LE BOUTON TRAD
        private async void dataGridView1_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            if (dataGridView1.Columns[e.ColumnIndex] is DataGridViewButtonColumn)
            {
                if (dataGridView1.Rows[e.RowIndex].DataBoundItem is ScanResult item)
                {
                    string sourceText = item.Text;
                    string targetLang = GetSelectedLangCode();

                    string translated = await TranslateTextAsync(sourceText, targetLang);

                    if (!string.IsNullOrWhiteSpace(translated))
                    {
                        item.JsonValue = translated;
                        dataGridView1.Refresh();
                    }
                }
            }
        }
    }
}

