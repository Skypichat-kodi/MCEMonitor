using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MCEMonitor.Languages;
using MediaMonitor.Core.Models;
using MediaMonitor.UI.Models;
using MediaMonitor.UI.Services;
using Microsoft.Win32;

namespace MediaMonitor.UI
{
    public partial class MainWindow : Window
    {
        private bool IsLoggingEnabled = false;
        public static Action<string> StaticUiLog;

        private readonly ObservableCollection<MediaUsageItem> _items = new();
        private readonly ObservableCollection<MediaUsageItem> _history = new();

        // Onglet Backup
        private readonly ObservableCollection<MediaUsageItem> _backupAll      = new();
        private readonly ObservableCollection<MediaUsageItem> _backupFiltered = new();
        private BackupFileModel? _currentBackup;
        private bool _backupTabInitialized = false;

        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing = false;
        private bool _loadingConfig = false;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            LoadDvbSettings();

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || args[1] != "--from-mcem")
            {
                MessageBox.Show(
                    "MediaMonitor.UI ne peut être lancé que depuis MCEMonitor.",
                    "Accès refusé",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            ResetUiLog();

            bool serviceRunning = Process.GetProcessesByName("MediaMonitor.Service").Length > 0;
            if (!serviceRunning)
            {
                MessageBox.Show(
                    "Le service MediaMonitor n'est pas démarré.\n" +
                    "Veuillez lancer MCEMonitor pour le démarrer automatiquement.",
                    "Service non démarré",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Close();
                return;
            }

            Loaded += MainWindow_Loaded;

            StaticUiLog = (msg) =>
            {
                if (IsLoggingEnabled) UiLog(msg);
            };

            FilesGrid.ItemsSource    = _items;
            HistoryGrid.ItemsSource  = _history;
            BackupGrid.ItemsSource   = _backupFiltered;

            TopSeriesGrid.ItemsSource   = new ObservableCollection<StatRow>();
            TopArtistesGrid.ItemsSource = new ObservableCollection<StatRow>();
            TopClientsGrid.ItemsSource  = new ObservableCollection<StatRow>();
            PerClientGrid.ItemsSource   = new ObservableCollection<ClientStatRow>();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += async (_, __) => await RefreshStateSafe();
            _refreshTimer.Start();

            _ = RefreshStateSafe();

            Loaded += async (_, __) =>
            {
                _loadingConfig = true;
                try
                {
                    ToggleWeb.IsChecked = await ServiceIpcClient.GetWebEnabled();
                    txtWebPort.Text = (await ServiceIpcClient.GetWebPort()).ToString();

                    var settingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MCEMonitor", "MediaMonitor.Web.config");

                    if (File.Exists(settingsPath))
                    {
                        foreach (var line in File.ReadAllLines(settingsPath))
                        {
                            if (line.StartsWith("Username=", StringComparison.OrdinalIgnoreCase))
                                txtWebLogin.Text = line.Split('=', 2)[1].Trim();

                            if (line.StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
                                txtWebPassword.Password = line.Split('=', 2)[1].Trim();

                            if (line.StartsWith("RetentionDays=", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(line.Split('=', 2)[1].Trim(), out int d))
                                {
                                    chkNone.IsChecked = d == 0;
                                    chk1w.IsChecked   = d == 7;
                                    chk2w.IsChecked   = d == 14;
                                    chk1m.IsChecked   = d == 30;
                                }
                            }
                        }

                        foreach (var line in File.ReadAllLines(settingsPath))
                        {
                            if (line.StartsWith("DvbViewerUrl=", StringComparison.OrdinalIgnoreCase))
                                txtDvbUrl.Text = line.Split('=', 2)[1].Trim();

                            if (line.StartsWith("DvbViewerUser=", StringComparison.OrdinalIgnoreCase))
                                txtDvbUser.Text = line.Split('=', 2)[1].Trim();

                            if (line.StartsWith("DvbViewerPass=", StringComparison.OrdinalIgnoreCase))
                            {
                                string pass = line.Split('=', 2)[1].Trim();
                                txtDvbPass.Password = pass;
                                txtDvbPassVisible.Text = pass;
                            }
                        }

                        UiLog("Config Web + DVBViewer chargée");
                    }
                }
                catch (Exception ex)
                {
                    UiLog("Erreur initialisation Serveur Web : " + ex.Message);
                }
                finally
                {
                    _loadingConfig = false;
                }
            };

            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEmailSwitch();
            await InitBackupTabAsync();
        }

        private void SystemEvents_PowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                StaticUiLog("PC sorti de veille → tentative de reconnexion IPC");
                _ = HandleResumeAsync();
            }
        }

        private async Task HandleResumeAsync()
        {
            try
            {
                await Task.Delay(1500);
                await RefreshStateSafe();
                await LoadEmailSwitch();
                Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                StaticUiLog("Erreur HandleResumeAsync : " + ex.Message);
            }
        }

        private async Task LoadEmailSwitch()
        {
            try
            {
                var enabled = await ServiceIpcClient.GetEmailEnabled();
                if (enabled != null)
                {
                    ToggleEmail.IsChecked = enabled.Value;
                    UiLog("État email chargé depuis le service : " + enabled.Value);
                }
                else UiLog("Impossible de lire l'état email depuis le service");
            }
            catch (Exception ex)
            {
                UiLog("Erreur LoadEmailSwitch : " + ex.Message);
            }
        }

        private void ResetUiLog()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor", "Logs");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "MediaMonitor.UI.log"), string.Empty);
            }
            catch { }
        }

        private async void ToggleLog_Checked(object sender, RoutedEventArgs e)
        {
            IsLoggingEnabled = true;
            UiLog("Log activé");
            try { await ServiceIpcClient.SetLogging(true); UiLog("Service : log activé"); }
            catch (Exception ex) { UiLog("Erreur IPC SetLogging(true) : " + ex.Message); }
        }

        private async void ToggleLog_Unchecked(object sender, RoutedEventArgs e)
        {
            UiLog("Log désactivé");
            IsLoggingEnabled = false;
            try { await ServiceIpcClient.SetLogging(false); UiLog("Service : log désactivé"); }
            catch (Exception ex) { UiLog("Erreur IPC SetLogging(false) : " + ex.Message); }
        }

        private async void ToggleEmail_Checked(object sender, RoutedEventArgs e)
        {
            UiLog("Envoi automatique du rapport activé");
            try { await ServiceIpcClient.SetEmailSending(true); UiLog("Service : envoi email activé"); }
            catch (Exception ex) { UiLog("Erreur IPC SetEmailSending(true) : " + ex.Message); }
        }

        private async void ToggleEmail_Unchecked(object sender, RoutedEventArgs e)
        {
            UiLog("Envoi automatique du rapport désactivé");
            try { await ServiceIpcClient.SetEmailSending(false); UiLog("Service : envoi email désactivé"); }
            catch (Exception ex) { UiLog("Erreur IPC SetEmailSending(false) : " + ex.Message); }
        }

        private async void ToggleWeb_Checked(object sender, RoutedEventArgs e)
        {
            UiLog("Serveur Web activé");
            try { await ServiceIpcClient.SetWebEnabled(true); UiLog("Service : serveur web activé"); }
            catch (Exception ex) { UiLog("Erreur IPC SetWebEnabled(true) : " + ex.Message); }
        }

        private async void ToggleWeb_Unchecked(object sender, RoutedEventArgs e)
        {
            UiLog("Serveur Web désactivé");
            try { await ServiceIpcClient.SetWebEnabled(false); UiLog("Service : serveur web désactivé"); }
            catch (Exception ex) { UiLog("Erreur IPC SetWebEnabled(false) : " + ex.Message); }
        }

        private void btnOpenWeb_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtWebPort.Text, out int port)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://localhost:{port}/?lang={Thread.CurrentThread.CurrentUICulture.Name}",
                    UseShellExecute = true
                });
                UiLog("Ouverture du navigateur sur http://localhost:" + port);
            }
            catch (Exception ex) { UiLog("Erreur ouverture navigateur : " + ex.Message); }
        }

        private void UiLog(string msg)
        {
            if (!IsLoggingEnabled) return;
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor", "Logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(
                    Path.Combine(folder, "MediaMonitor.UI.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
            }
            catch { }
        }

        private async Task RefreshStateSafe()
        {
            if (_isRefreshing) { StaticUiLog("RefreshState ignoré (déjà en cours)"); return; }
            _isRefreshing = true;
            try { await RefreshState(); }
            finally { _isRefreshing = false; }
        }

        private async Task RefreshState()
        {
            StaticUiLog("RefreshState() appelé");
            try
            {
                var state = await ServiceIpcClient.GetState();
                if (state == null) { StaticUiLog("state == null"); return; }

                _items.Clear();
                foreach (var item in state.openFiles) _items.Add(item);

                LastImageText.Text = string.IsNullOrEmpty(state.lastImage)
                    ? (LanguageManager.Get("Dernière image ouverte : aucune") ?? "Dernière image ouverte : aucune")
                    : (LanguageManager.Get("Dernière image ouverte :") ?? "Dernière image ouverte :") + " " + state.lastImage;

                var history = await ServiceIpcClient.GetHistory();
                if (history != null)
                {
                    _history.Clear();
                    foreach (var h in history) _history.Add(h);
                }
            }
            catch (Exception ex)
            {
                StaticUiLog("Erreur IPC : " + ex.Message);
                LastImageText.Text = "Erreur IPC : " + ex.Message;
            }
        }

        private async void SendReportNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await ServiceIpcClient.SendReport();
                MessageBox.Show(result ? "Rapport envoyé." : "Erreur lors de l'envoi du rapport.");
            }
            catch (Exception ex) { MessageBox.Show("Erreur IPC : " + ex.Message); }
        }

        private async void btnApplyWebAll_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtWebPort.Text, out int port))
            {
                MessageBox.Show("Port invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string login = txtWebLogin.Text.Trim();
            string pass = txtWebPassword.Password.Trim();

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
            {
                MessageBox.Show("Veuillez entrer un login et un mot de passe.", "Erreur",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await ServiceIpcClient.SetWebPort(port);
                await ServiceIpcClient.SetWebCredentials(login, pass);
                MessageBox.Show("Paramètres Web mis à jour.", "OK",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UiLog("Erreur IPC WebAll : " + ex.Message);
                MessageBox.Show("Erreur IPC : " + ex.Message, "Erreur",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool _passwordVisible = false;

        private void btnShowPassword_Click(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;
            if (_passwordVisible)
            {
                txtWebPasswordVisible.Text = txtWebPassword.Password;
                txtWebPasswordVisible.Visibility = Visibility.Visible;
                txtWebPassword.Visibility = Visibility.Collapsed;
                btnShowPassword.Content = "🙈";
            }
            else
            {
                txtWebPassword.Password = txtWebPasswordVisible.Text;
                txtWebPasswordVisible.Visibility = Visibility.Collapsed;
                txtWebPassword.Visibility = Visibility.Visible;
                btnShowPassword.Content = "👁";
            }
        }

        private void btnOpenBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtWebPort.Text, out int port))
            {
                MessageBox.Show("Port Web invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://localhost:{port}/backup?lang={Thread.CurrentThread.CurrentUICulture.Name}",
                    UseShellExecute = true
                });
                UiLog("Ouverture du navigateur sur /backup");
            }
            catch (Exception ex)
            {
                UiLog("Erreur ouverture /backup : " + ex.Message);
                MessageBox.Show("Impossible d’ouvrir la page /backup.\n" + ex.Message,
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BackupOption_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingConfig) return;
            if (sender is not CheckBox chk) return;

            chkNone.IsChecked = chk == chkNone;
            chk1w.IsChecked   = chk == chk1w;
            chk2w.IsChecked   = chk == chk2w;
            chk1m.IsChecked   = chk == chk1m;

            int days = chk switch
            {
                var c when c == chkNone => 0,
                var c when c == chk1w   => 7,
                var c when c == chk2w   => 14,
                var c when c == chk1m   => 30,
                _ => 0
            };

            SaveRetentionToWebConfig(days);
        }

        private void SaveRetentionToWebConfig(int days)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor", "MediaMonitor.Web.config");

            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            bool found = false;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("RetentionDays=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "RetentionDays=" + days;
                    found = true;
                    break;
                }
            }
            if (!found) lines.Add("RetentionDays=" + days);
            File.WriteAllLines(path, lines);
        }

        private void LoadDvbSettings()
        {
            string configPath = @"C:\ProgramData\MCEMonitor\MediaMonitor.Web.config";
            if (!File.Exists(configPath)) return;

            foreach (var line in File.ReadAllLines(configPath))
            {
                if (line.StartsWith("DvbViewerSwitch=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line.Split('=')[1].Trim();
                    ToggleDvb.IsChecked = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void SaveDvbSwitch()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor", "MediaMonitor.Web.config");

            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            bool found = false;
            string value = ToggleDvb.IsChecked == true ? "true" : "false";

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("DvbViewerSwitch=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "DvbViewerSwitch=" + value;
                    found = true;
                }
            }
            if (!found) lines.Add("DvbViewerSwitch=" + value);
            File.WriteAllLines(path, lines);
        }

        private void ToggleDvb_Checked(object sender, RoutedEventArgs e) => SaveDvbSwitch();
        private void ToggleDvb_Unchecked(object sender, RoutedEventArgs e) => SaveDvbSwitch();

        private void btnApplyDvb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = txtDvbUrl.Text.Trim();
                string user = txtDvbUser.Text.Trim();
                string pass = (txtDvbPass.Visibility == Visibility.Visible)
                    ? txtDvbPass.Password
                    : txtDvbPassVisible.Text;

                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor", "MediaMonitor.Web.config");

                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                bool fU = false, fUs = false, fP = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("DvbViewerUrl=",  StringComparison.OrdinalIgnoreCase)) { lines[i] = "DvbViewerUrl="  + url;  fU  = true; }
                    if (lines[i].StartsWith("DvbViewerUser=", StringComparison.OrdinalIgnoreCase)) { lines[i] = "DvbViewerUser=" + user; fUs = true; }
                    if (lines[i].StartsWith("DvbViewerPass=", StringComparison.OrdinalIgnoreCase)) { lines[i] = "DvbViewerPass=" + pass; fP  = true; }
                }
                if (!fU)  lines.Add("DvbViewerUrl="  + url);
                if (!fUs) lines.Add("DvbViewerUser=" + user);
                if (!fP)  lines.Add("DvbViewerPass=" + pass);

                File.WriteAllLines(path, lines);
                UiLog("Configuration DVBViewer sauvegardée dans Web.config");
            }
            catch (Exception ex) { UiLog("Erreur sauvegarde DVB : " + ex.Message); }
        }

        private void btnShowDvbPass_Click(object sender, RoutedEventArgs e)
        {
            if (txtDvbPass.Visibility == Visibility.Visible)
            {
                txtDvbPassVisible.Text = txtDvbPass.Password;
                txtDvbPass.Visibility = Visibility.Collapsed;
                txtDvbPassVisible.Visibility = Visibility.Visible;
            }
            else
            {
                txtDvbPass.Password = txtDvbPassVisible.Text;
                txtDvbPassVisible.Visibility = Visibility.Collapsed;
                txtDvbPass.Visibility = Visibility.Visible;
            }
        }

        // ============================================================
        //  ONGLET BACKUP
        // ============================================================
        private async Task InitBackupTabAsync()
        {
            try
            {
                BackupFileModel? backup = null;
                await Task.Run(() => { backup = BackupLoader.Load(); });

                if (backup == null)
                {
                    _backupAll.Clear();
                    _backupFiltered.Clear();
                    BackupInfoText.Text = "Aucun backup disponible.";
                    RefreshStatsFromBackup();
                    return;
                }

                _currentBackup = backup;
                var items = BackupLoader.Flatten(backup);

                _backupAll.Clear();
                foreach (var it in items) _backupAll.Add(it);

                var clients = _backupAll
                    .Where(i => !string.IsNullOrWhiteSpace(i.ClientDisplay))
                    .Select(i => i.ClientDisplay)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                BackupFilterClient.ItemsSource = new[] { "Tous" }.Concat(clients).ToList();
                BackupFilterClient.SelectedIndex = 0;
                BackupFilterType.SelectedIndex = 0;
                BackupFilterDate.SelectedIndex = 0;

                BackupInfoText.Text = $"{items.Count} élément(s) – Rétention : {backup.RetentionDays} j";

                ApplyBackupFilters();
                RefreshStatsFromBackup();
            }
            catch (Exception ex) { UiLog("Erreur InitBackupTabAsync : " + ex.Message); }
        }

        private void ApplyBackupFilters()
        {
            if (_backupAll == null) return;

            IEnumerable<MediaUsageItem> items = _backupAll;

            string type = (BackupFilterType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tous";
            items = type switch
            {
                "Audio" => items.Where(i => i.MediaType.Equals("audio", StringComparison.OrdinalIgnoreCase)),
                "Série" => items.Where(i => i.MediaType.Equals("serie", StringComparison.OrdinalIgnoreCase)),
                "Vidéo" => items.Where(i => i.MediaType.Equals("video", StringComparison.OrdinalIgnoreCase)),
                "Image" => items.Where(i => i.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase)),
                "REC"   => items.Where(i => i.MediaType.StartsWith("rec", StringComparison.OrdinalIgnoreCase)),
                "TV"    => items.Where(i => i.MediaType.Equals("tv", StringComparison.OrdinalIgnoreCase)),
                _       => items
            };

            string client = BackupFilterClient.SelectedItem?.ToString() ?? "Tous";
            if (client != "Tous")
                items = items.Where(i => i.ClientDisplay != null &&
                                         i.ClientDisplay.Equals(client, StringComparison.OrdinalIgnoreCase));

            string dateF = (BackupFilterDate.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tout";
            DateTime now = DateTime.Now;
            items = dateF switch
            {
                "Aujourd'hui" => items.Where(i => i.Timestamp.Date == now.Date),
                "Hier"        => items.Where(i => i.Timestamp.Date == now.AddDays(-1).Date),
                "7 jours"     => items.Where(i => i.Timestamp >= now.AddDays(-7)),
                "30 jours"    => items.Where(i => i.Timestamp >= now.AddDays(-30)),
                _             => items
            };

            _backupFiltered.Clear();
            foreach (var it in items) _backupFiltered.Add(it);
        }

        private void btnRefreshBackup_Click(object sender, RoutedEventArgs e) => _ = InitBackupTabAsync();

        private void btnBackupPdf_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBackup == null)
            {
                MessageBox.Show("Aucun backup à exporter.", "Info",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Enregistrer le backup en PDF",
                Filter = "Fichiers PDF (*.pdf)|*.pdf",
                FileName = $"backup_{DateTime.Now:yyyy-MM-dd}.pdf"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                PdfBackupGenerator.Generate(_currentBackup, dlg.FileName);
                MessageBox.Show("PDF généré avec succès.", "OK",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur génération PDF : " + ex.Message,
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BackupFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyBackupFilters();
        }

        private void btnPurgeBackup_Click(object sender, RoutedEventArgs e)
        {
            var msg = LanguageManager.Get("Voulez-vous vraiment supprimer TOUTES les sauvegardes ?")
                      ?? "Voulez-vous vraiment supprimer TOUTES les sauvegardes ?";

            if (MessageBox.Show(msg, "Confirmation",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                if (File.Exists(BackupLoader.BackupPath))
                    File.Delete(BackupLoader.BackupPath);

                _ = InitBackupTabAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur purge : " + ex.Message);
            }
        }
        
        // ============================================================
        //  ONGLET STATISTIQUES
        // ============================================================
        private void RefreshStatsFromBackup()
        {
            var topSeriesCol   = (ObservableCollection<StatRow>)TopSeriesGrid.ItemsSource;
            var topArtistesCol = (ObservableCollection<StatRow>)TopArtistesGrid.ItemsSource;
            var topClientsCol  = (ObservableCollection<StatRow>)TopClientsGrid.ItemsSource;
            var perClientCol   = (ObservableCollection<ClientStatRow>)PerClientGrid.ItemsSource;

            if (_backupAll == null || _backupAll.Count == 0)
            {
                StatTotal.Text = StatAudio.Text = StatSerie.Text = StatVideo.Text = "0";
                StatImage.Text = StatRec.Text   = StatTv.Text    = "0";
                topSeriesCol.Clear();
                topArtistesCol.Clear();
                topClientsCol.Clear();
                perClientCol.Clear();
                return;
            }

            var (total, audio, serie, video, image, rec, tv) = StatsCalculator.CountByType(_backupAll);

            StatTotal.Text = total.ToString();
            StatAudio.Text = audio.ToString();
            StatSerie.Text = serie.ToString();
            StatVideo.Text = video.ToString();
            StatImage.Text = image.ToString();
            StatRec.Text   = rec.ToString();
            StatTv.Text    = tv.ToString();

            topSeriesCol.Clear();
            foreach (var r in StatsCalculator.TopSeries(_backupAll)) topSeriesCol.Add(r);

            topArtistesCol.Clear();
            foreach (var r in StatsCalculator.TopArtistes(_backupAll)) topArtistesCol.Add(r);

            topClientsCol.Clear();
            foreach (var r in StatsCalculator.TopClients(_backupAll)) topClientsCol.Add(r);

            perClientCol.Clear();
            foreach (var r in StatsCalculator.PerClient(_backupAll)) perClientCol.Add(r);
        }

        // ============================================================
        //  INFO POPUP (inchangé)
        // ============================================================
        public async void ShowInfoPopup(MediaUsageItem info)
        {
            string path = info.Path;

            if (string.IsNullOrEmpty(Path.GetExtension(path)))
            {
                var popup = new InfoPopupWindow(info) { Owner = this };
                popup.ShowDialog();
                return;
            }

            string? json = await ServiceIpcClient.SendRaw($"get-file-info {path}");
            if (json == null)
            {
                MessageBox.Show($"Impossible d'obtenir les informations du fichier.\n\nChemin : {path}",
                                "Erreur IPC", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (json.Contains("\"error\""))
            {
                MessageBox.Show($"Erreur renvoyée par le serveur :\n\n{json}",
                                "Erreur IPC", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MediaUsageItem? fullInfo = null;
            try { fullInfo = System.Text.Json.JsonSerializer.Deserialize<MediaUsageItem>(json); }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur de désérialisation : {ex.Message}",
                                "Erreur JSON", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (fullInfo == null) return;

            var popup2 = new InfoPopupWindow(fullInfo) { Owner = this };
            popup2.ShowDialog();
        }

        public ICommand ShowInfoCommand => new RelayCommand<MediaUsageItem>(ShowInfo);

        private async void ShowInfo(MediaUsageItem info)
        {
            if (info == null) return;
            string path = info.Path;

            if (string.IsNullOrEmpty(Path.GetExtension(path))) { ShowInfoPopup(info); return; }

            try
            {
                var fullInfo = await ServiceIpcClient.GetFileInfoAsync(path);
                ShowInfoPopup(fullInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'analyse du fichier : " + ex.Message);
            }
        }
    }
}