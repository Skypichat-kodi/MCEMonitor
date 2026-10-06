using System.Collections.ObjectModel;
using MCEMonitorClient.Maui.Pages;
using MCEMonitorClient.Maui.Services;
using MCEMonitorClient.Shared.Models;
#if ANDROID
using MCEMonitorClient.Maui.Platforms.Android.Services;
#endif

namespace MCEMonitorClient.Maui;

public partial class MainPage : ContentPage
{
    private const string PrefKeyServiceRunning = "service_running";

    private readonly ObservableCollection<ServerEntry> _servers = new();
    private bool _serviceRunning;

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
    }

    private void LoadServers()
    {
        _servers.Clear();
        var config = ConfigService.Load();
        foreach (var s in config.Servers)
            _servers.Add(s);
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

    private ServerEntry? GetSelected()
    {
        return serversList.SelectedItem as ServerEntry;
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var page = new ServerEditPage(null);
        await Navigation.PushModalAsync(page);
    }

    private async void OnEditClicked(object sender, EventArgs e)
    {
        var server = GetSelected();
        if (server == null)
        {
            await DisplayAlert("Info", "Sélectionnez un serveur.", "OK");
            return;
        }

        var page = new ServerEditPage(server);
        await Navigation.PushModalAsync(page);
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        var server = GetSelected();
        if (server == null)
        {
            await DisplayAlert("Info", "Sélectionnez un serveur.", "OK");
            return;
        }

        bool ok = await DisplayAlert("Confirmation",
            $"Supprimer le serveur \"{server.Name}\" ?", "Oui", "Non");

        if (!ok) return;

        var config = ConfigService.Load();
        var toRemove = config.Servers.FirstOrDefault(s => s.Id == server.Id);
        if (toRemove != null)
        {
            config.Servers.Remove(toRemove);
            ConfigService.Save(config);
            LoadServers();
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
        // La sélection est déjà stockée dans serversList.SelectedItem
    }

    private void OnServerTapped(object sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is ServerEntry entry)
        {
            serversList.SelectedItem = entry;
        }
    }
}