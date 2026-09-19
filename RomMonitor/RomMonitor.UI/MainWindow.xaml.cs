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
                // ============================================================
                //  1. Statut
                // ============================================================
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

                // ============================================================
                //  2. Récupérer disques + SMART
                // ============================================================
                var disks = await RomMonitorIpcClient.GetDisks();
                var smart = await RomMonitorIpcClient.GetSmart();

                _disks.Clear();

                if (disks != null)
                {
                    // ============================================================
                    //  3. Groupement par DiskNumber (si dispo)
                    // ============================================================
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

                    // ============================================================
                    //  4. Mapping DiskNumber ? SMART (avec fallbacks)
                    // ============================================================
                    var diskNumberToSmart = new System.Collections.Generic.Dictionary<int, RomSmart>();

                    if (smart != null && smart.Count > 0)
                    {
                        // --- CAS 1 : un seul SMART ? on l'associe à tous les disques
                        if (smart.Count == 1)
                        {
                            foreach (var kv in diskNumberToCapacity)
                                diskNumberToSmart[kv.Key] = smart[0];
                        }
                        else
                        {
                            // --- CAS 2 : plusieurs SMART ? match par capacité
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

                    // ============================================================
                    //  5. Créer les ViewModels des disques
                    // ============================================================
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

                        // Association SMART
                        RomSmart? match = null;

                        // Tentative 1 : via DiskNumber
                        if (d.physicalDiskNumber.HasValue &&
                            diskNumberToSmart.TryGetValue(d.physicalDiskNumber.Value, out var m))
                        {
                            match = m;
                        }

                        // Tentative 2 (FALLBACK) : si un seul SMART dispo
                        if (match == null && smart != null && smart.Count == 1)
                        {
                            match = smart[0];
                        }

                        // Tentative 3 (FALLBACK ULTIME) : le SMART le plus proche en capacité
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

                        // Appliquer le match
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
                StatusText.Text = "[!] Erreur IPC : " + ex.Message;
            }
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