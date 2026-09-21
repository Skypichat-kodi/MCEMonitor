using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MCEMonitor.Languages;

namespace RomMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<DiskViewModel> _disks = new();
        private readonly ObservableCollection<AlertViewModel> _alerts = new();

        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing = false;
        private bool _loadingWebConfig = false;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            // ?? Empêche l'exécution directe
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || args[1] != "--from-mcem")
            {
                MessageBox.Show(
                    "RomMonitor.UI ne peut être lancé que depuis MCEMonitor.",
                    "Accès refusé",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Application.Current.Shutdown();
                return;
            }

            // Vérifie que le service tourne
            bool serviceRunning = Process.GetProcessesByName("RomMonitor.Service").Length > 0;

            if (!serviceRunning)
            {
                var result = MessageBox.Show(
                    (LanguageManager.Get("RomMonitor.Service n'est pas en cours d'exécution.")
                        ?? "RomMonitor.Service n'est pas en cours d'exécution.")
                    + "\n\n" +
                    (LanguageManager.Get("Voulez-vous le démarrer maintenant ?")
                        ?? "Voulez-vous le démarrer maintenant ?"),
                    LanguageManager.Get("Service non démarré") ?? "Service non démarré",
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
                            "RomMonitor.Service.exe"
                        );

                        if (!File.Exists(servicePath))
                        {
                            MessageBox.Show(
                                "RomMonitor.Service.exe est introuvable dans :\n" + servicePath,
                                "Erreur",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                            Close();
                            return;
                        }

                        string lang = LanguageManager.CurrentLanguage ?? "fr-FR";

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = servicePath,
                            Arguments = $"-lang {lang}",
                            UseShellExecute = true
                        });

                        System.Threading.Thread.Sleep(1200);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Impossible de démarrer RomMonitor.Service.exe :\n" + ex.Message,
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

            // Démarre le Tray
            StartTrayIfNeeded();

            // Sources des tableaux
            DisksGrid.ItemsSource = _disks;
            AlertsGrid.ItemsSource = _alerts;

            // Timer de rafraîchissement (5s)
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += async (_, __) => await RefreshSafe();
            _refreshTimer.Start();

            // Premier refresh
            _ = RefreshSafe();

            // Charger la config Web
            _ = LoadWebConfig();
        }

        // ------------------------------------------------------------
        //  Démarrage du Tray
        // ------------------------------------------------------------
        private void StartTrayIfNeeded()
        {
            try
            {
                if (Process.GetProcessesByName("RomMonitor.Tray").Length > 0)
                    return;

                string trayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "MCEMonitor",
                    "RomMonitor.Tray.exe"
                );

                if (!File.Exists(trayPath))
                {
                    trayPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "MCEMonitor",
                        "RomMonitor.Tray.exe"
                    );
                }

                if (File.Exists(trayPath))
                {
                    string lang = LanguageManager.CurrentLanguage ?? "fr-FR";

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = trayPath,
                        Arguments = $"-lang {lang}",
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        // ------------------------------------------------------------
        //  Refresh sécurisé
        // ------------------------------------------------------------
        private async Task RefreshSafe()
        {
            if (_isRefreshing) return;

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
            try
            {
                // ============================================================
                //  1. Statut
                // ============================================================
                var status = await RomMonitorIpcClient.GetStatus();

                if (status == null)
                {
                    StatusText.Text = "Service RomMonitor non joignable";
                    SetStatusIcon("/Resources/Icons/warning.png");
                    return;
                }

                StatusText.Text =
                    $"Service actif  |  Dernier check : {status.lastCheck:HH:mm:ss}  |  " +
                    $"{status.diskCount} disque(s)  |  {status.smartCount} SMART  |  " +
                    $"{status.alertCount} alerte(s)  |  Intervalle : {status.interval} min";

                // ?? Icône selon l'état
                if (status.alertCount > 0)
                    SetStatusIcon("/Resources/Icons/warning.png");
                else
                    SetStatusIcon("/Resources/Icons/check.png");

                // ============================================================
                //  2. Récupérer disques + SMART
                // ============================================================
                var disks = await RomMonitorIpcClient.GetDisks();
                var smart = await RomMonitorIpcClient.GetSmart();

                _disks.Clear();

                if (disks != null)
                {
                    // 3. Groupement par DiskNumber
                    var diskNumberToCapacity = new System.Collections.Generic.Dictionary<int, double>();

                    foreach (var d in disks)
                    {
                        if (!d.physicalDiskNumber.HasValue) continue;

                        int num = d.physicalDiskNumber.Value;

                        if (d.physicalSizeGo > 0)
                            diskNumberToCapacity[num] = d.physicalSizeGo;
                        else
                        {
                            if (!diskNumberToCapacity.ContainsKey(num))
                                diskNumberToCapacity[num] = 0;
                            diskNumberToCapacity[num] += d.totalGo;
                        }
                    }

                    // 4. Mapping DiskNumber ? SMART
                    var diskNumberToSmart = new System.Collections.Generic.Dictionary<int, RomSmart>();

                    if (smart != null && smart.Count > 0)
                    {
                        if (smart.Count == 1)
                        {
                            foreach (var kv in diskNumberToCapacity)
                                diskNumberToSmart[kv.Key] = smart[0];
                        }
                        else
                        {
                            foreach (var kv in diskNumberToCapacity)
                            {
                                int num = kv.Key;
                                double totalCapacity = kv.Value;

                                if (totalCapacity <= 0) continue;

                                RomSmart? best = null;
                                double bestDelta = double.MaxValue;

                                foreach (var s in smart)
                                {
                                    if (s.capacityGo <= 0) continue;

                                    double delta = Math.Abs(s.capacityGo - totalCapacity) / totalCapacity;

                                    if (delta < 0.10 && delta < bestDelta)
                                    {
                                        best = s;
                                        bestDelta = delta;
                                    }
                                }

                                if (best != null)
                                    diskNumberToSmart[num] = best;
                            }
                        }
                    }

                    // 5. Créer les ViewModels des disques
                    foreach (var d in disks)
                    {
                        var vm = new DiskViewModel
                        {
                            Name = d.name ?? "",
                            Label = d.label ?? "",
                            DriveType = d.driveType ?? "",
                            TotalGo = d.totalGo,
                            FreeGo = d.freeGo,
                            FreePercent = d.freePercent
                        };

                        RomSmart? match = null;

                        if (d.physicalDiskNumber.HasValue &&
                            diskNumberToSmart.TryGetValue(d.physicalDiskNumber.Value, out var m))
                        {
                            match = m;
                        }

                        if (match == null && smart != null && smart.Count == 1)
                            match = smart[0];

                        if (match == null && smart != null && smart.Count > 0)
                        {
                            double bestDelta = double.MaxValue;

                            foreach (var s in smart)
                            {
                                if (s.capacityGo <= 0) continue;

                                double delta = Math.Abs(s.capacityGo - d.totalGo);
                                if (delta < bestDelta)
                                {
                                    bestDelta = delta;
                                    match = s;
                                }
                            }
                        }

                        if (match != null)
                        {
                            vm.SmartAvailable = true;
                            vm.SmartModel = match.model ?? "";
                            vm.SmartSerial = match.serial ?? "";
                            vm.SmartStatus = match.status ?? "N/A";
                            vm.SmartReason = match.statusReason ?? "";
                            vm.SmartTemperature = match.temperature;
                            vm.SmartPowerOnHours = match.powerOnHours;
                        }

                        vm.UpdateBrushes();
                        _disks.Add(vm);
                    }
                }

                // ============================================================
                //  6. Alertes
                // ============================================================
                var alerts = await RomMonitorIpcClient.GetAlerts();

                _alerts.Clear();

                if (alerts != null)
                {
                    foreach (var a in alerts.OrderByDescending(x => x.timestamp).Take(100))
                    {
                        var vm = new AlertViewModel
                        {
                            Timestamp = a.timestamp,
                            Type = a.type ?? "",
                            Severity = a.severity ?? "",
                            Target = a.target ?? "",
                            Message = a.message ?? "",
                            EmailSent = a.emailSent
                        };

                        vm.UpdateBrushes();
                        _alerts.Add(vm);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Erreur IPC : " + ex.Message;
                SetStatusIcon("/Resources/Icons/warning.png");
            }
        }

        // ============================================================
        //  Icône de statut
        // ============================================================
        private void SetStatusIcon(string iconPath)
        {
            try
            {
                imgStatusIcon.Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,," + iconPath));

                imgStatusIcon.Visibility = Visibility.Visible;
            }
            catch
            {
                imgStatusIcon.Visibility = Visibility.Collapsed;
            }
        }
        
        // ------------------------------------------------------------
        //  Boutons principaux
        // ------------------------------------------------------------
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshSafe();
        }

        private void OpenLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                if (!Directory.Exists(logFolder))
                    Directory.CreateDirectory(logFolder);

                Process.Start("explorer.exe", logFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir le dossier Logs.\n" + ex.Message);
            }
        }

        private void OpenHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string historyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "RomMonitor",
                    "alert_history.json"
                );

                if (!File.Exists(historyPath))
                {
                    MessageBox.Show(
                        "Aucun historique d'alertes disponible pour l'instant.",
                        "Historique",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = historyPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir l'historique.\n" + ex.Message);
            }
        }

        // ============================================================
        //  SERVEUR WEB
        // ============================================================

        private async Task LoadWebConfig()
        {
            try
            {
                _loadingWebConfig = true;

                var status = await RomMonitorIpcClient.GetWebStatus();

                if (status == null)
                    return;

                ToggleWeb.IsChecked = status.enabled;
                txtWebPort.Text = status.port.ToString();
                txtWebLogin.Text = status.username;

                // Mot de passe : lu depuis le fichier config local
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "RomMonitor.config"
                );

                if (File.Exists(configPath))
                {
                    foreach (var line in File.ReadAllLines(configPath))
                    {
                        if (line.StartsWith("WebPassword=", StringComparison.OrdinalIgnoreCase))
                        {
                            string pass = line.Split('=', 2)[1].Trim();
                            txtWebPassword.Password = pass;
                            txtWebPasswordVisible.Text = pass;
                            break;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                _loadingWebConfig = false;
            }
        }

        private async void ToggleWeb_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingWebConfig) return;
            await ApplyWebConfig();
        }

        private async void ToggleWeb_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_loadingWebConfig) return;
            await ApplyWebConfig();
        }

        private async void btnApplyWeb_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await ApplyWebConfig();

            if (ok)
            {
                MessageBox.Show(
                    LanguageManager.Get("Paramètres Web mis à jour.") ?? "Paramètres Web mis à jour.",
                    "OK",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async Task<bool> ApplyWebConfig()
        {
            try
            {
                if (!int.TryParse(txtWebPort.Text, out int port))
                {
                    MessageBox.Show("Port invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                string login = txtWebLogin.Text.Trim();

                string pass = txtWebPassword.Visibility == Visibility.Visible
                    ? txtWebPassword.Password
                    : txtWebPasswordVisible.Text;

                bool enabled = ToggleWeb.IsChecked == true;

                return await RomMonitorIpcClient.SetWebConfig(enabled, port, login, pass);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
                return false;
            }
        }

        private void btnShowWebPassword_Click(object sender, RoutedEventArgs e)
        {
            if (txtWebPassword.Visibility == Visibility.Visible)
            {
                // Afficher le mot de passe
                txtWebPasswordVisible.Text = txtWebPassword.Password;
                txtWebPassword.Visibility = Visibility.Collapsed;
                txtWebPasswordVisible.Visibility = Visibility.Visible;

                imgWebEye.Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Resources/Icons/eye-off.png"));
            }
            else
            {
                // Cacher le mot de passe
                txtWebPassword.Password = txtWebPasswordVisible.Text;
                txtWebPasswordVisible.Visibility = Visibility.Collapsed;
                txtWebPassword.Visibility = Visibility.Visible;

                imgWebEye.Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Resources/Icons/eye.png"));
            }
        }

        private void btnOpenWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(txtWebPort.Text, out int port))
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://localhost:{port}/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir le navigateur : " + ex.Message);
            }
        }
    }
}