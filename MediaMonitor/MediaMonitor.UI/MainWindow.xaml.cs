using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MediaMonitor.UI.Services;
using MediaMonitor.Core.Models;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;

namespace MediaMonitor.UI
{
    public partial class MainWindow : Window
    {
        private bool IsLoggingEnabled = false;

        public static Action<string> StaticUiLog;

        private readonly ObservableCollection<MediaUsageItem> _items = new();
        private readonly ObservableCollection<MediaUsageItem> _history = new();

        private readonly DispatcherTimer _refreshTimer;

        // Empêche RefreshState de se lancer en parallèle
        private bool _isRefreshing = false;

        // Empêche les handlers de réécrire le config pendant le chargement
        private bool _loadingConfig = false;

        public MainWindow()
        {
            InitializeComponent();

            // 🔒 Empêche l'exécution directe de MediaMonitor.UI
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || args[1] != "--from-mcem")
            {
                MessageBox.Show(
                    "MediaMonitor.UI ne peut être lancé que depuis MCEMonitor.",
                    "Accès refusé",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Application.Current.Shutdown();
                return;
            }

            ResetUiLog();

            bool serviceRunning = Process.GetProcessesByName("MediaMonitor.Service").Length > 0;

            if (!serviceRunning)
            {
                var result = MessageBox.Show(
                    "MediaMonitor.Service n'est pas en cours d'exécution.\n\n" +
                    "Voulez-vous le démarrer maintenant ?",
                    "Service non démarré",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        string servicePath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "MCEMonitor",
                            "MediaMonitor.Service.exe"
                        );

                        if (!File.Exists(servicePath))
                        {
                            MessageBox.Show(
                                "MediaMonitor.Service.exe est introuvable dans :\n" + servicePath,
                                "Erreur",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                            Close();
                            return;
                        }

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = servicePath,
                            UseShellExecute = true
                        });

                        System.Threading.Thread.Sleep(1200);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Impossible de démarrer MediaMonitor.Service.exe :\n" + ex.Message,
                            "Erreur",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                        Close();
                        return;
                    }
                }
                else
                {
                    Close();
                    return;
                }
            }

            string trayPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MCEMonitor",
                "MediaMonitor.Tray.exe"
            );

            if (!File.Exists(trayPath))
            {
                trayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MCEMonitor",
                    "MediaMonitor.Tray.exe"
                );
            }

            if (File.Exists(trayPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = trayPath,
                    UseShellExecute = true
                });
            }

            // UI

            // Chargement état email
            Loaded += MainWindow_Loaded;

            StaticUiLog = (msg) =>
            {
                if (IsLoggingEnabled)
                    UiLog(msg);
            };

            FilesGrid.ItemsSource = _items;
            HistoryGrid.ItemsSource = _history;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
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

                    // 🔵 Chargement des identifiants Web
                    var settingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MCEMonitor",
                        "MediaMonitor.Web.config"
                    );

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

                        UiLog("Identifiants Web chargés depuis le service");

                        // 🔵 Chargement config DVBViewer
                        string tvPath = settingsPath;

                        if (File.Exists(tvPath))
                        {
                            foreach (var line in File.ReadAllLines(tvPath))
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

                            UiLog("Configuration DVBViewer chargée");
                        }
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

            // Détection sortie de veille
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        }

        // Quand la fenêtre est prête
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEmailSwitch();
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
                else
                {
                    UiLog("Impossible de lire l'état email depuis le service");
                }
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
                    "MCEMonitor",
                    "Logs"
                );

                Directory.CreateDirectory(folder);

                string file = Path.Combine(folder, "MediaMonitor.UI.log");

                File.WriteAllText(file, string.Empty);
            }
            catch { }
        }

        private async void ToggleLog_Checked(object sender, RoutedEventArgs e)
        {
            IsLoggingEnabled = true;
            UiLog("Log activé");

            try
            {
                await ServiceIpcClient.SetLogging(true);
                UiLog("Service : log activé");
            }
            catch (Exception ex)
            {
                UiLog("Erreur IPC SetLogging(true) : " + ex.Message);
            }
        }

        private async void ToggleLog_Unchecked(object sender, RoutedEventArgs e)
        {
            UiLog("Log désactivé");
            IsLoggingEnabled = false;

            try
            {
                await ServiceIpcClient.SetLogging(false);
                UiLog("Service : log désactivé");
            }
            catch (Exception ex)
            {
                UiLog("Erreur IPC SetLogging(false) : " + ex.Message);
            }
        }

        private async void ToggleEmail_Checked(object sender, RoutedEventArgs e)
        {
            UiLog("Envoi automatique du rapport activé");

            try
            {
                await ServiceIpcClient.SetEmailSending(true);
                UiLog("Service : envoi email activé");
            }
            catch (Exception ex)
            {
                UiLog("Erreur IPC SetEmailSending(true) : " + ex.Message);
            }
        }

        private async void ToggleEmail_Unchecked(object sender, RoutedEventArgs e)
        {
            UiLog("Envoi automatique du rapport désactivé");

            try
            {
                await ServiceIpcClient.SetEmailSending(false);
                UiLog("Service : envoi email désactivé");
            }
            catch (Exception ex)
            {
                UiLog("Erreur IPC SetEmailSending(false) : " + ex.Message);
            }
        }

        // HANDLERS SERVEUR WEB

        private async void ToggleWeb_Checked(object sender, RoutedEventArgs e)
        {
            UiLog("Serveur Web activé");

            try
            {
                await ServiceIpcClient.SetWebEnabled(true);
                UiLog("Service : serveur web activé");
            }
            catch (Exception ex)
            {
                UiLog("Erreur IPC SetWebEnabled(true) : " + ex.Message);
            }
        }

        private async void ToggleWeb_Unchecked(object sender, RoutedEventArgs e)
        {
            UiLog("Serveur Web désactivé");

            try
            {
                await ServiceIpcClient.SetWebEnabled(false);
                UiLog("Service : serveur web désactivé");
            }
            catch (Exception ex)
            {
                UiLog("Erreur IPC SetWebEnabled(false) : " + ex.Message);
            }
        }

        private async void txtWebPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingConfig)
                return;

            if (!int.TryParse(txtWebPort.Text, out int port))
                return;

            UiLog("Changement du port Web : " + port);

            try
            {
                await ServiceIpcClient.SetWebPort(port);
                UiLog("Service : port web mis à " + port);
            }
            catch (Exception ex)
            {
                UiLog("Erreur IPC SetWebPort : " + ex.Message);
            }
        }

        private void btnOpenWeb_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtWebPort.Text, out int port))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://localhost:{port}/",
                    UseShellExecute = true
                });

                UiLog("Ouverture du navigateur sur http://localhost:" + port);
            }
            catch (Exception ex)
            {
                UiLog("Erreur ouverture navigateur : " + ex.Message);
            }
        }

        private void UiLog(string msg)
        {
            if (!IsLoggingEnabled)
                return;

            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                Directory.CreateDirectory(folder);

                string file = Path.Combine(folder, "MediaMonitor.UI.log");

                File.AppendAllText(
                    file,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}"
                );
            }
            catch { }
        }

        // Wrapper sécurisé pour éviter les appels concurrents
        private async Task RefreshStateSafe()
        {
            if (_isRefreshing)
            {
                StaticUiLog("RefreshState ignoré (déjà en cours)");
                return;
            }

            _isRefreshing = true;
            try
            {
                await RefreshState();
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private async Task RefreshState()
        {
            StaticUiLog("RefreshState() appelé");

            try
            {
                var state = await ServiceIpcClient.GetState();

                if (state == null)
                {
                    StaticUiLog("state == null");
                    return;
                }

                StaticUiLog("openFiles count = " + state.openFiles.Count);

                _items.Clear();
                foreach (var item in state.openFiles)
                {
                    StaticUiLog("Ajout item : " + item.ClientName + " | " + item.FileName);
                    _items.Add(item);
                }

                LastImageText.Text = string.IsNullOrEmpty(state.lastImage)
                    ? "Dernière image ouverte : aucune"
                    : "Dernière image ouverte : " + state.lastImage;

                var history = await ServiceIpcClient.GetHistory();

                if (history != null)
                {
                    StaticUiLog("history count = " + history.Count);

                    _history.Clear();
                    foreach (var h in history)
                    {
                        StaticUiLog("Ajout historique : " + h.ClientName + " | " + h.FileName);
                        _history.Add(h);
                    }
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

                if (result)
                    MessageBox.Show("Rapport envoyé.");
                else
                    MessageBox.Show("Erreur lors de l'envoi du rapport.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur IPC : " + ex.Message);
            }
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
                MessageBox.Show("Veuillez entrer un login et un mot de passe.",
                                "Erreur",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            UiLog($"Mise à jour Web : port={port}, login={login}, pass=(hidden)");

            try
            {
                await ServiceIpcClient.SetWebPort(port);
                await ServiceIpcClient.SetWebCredentials(login, pass);

                MessageBox.Show("Paramètres Web mis à jour.",
                                "OK",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UiLog("Erreur IPC WebAll : " + ex.Message);
                MessageBox.Show("Erreur IPC : " + ex.Message,
                                "Erreur",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
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
                    FileName = $"http://localhost:{port}/backup",
                    UseShellExecute = true
                });

                UiLog("Ouverture du navigateur sur /backup");
            }
            catch (Exception ex)
            {
                UiLog("Erreur ouverture /backup : " + ex.Message);
                MessageBox.Show("Impossible d’ouvrir la page /backup.\n" + ex.Message,
                                "Erreur",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private void BackupOption_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingConfig)
                return;

            if (sender is not CheckBox chk)
                return;

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
                "MCEMonitor",
                "MediaMonitor.Web.config"
            );

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

            if (!found)
                lines.Add("RetentionDays=" + days);

            File.WriteAllLines(path, lines);
        }

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
                    "MCEMonitor",
                    "MediaMonitor.Web.config"
                );

                var lines = File.Exists(path)
                    ? File.ReadAllLines(path).ToList()
                    : new List<string>();

                bool foundUrl = false;
                bool foundUser = false;
                bool foundPass = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("DvbViewerUrl=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "DvbViewerUrl=" + url;
                        foundUrl = true;
                    }

                    if (lines[i].StartsWith("DvbViewerUser=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "DvbViewerUser=" + user;
                        foundUser = true;
                    }

                    if (lines[i].StartsWith("DvbViewerPass=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "DvbViewerPass=" + pass;
                        foundPass = true;
                    }
                }

                if (!foundUrl)  lines.Add("DvbViewerUrl=" + url);
                if (!foundUser) lines.Add("DvbViewerUser=" + user);
                if (!foundPass) lines.Add("DvbViewerPass=" + pass);

                File.WriteAllLines(path, lines);

                UiLog("Configuration DVBViewer sauvegardée dans Web.config");
            }
            catch (Exception ex)
            {
                UiLog("Erreur sauvegarde DVB : " + ex.Message);
            }
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
    }
}

