using System.Reflection;
using System.Collections.ObjectModel;
using MCEMonitorClient.Maui.Pages;
using MCEMonitorClient.Maui.Services;
using MCEMonitorClient.Maui.ViewModels;
using CommunityToolkit.Maui.Views;
using MCEMonitorClient.Shared.Models;
using CommunityToolkit.Maui;
#if ANDROID
using MCEMonitorClient.Maui.Platforms.Android.Services;
#endif

namespace MCEMonitorClient.Maui;

public partial class MainPage : ContentPage
{
    private const string PrefKeyServiceRunning = "service_running";

    private readonly ObservableCollection<ServerItemViewModel> _servers = new();
    private bool _serviceRunning;
    private IDispatcherTimer? _refreshTimer;

    public MainPage()
    {
        InitializeComponent();
        serversList.ItemsSource = _servers;
        _serviceRunning = Preferences.Get(PrefKeyServiceRunning, false);

        // --- Mise à jour dynamique de la version ---
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        string fullVersion = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "Inconnue";

        // GitVersion ajoute souvent des métadonnées après '+' (ex: "1.2.3+5.Branch.main.Sha.abc")
        // On coupe pour n'afficher que la partie lisible
        int plusIndex = fullVersion.IndexOf('+');
        string version = plusIndex > 0 ? fullVersion.Substring(0, plusIndex) : fullVersion;

        lblAppVersion.Text = $"v{version}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_servers.Count == 0)
            await LoadServersAsync();

        UpdateServiceStatus();
        RefreshStatusColors();

        _refreshTimer = Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick += (s, e) => RefreshStatusColors();
        _refreshTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    private async Task LoadServersAsync()
    {
        _servers.Clear();
        var config = await ConfigService.LoadAsync();
        foreach (var s in config.Servers)
            _servers.Add(new ServerItemViewModel(s));
    }

    private void RefreshStatusColors()
    {
        foreach (var vm in _servers)
        {
            if (!vm.Server.Enabled)
            {
                vm.StatusColor = Color.FromArgb("#4C4C4C");
                continue;
            }

            string status = PollingState.GetStatus(vm.Server.Id);

            vm.StatusColor = status switch
            {
                "ok"       => Color.FromArgb("#6CCB5F"),
                "warning"  => Color.FromArgb("#FFB900"),
                "critical" => Color.FromArgb("#FF6347"),
                "offline"  => Color.FromArgb("#888888"),
                _          => Color.FromArgb("#4C4C4C")
            };
        }
    }

    private void UpdateServiceStatus()
    {
        btnToggleService.Text = _serviceRunning ? "Arrêter" : "Démarrer";
        btnToggleService.BackgroundColor = _serviceRunning
            ? Color.FromArgb("#F44336")
            : Color.FromArgb("#4CAF50");

        lblServiceStatus.Text = _serviceRunning
            ? $"Service actif — {_servers.Count} serveur(s)"
            : "Service arrêté";
    }

    private ServerItemViewModel? GetSelected()
    {
        return serversList.SelectedItem as ServerItemViewModel;
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var page = new ServerEditPage(null);
        await Navigation.PushModalAsync(page);

        // Rafraîchir au retour
        page.Disappearing += async (_, __) => await LoadServersAsync();
    }

    private async void OnOptionsClicked(object sender, EventArgs e)
    {
        var page = new SettingsPage();
        await Navigation.PushModalAsync(page);
    }

    private async void OnToggleServiceClicked(object sender, EventArgs e)
    {
    #if ANDROID
        var ctx = Android.App.Application.Context;

        if (_serviceRunning)
        {
            ServiceHelper.Stop(ctx);
            _serviceRunning = false;
        }
        else
        {
            ServiceHelper.Start(ctx);
            _serviceRunning = true;
        }

        Preferences.Set(PrefKeyServiceRunning, _serviceRunning);
        UpdateServiceStatus();
    #endif
        await Task.CompletedTask;
    }

    private void OnServerSelected(object sender, SelectionChangedEventArgs e)
    {
        // Plus utilisé : on passe par OnServerTapped + menu contextuel
    }

    private async void OnServerTapped(object sender, TappedEventArgs e)
    {
        if (sender is not Border border || border.BindingContext is not ServerItemViewModel vm)
            return;

        serversList.SelectedItem = vm;

        // Déterminer si le serveur est online (état connu du dernier poll)
        string knownStatus = PollingState.GetStatus(vm.Server.Id);
        bool online = knownStatus != "offline" && knownStatus != "unknown";

        // Afficher la popup
        var popup = new Pages.ServerActionsPopup(vm.Name, vm.Server.ServiceType, online);
        await this.ShowPopupAsync(popup);

        // Traiter l'action choisie
        switch (popup.SelectedAction)
        {
            case "Consulter":
                await ConsumeServerAsync(vm);
                break;
            case "Modifier":
                await EditServerAsync(vm);
                break;
            case "Supprimer":
                await DeleteServerAsync(vm);
                break;
            // "" (Annuler) ou null : rien
        }
    }
    
    private async Task ConsumeServerAsync(ServerItemViewModel vm)
    {
        // Vérification fraîche (l'état de la popup peut être obsolète de quelques secondes)
        bool online = await IsServerReachableAsync(vm.Server);

        if (!online)
        {
            await DisplayAlert(
                "Serveur hors ligne",
                $"« {vm.Name} » ne répond plus. Vérifiez la connexion réseau.",
                "OK");
            return;
        }

        // Navigation
        string route = Pages.ServerDetailRouter.GetRouteForService(vm.Server.ServiceType, vm.Server.Id);

        if (!string.IsNullOrEmpty(route))
        {
            await Shell.Current.GoToAsync(route);
        }
        else
        {
            await DisplayAlert(
                "Bientôt disponible",
                $"La page de détail pour « {vm.Server.ServiceType} » n'est pas encore disponible.",
                "OK");
        }
    }

    private async Task<bool> IsServerReachableAsync(ServerEntry server)
    {
        if (server == null || string.IsNullOrWhiteSpace(server.FullUrl))
            return false;

        try
        {
            using var http = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            if (!string.IsNullOrEmpty(server.Username))
            {
                string raw = $"{server.Username}:{server.Password}";
                string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", b64);
            }

            using var resp = await http.GetAsync($"{server.FullUrl}/api/summary");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task EditServerAsync(ServerItemViewModel vm)
    {
        var page = new ServerEditPage(vm.Server);
        await Navigation.PushModalAsync(page);

        // Rafraîchir au retour
        page.Disappearing += async (_, __) => await LoadServersAsync();
    }

    private async Task DeleteServerAsync(ServerItemViewModel vm)
    {
        bool ok = await DisplayAlert(
            "Confirmation",
            $"Supprimer le serveur \"{vm.Name}\" ?",
            "Oui", "Non");

        if (!ok) return;

        var config = await ConfigService.LoadAsync();
        var toRemove = config.Servers.FirstOrDefault(s => s.Id == vm.Server.Id);
        if (toRemove != null)
        {
            config.Servers.Remove(toRemove);
            await ConfigService.SaveAsync(config);
            await LoadServersAsync();
            RefreshStatusColors();
        }
    }
}