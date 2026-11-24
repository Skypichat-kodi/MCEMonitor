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
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MCEMonitor.Languages;
using MediaMonitor.Core.Models;
using MediaMonitor.UI.Models;
using MediaMonitor.UI.Services;
using Microsoft.Win32;

namespace MediaMonitor.UI
{
    public partial class MainWindow : Window
    {
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

        private bool IsLoggingEnabled = false;
        public static Action<string> StaticUiLog = _ => { };

        private readonly ObservableCollection<MediaUsageItem> _items = new();
        private readonly ObservableCollection<MediaUsageItem> _history = new();

        // Onglet Backup
        private readonly ObservableCollection<MediaUsageItem> _backupAll      = new();
        private readonly ObservableCollection<MediaUsageItem> _backupFiltered = new();
        private BackupFileModel? _currentBackup;

        private readonly DispatcherTimer _refreshTimer = null!;
        private bool _isRefreshing = false;
        private bool _loadingConfig = false;

        // Pendant le chargement des réglages, cocher un ToggleButton déclenche son
        // événement Checked. Sans ce garde-fou, l'application réécrivait le fichier
        // qu'elle venait de lire, et échouait si l'écriture était refusée.
        private bool _chargementReglages;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            LoadDvbSettings();

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || args[1] != "--from-mcem")
            {
                Boite.Show(this,
                    LanguageManager.Get("MediaMonitor.UI ne peut être lancé que depuis MCEMonitor.")
                        ?? "MediaMonitor.UI ne peut être lancé que depuis MCEMonitor.",
                    LanguageManager.Get("Accès refusé") ?? "Accès refusé",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            ResetUiLog();

            bool serviceRunning = Process.GetProcessesByName("MediaMonitor.Service").Length > 0;
            if (!serviceRunning)
            {
                Boite.Show(this,
                    (LanguageManager.Get("Le service MediaMonitor n'est pas démarré.")
                        ?? "Le service MediaMonitor n'est pas démarré.") + "\n" +
                    (LanguageManager.Get("Veuillez lancer MCEMonitor pour le démarrer automatiquement.")
                        ?? "Veuillez lancer MCEMonitor pour le démarrer automatiquement."),
                    LanguageManager.Get("Service non démarré") ?? "Service non démarré",
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
                        "MCEMonitor", "MediaMonitor.web.config");

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
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
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
                LastImageText.Text = (LanguageManager.Get("Erreur IPC :") ?? "Erreur IPC :") + " " + ex.Message;
            }
        }

        private async void SendReportNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await ServiceIpcClient.SendReport();
                Boite.Show(this, result
                    ? (LanguageManager.Get("Rapport envoyé.") ?? "Rapport envoyé.")
                    : (LanguageManager.Get("Erreur lors de l'envoi du rapport.")
                        ?? "Erreur lors de l'envoi du rapport."));
            }
            catch (Exception ex)
            {
                Boite.Show(this, 
                    (LanguageManager.Get("Erreur IPC :") ?? "Erreur IPC :") + " " + ex.Message);
            }
        }

        private async void btnApplyWebAll_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtWebPort.Text, out int port))
            {
                Boite.Show(this, 
                    LanguageManager.Get("Port invalide.") ?? "Port invalide.",
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string login = txtWebLogin.Text.Trim();
            string pass = txtWebPassword.Password.Trim();

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
            {
                Boite.Show(this, 
                    LanguageManager.Get("Veuillez entrer un login et un mot de passe.") 
                        ?? "Veuillez entrer un login et un mot de passe.",
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                await ServiceIpcClient.SetWebPort(port);
                await ServiceIpcClient.SetWebCredentials(login, pass);
                Boite.Show(this, 
                    LanguageManager.Get("Paramètres Web mis à jour.") ?? "Paramètres Web mis à jour.",
                    LanguageManager.Get("OK") ?? "OK",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
                catch (Exception ex)
                {
                    UiLog("Erreur IPC WebAll : " + ex.Message);   // ← log, pas de trad
                    Boite.Show(this, 
                        (LanguageManager.Get("Erreur IPC :") ?? "Erreur IPC :") + " " + ex.Message,
                        LanguageManager.Get("Erreur") ?? "Erreur",
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
                Boite.Show(this, 
                    LanguageManager.Get("Port Web invalide.") ?? "Port Web invalide.",
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                UiLog("Erreur ouverture /backup : " + ex.Message);   // ← log
                Boite.Show(this, 
                    (LanguageManager.Get("Impossible d’ouvrir la page /backup.\n") 
                        ?? "Impossible d’ouvrir la page /backup.\n") + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                "MCEMonitor", "MediaMonitor.web.config");

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
            string configPath = @"C:\ProgramData\MCEMonitor\MediaMonitor.web.config";
            if (!File.Exists(configPath)) return;

            _chargementReglages = true;
            try
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    if (line.StartsWith("DvbViewerSwitch=", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line.Split('=')[1].Trim();
                        ToggleDvb.IsChecked = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            finally
            {
                _chargementReglages = false;
            }
        }

        private void SaveDvbSwitch()
        {
            // Rien à enregistrer si l'on vient de lire le fichier, ni si l'écriture
            // est refusée : mieux vaut une case sans effet qu'une interface qui plante.
            if (_chargementReglages)
                return;

            try
            {
                EcrireDvbSwitch();
            }
            catch (UnauthorizedAccessException)
            {
                // Droits insuffisants sur ProgramData : on ignore.
            }
            catch (IOException)
            {
                // Fichier verrouillé par un autre processus : idem.
            }
        }

        private void EcrireDvbSwitch()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor", "MediaMonitor.web.config");

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

        private async void btnApplyDvb_Click(object sender, RoutedEventArgs e)
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
                    "MCEMonitor", "MediaMonitor.web.config");

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

                // ⚡ Propagation à chaud vers le service (sans redémarrage)
                bool ok = await ServiceIpcClient.SetDvbConfig(url, user, pass);

                if (ok)
                    UiLog("Configuration DVBViewer sauvegardée + propagée au service");
                else
                    UiLog("Configuration DVBViewer sauvegardée (⚠️ propagation IPC échouée)");
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
                    BackupInfoText.Text = LanguageManager.Get("Aucun backup disponible.") ?? "Aucun backup disponible.";
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

            // ─── Filtre par type ──────────────────────────────────────
            string type = (BackupFilterType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";

            items = type switch
            {
                "audio" => items.Where(i => i.MediaType.Equals("audio", StringComparison.OrdinalIgnoreCase)),
                "serie" => items.Where(i => i.MediaType.Equals("serie", StringComparison.OrdinalIgnoreCase)),
                "video" => items.Where(i => i.MediaType.Equals("video", StringComparison.OrdinalIgnoreCase)),
                "image" => items.Where(i => i.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase)),
                "rec"   => items.Where(i => i.MediaType.StartsWith("rec", StringComparison.OrdinalIgnoreCase)),
                "tv"    => items.Where(i => i.MediaType.Equals("tv", StringComparison.OrdinalIgnoreCase)),
                _       => items
            };

            // ─── Filtre par client ────────────────────────────────────
            string client = BackupFilterClient.SelectedItem?.ToString() ?? "all";
            if (client != "all" && client != "Tous")
                items = items.Where(i => i.ClientDisplay != null &&
                                         i.ClientDisplay.Equals(client, StringComparison.OrdinalIgnoreCase));

            // ─── Filtre par date ──────────────────────────────────────
            string dateF = (BackupFilterDate.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            DateTime now = DateTime.Now;
            items = dateF switch
            {
                "today"     => items.Where(i => i.Timestamp.Date == now.Date),
                "yesterday" => items.Where(i => i.Timestamp.Date == now.AddDays(-1).Date),
                "7"         => items.Where(i => i.Timestamp >= now.AddDays(-7)),
                "30"        => items.Where(i => i.Timestamp >= now.AddDays(-30)),
                _           => items
            };

            _backupFiltered.Clear();
            foreach (var it in items) _backupFiltered.Add(it);
        }

        private void btnRefreshBackup_Click(object sender, RoutedEventArgs e) => _ = InitBackupTabAsync();

        private void btnBackupPdf_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBackup == null)
            {
                Boite.Show(this, 
                    LanguageManager.Get("Aucun backup à exporter.") ?? "Aucun backup à exporter.",
                    LanguageManager.Get("Info") ?? "Info",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                Boite.Show(this, 
                    LanguageManager.Get("PDF généré avec succès.") ?? "PDF généré avec succès.",
                    LanguageManager.Get("OK") ?? "OK",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Boite.Show(this, 
                    (LanguageManager.Get("Erreur génération PDF :") ?? "Erreur génération PDF :") 
                        + " " + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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

            if (Boite.Show(this, msg, "Confirmation",
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
                Boite.Show(this, 
                    (LanguageManager.Get("Erreur purge :") ?? "Erreur purge :") 
                        + " " + ex.Message);
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
                Boite.Show(this,
                    (LanguageManager.Get("Impossible d'obtenir les informations du fichier.")
                        ?? "Impossible d'obtenir les informations du fichier.") + "\n\n" +
                    (LanguageManager.Get("Chemin :") ?? "Chemin :") + " " + path,
                    LanguageManager.Get("Erreur IPC") ?? "Erreur IPC",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (json.Contains("\"error\""))
            {
                Boite.Show(this,
                    (LanguageManager.Get("Erreur renvoyée par le serveur :")
                        ?? "Erreur renvoyée par le serveur :") + "\n\n" + json,
                    LanguageManager.Get("Erreur IPC") ?? "Erreur IPC",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MediaUsageItem? fullInfo = null;
            try { fullInfo = System.Text.Json.JsonSerializer.Deserialize<MediaUsageItem>(json); }
            catch (Exception ex)
            {
                Boite.Show(this,
                    (LanguageManager.Get("Erreur de désérialisation :")
                        ?? "Erreur de désérialisation :") + " " + ex.Message,
                    LanguageManager.Get("Erreur JSON") ?? "Erreur JSON",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

                if (fullInfo == null)
                {
                    Boite.Show(this, 
                        LanguageManager.Get("Impossible d'obtenir les informations du fichier.") 
                            ?? "Impossible d'obtenir les informations du fichier.",
                        LanguageManager.Get("Erreur IPC") ?? "Erreur IPC",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ShowInfoPopup(fullInfo);
            }
            catch (Exception ex)
            {
                Boite.Show(this, 
                    (LanguageManager.Get("Erreur lors de l'analyse du fichier :") 
                        ?? "Erreur lors de l'analyse du fichier :") + " " + ex.Message);
            }
        }
    }
}