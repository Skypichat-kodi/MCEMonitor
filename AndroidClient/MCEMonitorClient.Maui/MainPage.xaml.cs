using System.Collections.ObjectModel;
using MCEMonitorClient.Maui.Pages;
using MCEMonitorClient.Maui.Services;
using MCEMonitorClient.Maui.ViewModels;
using MCEMonitorClient.Shared.Models;
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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadServers();
        UpdateServiceStatus();
        RefreshStatusColors();

        // Timer pour rafraîchir les pastilles toutes les 2s
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

    private void LoadServers()
    {
        _servers.Clear();
        var config = ConfigService.Load();
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
    }

    private async void OnEditClicked(object sender, EventArgs e)
    {
        var vm = GetSelected();
        if (vm == null)
        {
            await DisplayAlert("Info", "Sélectionnez un serveur.", "OK");
            return;
        }

        var page = new ServerEditPage(vm.Server);
        await Navigation.PushModalAsync(page);
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        var vm = GetSelected();
        if (vm == null)
        {
            await DisplayAlert("Info", "Sélectionnez un serveur.", "OK");
            return;
        }

        bool ok = await DisplayAlert("Confirmation",
            $"Supprimer le serveur \"{vm.Name}\" ?", "Oui", "Non");

        if (!ok) return;

        var config = ConfigService.Load();
        var toRemove = config.Servers.FirstOrDefault(s => s.Id == vm.Server.Id);
        if (toRemove != null)
        {
            config.Servers.Remove(toRemove);
            ConfigService.Save(config);
            LoadServers();
            RefreshStatusColors();
        }
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
        // La sélection est stockée dans serversList.SelectedItem
    }

    private void OnServerTapped(object sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is ServerItemViewModel vm)
        {
            serversList.SelectedItem = vm;
        }
    }
}