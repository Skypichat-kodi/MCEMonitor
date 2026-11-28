using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MCEMonitor.Languages;

namespace RomMonitor.UI
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Plage autorisée du serveur web de RomMonitor. Chaque module de la suite
        /// occupe la sienne : la passerelle HTTPS les retrouve en balayant ces plages.
        /// </summary>
        private const int PortWebMin = 12000;
        private const int PortWebMax = 12050;

        // ============================================================
        //  Barre de titre personnalisée (WindowStyle=None + WindowChrome)
        //  La barre Windows est supprimée ; Réduire / Agrandir / Fermer sont
        //  recréés dans l'en-tête de la fenêtre.
        // ============================================================

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr fenetre, int attribut, ref int valeur, int taille);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;   // Windows 11
        private const int DWMWCP_ROUND                   = 2;

        // ------------------------------------------------------------
        //  Agrandissement borne a la zone de travail
        //  Sans WindowStyle, Windows agrandit la fenetre sur le moniteur
        //  entier : elle recouvre la barre des taches et son bas devient
        //  inaccessible. WM_GETMINMAXINFO permet d'imposer la zone de
        //  travail du moniteur qui contient la fenetre.
        // ------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int  cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int  dwFlags;
        }

        private const int WM_GETMINMAXINFO        = 0x0024;
        private const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr fenetre, int drapeau);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr moniteur, ref MONITORINFO info);

        /// <summary>
        /// Coins arrondis : WindowChrome.CornerRadius est sans effet sous Windows 11,
        /// il faut le demander à DWM. Sous Windows 10 l'attribut est inconnu, l'appel
        /// est sans effet et les angles restent carrés.
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr handle = new WindowInteropHelper(this).Handle;

            try
            {
                int arrondi = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref arrondi, sizeof(int));
            }
            catch
            {
                // Windows 10 : rien à faire, la fenêtre reste à angles droits.
            }

            // L'agrandissement doit s'arrêter à la zone de travail.
            try { HwndSource.FromHwnd(handle)?.AddHook(Fenetre_WndProc); }
            catch { /* sans hook, la fenêtre s'agrandit sur le moniteur entier */ }
        }

        /// <summary>
        /// Borne la fenêtre agrandie à la zone de travail du moniteur courant, pour
        /// qu'elle ne passe pas sous la barre des tâches.
        /// </summary>
        private IntPtr Fenetre_WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool traite)
        {
            if (message != WM_GETMINMAXINFO) return IntPtr.Zero;

            IntPtr moniteur = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (moniteur == IntPtr.Zero) return IntPtr.Zero;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(moniteur, ref info)) return IntPtr.Zero;

            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            RECT ecran = info.rcMonitor;
            RECT zone  = info.rcWork;

            mmi.ptMaxPosition.X = zone.Left - ecran.Left;
            mmi.ptMaxPosition.Y = zone.Top  - ecran.Top;
            mmi.ptMaxSize.X     = zone.Right  - zone.Left;
            mmi.ptMaxSize.Y     = zone.Bottom - zone.Top;

            Marshal.StructureToPtr(mmi, lParam, true);
            traite = true;
            return IntPtr.Zero;
        }

        /// <summary>
        /// L'en-tête fait office de barre de titre : un clic déplace la fenêtre,
        /// un double-clic bascule agrandi / normal.
        /// Les boutons de l'en-tête gèrent leurs propres clics et n'atteignent
        /// donc jamais ce gestionnaire.
        /// </summary>
        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { /* bouton relâché avant le déplacement */ }
            }
        }

        private void ToggleWindowState()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void TitleBarMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void TitleBarMaximize_Click(object sender, RoutedEventArgs e) => ToggleWindowState();

        private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

        // ============================================================

        private readonly ObservableCollection<DiskViewModel> _disks = new();
        private readonly ObservableCollection<AlertViewModel> _alerts = new();

        private readonly DispatcherTimer _refreshTimer = null!;
        private bool _isRefreshing = false;
        private bool _loadingWebConfig = false;

        // NOUVEAU — sync avec RomMonitor.config
        private DateTime _lastConfigWriteTime = DateTime.MinValue;
        private long _lastConfigSize = -1;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            // ?? Empêche l'exécution directe
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || args[1] != "--from-mcem")
            {
                Boite.Show(this,
                    LanguageManager.Get("RomMonitor.UI ne peut être lancé que depuis MCEMonitor.") ?? "RomMonitor.UI ne peut être lancé que depuis MCEMonitor.",
                    LanguageManager.Get("Accès refusé") ?? "Accès refusé",
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
                var result = Boite.Show(this, 
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
                            Boite.Show(this,
                                (LanguageManager.Get("RomMonitor.Service.exe est introuvable dans :") ?? "RomMonitor.Service.exe est introuvable dans :") + "\n" + servicePath,
                                LanguageManager.Get("Erreur") ?? "Erreur",
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
                        Boite.Show(this,
                            (LanguageManager.Get("Impossible de démarrer RomMonitor.Service.exe :") ?? "Impossible de démarrer RomMonitor.Service.exe :") + "\n" + ex.Message,
                            LanguageManager.Get("Erreur") ?? "Erreur",
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

            LoadWatchConfig();
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
                CheckConfigFileChanged();
                
                var status = await RomMonitorIpcClient.GetStatus();

                if (status == null)
                {
                    StatusText.Text = LanguageManager.Get("Service RomMonitor non joignable") ?? "Service RomMonitor non joignable";
                    SetStatusIcon("/Resources/Icons/warning.png");
                    return;
                }

                StatusText.Text =
                    $"{LanguageManager.Get("Service actif") ?? "Service actif"}  |  " +
                    $"{LanguageManager.Get("Dernier check") ?? "Dernier check"} : {status.lastCheck:HH:mm:ss}  |  " +
                    $"{status.diskCount} {LanguageManager.Get("disque(s)") ?? "disque(s)"}  |  " +
                    $"{status.smartCount} SMART  |  " +
                    $"{status.alertCount} {LanguageManager.Get("alerte(s)") ?? "alerte(s)"}  |  " +
                    $"{LanguageManager.Get("Intervalle") ?? "Intervalle"} : {status.interval} min";

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

                // Recharge les seuils depuis RomMonitor.config
                LoadThresholds();

                _disks.Clear();

                if (disks != null)
                {
                    // 3. Construction d'un dictionnaire : DiskNumber ? (SMART)
                    // On apparie chaque SMART à un disque physique via son index
                    var diskNumberToSmart = new System.Collections.Generic.Dictionary<int, RomSmart>();

                    if (smart != null && smart.Count > 0)
                    {
                        // Pour chaque disque physique présent dans les partitions
                        // (déduit des physicalDiskNumber non-null), on cherche le SMART correspondant
                        var uniqueDiskNumbers = new System.Collections.Generic.HashSet<int>();

                        foreach (var d in disks)
                        {
                            if (d.physicalDiskNumber.HasValue)
                                uniqueDiskNumbers.Add(d.physicalDiskNumber.Value);
                        }

                        // Pour chaque disque physique, on cherche le SMART de même capacité physique
                        var usedSmartSerials = new System.Collections.Generic.HashSet<string>();

                        foreach (int diskNum in uniqueDiskNumbers)
                        {
                            // Trouver la taille du disque physique à partir des partitions
                            double physicalSize = 0;

                            foreach (var d in disks)
                            {
                                if (d.physicalDiskNumber == diskNum && d.physicalSizeGo > 0)
                                {
                                    physicalSize = d.physicalSizeGo;
                                    break;
                                }
                            }

                            if (physicalSize <= 0)
                                continue;

                            RomSmart? best = null;
                            double bestDelta = double.MaxValue;

                            foreach (var s in smart)
                            {
                                if (s.capacityGo <= 0) continue;
                                if (usedSmartSerials.Contains(s.serial ?? "")) continue;

                                double delta = Math.Abs(s.capacityGo - physicalSize) / physicalSize;

                                // Tolérance stricte (5%) car on compare maintenant des tailles de disques physiques
                                if (delta < 0.05 && delta < bestDelta)
                                {
                                    best = s;
                                    bestDelta = delta;
                                }
                            }

                            if (best != null)
                            {
                                diskNumberToSmart[diskNum] = best;
                                if (!string.IsNullOrEmpty(best.serial))
                                    usedSmartSerials.Add(best.serial);
                            }
                        }
                    }

                    // 4. Créer les ViewModels
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

                        // Match UNIQUEMENT par numéro de disque physique
                        if (d.physicalDiskNumber.HasValue &&
                            diskNumberToSmart.TryGetValue(d.physicalDiskNumber.Value, out var m))
                        {
                            match = m;
                        }

                        // Pas de fallback par taille. Si on n'a pas matché, on affiche N/A.

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
                StatusText.Text = (LanguageManager.Get("Erreur IPC :") ?? "Erreur IPC :") + " " + ex.Message;
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
            try
            {
                // 1) Demande au service de refaire un scan immédiatement
                StatusText.Text = LanguageManager.Get("Scan forcé en cours…") ?? "Scan forcé en cours…";
                SetStatusIcon("/Resources/Icons/warning.png");

                bool ok = await RomMonitorIpcClient.ForceScanAsync();

                if (!ok)
                {
                    StatusText.Text = LanguageManager.Get("Impossible de contacter le service RomMonitor.") ?? "Impossible de contacter le service RomMonitor.";
                    SetStatusIcon("/Resources/Icons/critical.png");
                    return;
                }

                // 2) Le service scanne en arrière-plan. On laisse un peu de temps
                //    avant de relire, pour laisser smartctl finir.
                //    On relit progressivement : 5 s, 10 s, 20 s, 40 s.
                int[] delays = { 5000, 5000, 10000, 20000 };

                foreach (int delay in delays)
                {
                    await Task.Delay(delay);
                    await RefreshSafe();
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = (LanguageManager.Get("Erreur lors du scan forcé :") ?? "Erreur lors du scan forcé :") + " " + ex.Message;
                SetStatusIcon("/Resources/Icons/warning.png");
            }
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
                Boite.Show(this, (LanguageManager.Get("Impossible d'ouvrir le dossier Logs.") ?? "Impossible d'ouvrir le dossier Logs.") + "\n" + ex.Message);
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
                    Boite.Show(this,
                        LanguageManager.Get("Aucun historique d'alertes disponible pour l'instant.") ?? "Aucun historique d'alertes disponible pour l'instant.",
                        LanguageManager.Get("Historique") ?? "Historique",
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
                Boite.Show(this, (LanguageManager.Get("Impossible d'ouvrir l'historique.") ?? "Impossible d'ouvrir l'historique.") + "\n" + ex.Message);
            }
        }

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = Boite.Show(this, 
                    "Voulez-vous vraiment vider tout l'historique des alertes ?\n" +
                    "Cette action est irréversible.",
                    "Vider l'historique",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                bool ok = await RomMonitorIpcClient.ClearAlertsAsync();

                if (ok)
                {
                    _alerts.Clear();
                    StatusText.Text = LanguageManager.Get("Historique des alertes vidé.") ?? "Historique des alertes vidé.";
                }
                else
                {
                    Boite.Show(this, 
                        "Impossible de vider l'historique (service non joignable ?).",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Boite.Show(this, (LanguageManager.Get("Erreur :") ?? "Erreur :") + " " + ex.Message);
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
                if (status == null) return;

                ToggleWeb.IsChecked = status.enabled;
                txtWebPort.Text = status.port.ToString();
                txtWebLogin.Text = status.username;

                // Mot de passe : priorité à la valeur IPC (si l'IPC l'envoie),
                // sinon fallback sur le fichier *.web.config
                string pass = status.password ?? "";

                if (string.IsNullOrEmpty(pass))
                {
                    string webConfigPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MCEMonitor",
                        "RomMonitor.web.config"
                    );

                    if (File.Exists(webConfigPath))
                    {
                        foreach (var line in File.ReadAllLines(webConfigPath))
                        {
                            if (line.StartsWith("WebPassword=", StringComparison.OrdinalIgnoreCase))
                            {
                                pass = line.Split('=', 2)[1].Trim();
                                break;
                            }
                        }
                    }
                }

                txtWebPassword.Password = pass;
                txtWebPasswordVisible.Text = pass;
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
                Boite.Show(this, 
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
                if (!int.TryParse(txtWebPort.Text, out int port) || port < PortWebMin || port > PortWebMax)
                {
                    Boite.Show(this,
                        string.Format(LanguageManager.Get("Port invalide : entier entre {0} et {1}.")
                                      ?? "Port invalide : entier entre {0} et {1}.", PortWebMin, PortWebMax),
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Boite.Show(this, (LanguageManager.Get("Erreur :") ?? "Erreur :") + " " + ex.Message);
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
                Boite.Show(this, (LanguageManager.Get("Impossible d'ouvrir le navigateur :") ?? "Impossible d'ouvrir le navigateur :") + " " + ex.Message);
            }
        }

        // ============================================================
        //  SYNCHRONISATION CONFIG (si modifiée par MCEMonitor)
        // ============================================================
        private void CheckConfigFileChanged()
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "RomMonitor.config");

                if (!File.Exists(configPath))
                    return;

                var fi = new FileInfo(configPath);

                if (fi.LastWriteTime == _lastConfigWriteTime && fi.Length == _lastConfigSize)
                    return;

                _lastConfigWriteTime = fi.LastWriteTime;
                _lastConfigSize = fi.Length;

                LoadWatchConfig();
            }
            catch { }
        }

        // ============================================================
        //  CHARGEMENT DES RÉGLAGES DE SURVEILLANCE
        // ============================================================
        private void LoadWatchConfig()
        {
            // Valeurs par défaut
            txtInterval.Text = "60";
            txtWarnPct.Text  = "15";
            txtCritPct.Text  = "5";
            txtCooldown.Text = "24";
            chkSmartAlert.IsChecked = true;

            // Surcharge disque : le pourcentage peut dépasser 100. PercentDiskTime
            // compte aussi le temps passé par les requêtes en attente, si bien qu un
            // disque débordé rapporte plusieurs centaines de pour cent.
            chkDiskIoAlert.IsChecked = true;
            txtDiskIoWarn.Text       = "150";
            txtDiskIoCrit.Text       = "400";
            txtDiskIoConfirm.Text    = "6";
            txtDiskIoInterval.Text   = "10";

            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "RomMonitor.config");

                if (!File.Exists(configPath))
                    return;

                foreach (var line in File.ReadAllLines(configPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string val = parts[1].Trim();

                    switch (key)
                    {
                        case "Interval":
                            txtInterval.Text = val;
                            break;

                        case "DiskSpaceWarnPercent":
                            txtWarnPct.Text = val;
                            break;

                        case "DiskSpaceCriticalPercent":
                            txtCritPct.Text = val;
                            break;

                        case "AlertCooldownHours":
                            txtCooldown.Text = val;
                            break;

                        case "AlertOnSmartFailure":
                            chkSmartAlert.IsChecked =
                                val.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;

                        case "AlertOnDiskIo":
                            chkDiskIoAlert.IsChecked =
                                val.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;

                        case "DiskIoWarnPercent":     txtDiskIoWarn.Text     = val; break;
                        case "DiskIoCriticalPercent": txtDiskIoCrit.Text     = val; break;
                        case "DiskIoConfirmTicks":    txtDiskIoConfirm.Text  = val; break;
                        case "DiskIoIntervalSeconds": txtDiskIoInterval.Text = val; break;
                    }
                }
            }
            catch { }
        }

        // ============================================================
        //  ENREGISTREMENT DES RÉGLAGES
        // ============================================================
        private async void btnSaveWatchConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1) Validation numérique
                if (!int.TryParse(txtInterval.Text.Trim(), out int interval) || interval < 15 || interval > 1440)
                {
                    Boite.Show(this, LanguageManager.Get("Fréquence de contrôle : entier entre 15 et 1440 (minutes).") ?? "Fréquence de contrôle : entier entre 15 et 1440 (minutes).",
                        "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(txtWarnPct.Text.Trim(), out int warnPct) || warnPct < 5 || warnPct > 100)
                {
                    Boite.Show(this, LanguageManager.Get("Seuil warning : entier entre 5 et 100 (%).") ?? "Seuil warning : entier entre 5 et 100 (%).",
                        "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(txtCritPct.Text.Trim(), out int critPct) || critPct < 1 || critPct > 100)
                {
                    Boite.Show(this, LanguageManager.Get("Seuil critique : entier entre 1 et 100 (%).") ?? "Seuil critique : entier entre 1 et 100 (%).",
                        "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(txtCooldown.Text.Trim(), out int cooldown) || cooldown < 1 || cooldown > 168)
                {
                    Boite.Show(this, LanguageManager.Get("Intervalle d'envoi des alertes : entier entre 1 et 168 (heures).") ?? "Intervalle d'envoi des alertes : entier entre 1 et 168 (heures).",
                        "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Surcharge disque
                if (!int.TryParse(txtDiskIoWarn.Text.Trim(), out int ioWarn) || ioWarn < 10 || ioWarn > 1000)
                {
                    Boite.Show(this,
                        LanguageManager.Get("Surcharge disque : seuil entre 10 et 1000 (%).") ?? "Surcharge disque : seuil entre 10 et 1000 (%).",
                        LanguageManager.Get("Valeur invalide") ?? "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(txtDiskIoCrit.Text.Trim(), out int ioCrit) || ioCrit < 20 || ioCrit > 1000)
                {
                    Boite.Show(this,
                        LanguageManager.Get("Surcharge disque : seuil critique entre 20 et 1000 (%).") ?? "Surcharge disque : seuil critique entre 20 et 1000 (%).",
                        LanguageManager.Get("Valeur invalide") ?? "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (ioWarn >= ioCrit)
                {
                    ioCrit = Math.Min(1000, ioWarn + 5);
                    txtDiskIoCrit.Text = ioCrit.ToString();

                    Boite.Show(this,
                        (LanguageManager.Get("Le seuil critique doit rester au-dessus du seuil d'alerte.") ?? "Le seuil critique doit rester au-dessus du seuil d'alerte.") + "\n"
                        + (LanguageManager.Get("Seuil critique ajusté automatiquement à") ?? "Seuil critique ajusté automatiquement à") + $" {ioCrit} %.",
                        LanguageManager.Get("Ajustement automatique") ?? "Ajustement automatique", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                if (!int.TryParse(txtDiskIoConfirm.Text.Trim(), out int ioConfirm) || ioConfirm < 1 || ioConfirm > 60)
                {
                    Boite.Show(this,
                        LanguageManager.Get("Surcharge disque : nombre de relevés entre 1 et 60.") ?? "Surcharge disque : nombre de relevés entre 1 et 60.",
                        LanguageManager.Get("Valeur invalide") ?? "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(txtDiskIoInterval.Text.Trim(), out int ioInterval) || ioInterval < 5 || ioInterval > 300)
                {
                    Boite.Show(this,
                        LanguageManager.Get("Surcharge disque : intervalle entre 5 et 300 secondes.") ?? "Surcharge disque : intervalle entre 5 et 300 secondes.",
                        LanguageManager.Get("Valeur invalide") ?? "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 2) Garde-fou ±3 entre warning et critique (aligné sur MCEMonitor)
                if (warnPct < critPct + 3)
                {
                    warnPct = Math.Min(100, critPct + 3);
                    txtWarnPct.Text = warnPct.ToString();

                    Boite.Show(this,
                        (LanguageManager.Get("Le seuil warning doit être au moins à 3 unités au-dessus du critique.") ?? "Le seuil warning doit être au moins à 3 unités au-dessus du critique.") + "\n" +
                        (LanguageManager.Get("Seuil warning ajusté automatiquement à") ?? "Seuil warning ajusté automatiquement à") + $" {warnPct} %.",
                        LanguageManager.Get("Ajustement automatique") ?? "Ajustement automatique",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                // 3) Lire les valeurs Web existantes pour les préserver
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "RomMonitor.config");

                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

                int    warnGo      = 10;
                int    critGo      = 5;

                bool   webEnabled  = true;
                int    webPort     = PortWebMin;
                string webUsername = "admin";
                string webPassword = "changeme";

                if (File.Exists(configPath))
                {
                    foreach (var line in File.ReadAllLines(configPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                            continue;

                        var parts = line.Split('=', 2);
                        if (parts.Length != 2) continue;

                        string key = parts[0].Trim();
                        string val = parts[1].Trim();

                        switch (key)
                        {
                            case "WebEnabled":  if (bool.TryParse(val, out bool we)) webEnabled = we; break;
                            case "WebPort":     if (int.TryParse(val, out int wp)) webPort = wp; break;
                            case "WebUsername": webUsername = val; break;
                            case "WebPassword": webPassword = val; break;
                            case "DiskSpaceWarnGo":     if (int.TryParse(val, out int wg)) warnGo = wg; break;
                            case "DiskSpaceCriticalGo": if (int.TryParse(val, out int cg)) critGo = cg; break;
                        }
                    }
                }

                // 4) Écrire le fichier complet (mêmes clés que MCEMonitor)
                var lines = new[]
                {
                    "# ============================================================",
                    "# RomMonitor.config",
                    "# Configuration du service RomMonitor",
                    "# ============================================================",
                    "",
                    "# Fréquence de contrôle (minutes)",
                    $"Interval={interval}",
                    "",
                    "# Seuil d'alerte espace disque (% libre)",
                    $"DiskSpaceWarnPercent={warnPct}",
                    $"DiskSpaceCriticalPercent={critPct}",
                    "",
                    "# Seuil d'alerte espace disque (Go libre)",
                    $"DiskSpaceWarnGo={warnGo}",
                    $"DiskSpaceCriticalGo={critGo}",
                    "",
                    "# Alertes email",
                    $"AlertOnSmartFailure={chkSmartAlert.IsChecked == true}".ToLowerInvariant(),
                    "AlertOnLowDiskSpace=true",
                    "",
                    "# Anti-spam : délai minimum entre 2 alertes email du même type (heures)",
                    $"AlertCooldownHours={cooldown}",
                    "",
                    "# Surcharge disque : alerte si un disque reste occupe au-dela du seuil",
                    $"AlertOnDiskIo={(chkDiskIoAlert.IsChecked == true).ToString().ToLowerInvariant()}",
                    $"DiskIoWarnPercent={ioWarn}",
                    $"DiskIoCriticalPercent={ioCrit}",
                    $"DiskIoConfirmTicks={ioConfirm}",
                    $"DiskIoIntervalSeconds={ioInterval}",
                    "",
                    "# Serveur Web",
                    $"WebEnabled={webEnabled.ToString().ToLower()}",
                    $"WebPort={webPort}",
                    $"WebUsername={webUsername}",
                    $"WebPassword={webPassword}"
                };

                File.WriteAllLines(configPath, lines);

                // 5) Mémoriser la signature pour ne pas se recharger soi-même
                var fi = new FileInfo(configPath);
                _lastConfigWriteTime = fi.LastWriteTime;
                _lastConfigSize      = fi.Length;

                // 6) Demander au service de recharger à chaud
                bool reloaded = await RomMonitorIpcClient.ReloadConfigAsync();

                if (reloaded)
                {
                    Boite.Show(this,
                        (LanguageManager.Get("Réglages de surveillance enregistrés.") ?? "Réglages de surveillance enregistrés.") + "\n\n" +
                        (LanguageManager.Get("Le service a rechargé la configuration à chaud.") ?? "Le service a rechargé la configuration à chaud."),
                        LanguageManager.Get("OK") ?? "OK",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    Boite.Show(this,
                        (LanguageManager.Get("Réglages enregistrés, mais le service n'a pas pu être notifié.") ?? "Réglages enregistrés, mais le service n'a pas pu être notifié.") + "\n" +
                        (LanguageManager.Get("Redémarrez-le pour appliquer les modifications.") ?? "Redémarrez-le pour appliquer les modifications."),
                        LanguageManager.Get("Attention") ?? "Attention",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Boite.Show(this, (LanguageManager.Get("Erreur :") ?? "Erreur :") + " " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private static void LoadThresholds()
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "RomMonitor.config");

                if (!File.Exists(configPath))
                    return;

                foreach (var line in File.ReadAllLines(configPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string val = parts[1].Trim();

                    switch (key)
                    {
                        case "DiskSpaceWarnPercent":
                            if (int.TryParse(val, out int wp)) DiskViewModel.WarnPercent = wp;
                            break;
                        case "DiskSpaceCriticalPercent":
                            if (int.TryParse(val, out int cp)) DiskViewModel.CriticalPercent = cp;
                            break;
                        case "DiskSpaceWarnGo":
                            if (int.TryParse(val, out int wg)) DiskViewModel.WarnGo = wg;
                            break;
                        case "DiskSpaceCriticalGo":
                            if (int.TryParse(val, out int cg)) DiskViewModel.CriticalGo = cg;
                            break;
                    }
                }
            }
            catch { /* silencieux */ }
        }        
    }
}