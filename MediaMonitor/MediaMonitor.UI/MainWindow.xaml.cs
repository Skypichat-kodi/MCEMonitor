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

            InitializeComponent();

            // 🔥 Correction : charger l’état email APRÈS que la fenêtre soit chargée
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
    }
}

