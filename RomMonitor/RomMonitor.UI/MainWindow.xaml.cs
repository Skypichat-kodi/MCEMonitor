using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MediaMonitor.Core.Language;

namespace RomMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<DiskViewModel> _disks = new();
        private readonly ObservableCollection<AlertViewModel> _alerts = new();

        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing = false;

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
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = trayPath,
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
                // 1. Statut
                var status = await RomMonitorIpcClient.GetStatus();

                if (status == null)
                {
                    StatusText.Text = "[!] Service RomMonitor non joignable";
                    return;
                }

                StatusText.Text =
                    $"Service actif  |  Dernier check : {status.lastCheck:HH:mm:ss}  |  " +
                    $"{status.diskCount} disque(s)  |  {status.smartCount} SMART  |  " +
                    $"{status.alertCount} alerte(s)  |  Intervalle : {status.interval} min";

                // 2. Disques
                var disks = await RomMonitorIpcClient.GetDisks();
                var smart = await RomMonitorIpcClient.GetSmart();

                _disks.Clear();

                if (disks != null)
                {
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

                        // Associer le SMART par Serial (mapping WMI)
                        if (smart != null && smart.Count > 0)
                        {
                            RomSmart? match = null;

                            // Match par serial (le plus fiable)
                            if (!string.IsNullOrEmpty(d.physicalSerial))
                            {
                                string wmiSerial = Normalize(d.physicalSerial);

                                match = smart.FirstOrDefault(s =>
                                    !string.IsNullOrEmpty(s.serial) &&
                                    Normalize(s.serial).Contains(wmiSerial));
                            }

                            // Fallback : match par DiskNumber (pour /dev/sdX)
                            if (match == null && d.physicalDiskNumber.HasValue)
                            {
                                // /dev/sda = Disk 0, /dev/sdb = Disk 1...
                                // (souvent mais pas toujours)
                                char expectedLetter = (char)('a' + d.physicalDiskNumber.Value);
                                string expectedDevice = "/dev/sd" + expectedLetter;

                                match = smart.FirstOrDefault(s =>
                                    s.device?.Equals(expectedDevice, StringComparison.OrdinalIgnoreCase) == true);
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
                        }
                        
                        // Mise à jour des Brush
                        vm.UpdateBrushes();

                        _disks.Add(vm);
                    }
                }

                // 3. Alertes
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
                StatusText.Text = "[!] Erreur IPC : " + ex.Message;

                try
                {
                    string logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MCEMonitor",
                        "Logs",
                        "RomMonitor.UI.crash.log"
                    );

                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RefreshState: {ex.Message}\n{ex.StackTrace}\n\n");
                }
                catch { }
            }
        }
        
        /// <summary>
        /// Normalise un numéro de série pour comparaison.
        /// </summary>
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            return s.ToUpperInvariant()
                .Replace("-", "")
                .Replace(" ", "")
                .Replace("_", "")
                .Trim();
        }        

        // ------------------------------------------------------------
        //  Boutons
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
    }
}