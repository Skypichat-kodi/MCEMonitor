using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MediaMonitor.UI.Services;
using MediaMonitor.Core.Models;

namespace MediaMonitor.UI
{
    public partial class MainWindow : Window
    {
        private bool IsLoggingEnabled = false;

        public static Action<string> StaticUiLog;

        private readonly ObservableCollection<MediaUsageItem> _items = new();
        private readonly ObservableCollection<MediaUsageItem> _history = new();

        private readonly DispatcherTimer _refreshTimer;

        public MainWindow()
        {
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

            // 🔥 IMPORTANT : InitializeComponent AVANT TOUT ACCÈS AUX CONTRÔLES
            InitializeComponent();

            // 🔥 Charger l’état email APRÈS chargement de la fenêtre
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
            _refreshTimer.Tick += async (_, __) => await RefreshState();
            _refreshTimer.Start();

            _ = RefreshState();

            Loaded += async (_, __) =>
            {
                try
                {
                    ToggleWeb.IsChecked = await ServiceIpcClient.GetWebEnabled();
                    txtWebPort.Text = (await ServiceIpcClient.GetWebPort()).ToString();
                }
                catch (Exception ex)
                {
                    UiLog("Erreur initialisation Serveur Web : " + ex.Message);
                }
            };
        }

        // 🔥 Nouvelle méthode appelée quand la fenêtre est prête
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEmailSwitch();
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

        // 🔥 HANDLERS SERVEUR WEB

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

        private async void txtWebPort_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
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
        private async void btnApplyWebPort_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtWebPort.Text, out int port))
            {
                MessageBox.Show("Port invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await ServiceIpcClient.SetWebPort(port);

            MessageBox.Show("Port mis à jour.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

