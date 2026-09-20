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

        private static readonly HttpClient http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private string ConfigPath => Path.Combine(AppContext.BaseDirectory, "autotrad.config.json");

        // ============================================================
        //  CONSTRUCTEUR
        // ============================================================
        public FormMain()
        {
            InitializeComponent();

            // ?? Config d'abord (peut ouvrir un dossier langues)
            LoadConfig();

            // Événements DataGrid
            dataGridView1.SelectionChanged += dataGridView1_SelectionChanged;
            dataGridView1.CellClick += dataGridView1_CellClick;

            // Positionner les contrôles ancrés à droite
            PositionRightControls();

            // Re-positionner au redimensionnement
            this.Resize += (s, e) => PositionRightControls();
        }

        // ============================================================
        //  POSITIONNEMENT DES CONTRÔLES ANCRÉS À DROITE
        // ============================================================
        private void PositionRightControls()
        {
            // --- toolbarPanel ---
            if (cmbLang != null && toolbarPanel != null)
            {
                cmbLang.Left = toolbarPanel.Width - cmbLang.Width - 12;
                cmbLang.Top = 12;
            }

            if (lblLang != null && cmbLang != null)
            {
                lblLang.Left = cmbLang.Left - lblLang.Width - 8;
                lblLang.Top = 18;
            }

            if (btnChangeLangFolder != null && lblLang != null)
            {
                btnChangeLangFolder.Left = lblLang.Left - btnChangeLangFolder.Width - 16;
                btnChangeLangFolder.Top = 12;
            }

            // --- apiPanel ---
            if (btnSaveApiKey != null && apiPanel != null)
            {
                btnSaveApiKey.Left = apiPanel.Width - btnSaveApiKey.Width - 12;
                btnSaveApiKey.Top = 10;

                if (txtApiKey != null)
                {
                    txtApiKey.Width = btnSaveApiKey.Left - txtApiKey.Left - 12;
                }

                // ?? Le lien juste en dessous du TextBox
                if (lnkApiKey != null && txtApiKey != null)
                {
                    lnkApiKey.Left = txtApiKey.Left;
                    lnkApiKey.Top = txtApiKey.Bottom + 2;
                }
            }
            
            // --- statusPanel ---
            if (btnApply != null && statusPanel != null)
            {
                btnApply.Left = statusPanel.Width - btnApply.Width - 12;
            }
        }

        // ============================================================
        //  SÉLECTION DANS LE TABLEAU
        // ============================================================
        private void dataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentCell != null)
                txtPreview.Text = dataGridView1.CurrentCell.Value?.ToString() ?? "";
        }

        // ============================================================
        //  OUVRIR UN FICHIER
        // ============================================================
        private void OuvrirFichier_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "Fichiers sources (*.cs;*.xaml;*.html;*.htm)|*.cs;*.xaml;*.html;*.htm|" +
                         "Fichiers C# (*.cs)|*.cs|" +
                         "Fichiers XAML (*.xaml)|*.xaml|" +
                         "Fichiers HTML (*.html;*.htm)|*.html;*.htm";

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _lastOpenedFile = dlg.FileName;
                LoadExistingJsonKeys();

                var list = Scanner.ScanFile(dlg.FileName, _existingKeys);

                SetupColumns(false);
                dataGridView1.DataSource = list;

                FillPreviewColumn();

                lblStatus.Text = $"Fichier : {Path.GetFileName(dlg.FileName)} — {list.Count} clé(s) détectée(s)";
            }
        }

        // ============================================================
        //  OUVRIR UN DOSSIER
        // ============================================================
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
                     || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                    && !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)
                    && !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
                )
                .ToList();

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

            int missing = allResults.Count(r => r.IsMissingKey);

            lblStatus.Text =
                $"Dossier : {Path.GetFileName(folder)} — " +
                $"{files.Count} fichier(s), {allResults.Count} clé(s), " +
                $"{missing} manquante(s)";
        }

        // ============================================================
        //  LANGUE SÉLECTIONNÉE
        // ============================================================
        private string GetSelectedLangCode()
        {
            if (cmbLang.SelectedItem == null)
                return "fr-FR";

            string txt = cmbLang.SelectedItem.ToString() ?? "";

            if (txt.Contains("(fr-FR)")) return "fr-FR";
            if (txt.Contains("(en-GB)")) return "en-GB";
            if (txt.Contains("(de-DE)")) return "de-DE";
            if (txt.Contains("(es-ES)")) return "es-ES";

            return "fr-FR";
        }

        // ============================================================
        //  CHARGEMENT DU JSON EXISTANT
        // ============================================================
        private void LoadExistingJsonKeys()
        {
            if (string.IsNullOrEmpty(_langFolder))
                return;

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

        // ============================================================
        //  CONFIGURATION DES COLONNES
        // ============================================================
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

        // ============================================================
        //  COLORATION DES LIGNES (thème sombre)
        // ============================================================
        private void dataGridView1_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            var row = dataGridView1.Rows[e.RowIndex];
            if (row.DataBoundItem is not ScanResult item)
                return;

            if (item.IsMissingKey)
            {
                // Clé manquante ? fond légèrement rouge/orange
                row.DefaultCellStyle.BackColor = Color.FromArgb(80, 40, 40);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(255, 200, 200);
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.JsonValue))
            {
                // Traduit ? fond légèrement vert
                row.DefaultCellStyle.BackColor = Color.FromArgb(40, 60, 40);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(200, 255, 200);
                return;
            }

            // Par défaut ? fond sombre
            row.DefaultCellStyle.BackColor = Color.FromArgb(37, 37, 38);
            row.DefaultCellStyle.ForeColor = Color.FromArgb(229, 229, 229);
        }

        // ============================================================
        //  SAUVEGARDE DU JSON
        // ============================================================
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

        // ============================================================
        //  DOUBLE-CLIC ? OUVRIR DANS L'ÉDITEUR
        // ============================================================
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

        // ============================================================
        //  CONFIGURATION
        // ============================================================
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

            lblLangFolder.Text = $"Dossier : {_langFolder}";

            // Charger la clé API
            LoadApiKey();
        }

        private void SaveConfig()
        {
            // Charger la config existante pour ne rien perdre
            var cfg = new Dictionary<string, string>();

            if (File.Exists(ConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(ConfigPath);
                    var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (existing != null)
                        cfg = existing;
                }
                catch { }
            }

            cfg["LangFolder"] = _langFolder;

            if (!string.IsNullOrWhiteSpace(txtApiKey.Text))
                cfg["ApiKey"] = txtApiKey.Text.Trim();

            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }

        // ============================================================
        //  CLÉ API
        // ============================================================
        private void LoadApiKey()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return;

                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (cfg != null && cfg.TryGetValue("ApiKey", out var key))
                    txtApiKey.Text = key ?? "";
            }
            catch { }
        }

        private void btnSaveApiKey_Click(object sender, EventArgs e)
        {
            string key = txtApiKey.Text.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show(
                    "Veuillez saisir une clé API valide.",
                    "Clé vide",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            SaveConfig();

            lblStatus.Text = "Clé API sauvegardée.";

            MessageBox.Show(
                "Clé API sauvegardée.",
                "OK",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        // ============================================================
        //  CHOISIR DOSSIER LANGUES
        // ============================================================
        private void ChoisirDossierLangues_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();
            dlg.Description = "Choisissez le dossier contenant fr-FR.json et en-GB.json";

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _langFolder = dlg.SelectedPath;
                SaveConfig();
                LoadExistingJsonKeys();

                lblLangFolder.Text = $"Dossier : {_langFolder}";
                PositionRightControls();

                MessageBox.Show("Dossier des langues mis à jour.");
            }
        }

        // ============================================================
        //  BOUTON APPLIQUER
        // ============================================================
        private void btnApply_Click(object sender, EventArgs e)
        {
            if (dataGridView1.DataSource is not IEnumerable<ScanResult> list)
                return;

            int applied = 0;

            foreach (var item in list)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                    continue;

                if (string.IsNullOrWhiteSpace(item.JsonValue))
                    continue;

                _existingKeys[item.Key] = item.JsonValue;
                applied++;
            }

            SaveJson();

            // Rafraîchir l'affichage
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

            lblStatus.Text = $"Modifications appliquées : {applied} clé(s).";

            MessageBox.Show($"Modifications appliquées au fichier JSON ({applied} clé(s)).",
                "Succès",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ============================================================
        //  TRADUCTION VIA API
        // ============================================================
        private async Task<string> TranslateTextAsync(string text, string lang)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return "";

                string target = lang switch
                {
                    "fr-FR" => "fr",
                    "en-GB" => "en",
                    "de-DE" => "de",
                    "es-ES" => "es",
                    _ => "en"
                };

                string apiKey = txtApiKey.Text.Trim();

                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show(
                        "Aucune clé API configurée. Saisissez-la dans le champ en haut.",
                        "Configuration manquante",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return "";
                }

                var payload = new
                {
                    text = text,
                    target_language = target
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.translateapi.ai/api/v1/translate/");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Content = content;

                var response = await http.SendAsync(request);
                string result = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        $"Erreur API ({(int)response.StatusCode}) :\n{result}",
                        "Erreur de traduction",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return "";
                }

                using var doc = JsonDocument.Parse(result);

                if (doc.RootElement.TryGetProperty("translated_text", out var translated))
                    return translated.GetString() ?? "";

                MessageBox.Show(
                    "Réponse inattendue de l'API :\n" + result,
                    "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return "";
            }
            catch (TaskCanceledException)
            {
                MessageBox.Show(
                    "La traduction a expiré (timeout 30s).",
                    "Timeout",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return "";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Erreur de traduction :\n" + ex.Message,
                    "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return "";
            }
        }

        // ============================================================
        //  CLIC SUR LE BOUTON "TRAD"
        // ============================================================
        private async void dataGridView1_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            if (dataGridView1.Columns[e.ColumnIndex] is not DataGridViewButtonColumn)
                return;

            if (dataGridView1.Rows[e.RowIndex].DataBoundItem is not ScanResult item)
                return;

            string sourceText = item.Text;

            if (string.IsNullOrWhiteSpace(sourceText))
                return;

            string targetLang = GetSelectedLangCode();

            lblStatus.Text = $"Traduction en cours : {sourceText}...";

            string translated = await TranslateTextAsync(sourceText, targetLang);

            if (!string.IsNullOrWhiteSpace(translated))
            {
                item.JsonValue = translated;
                dataGridView1.Refresh();

                lblStatus.Text = $"Traduit : {sourceText} ? {translated}";
            }
            else
            {
                lblStatus.Text = "Traduction annulée ou échouée.";
            }
        }
    }
}