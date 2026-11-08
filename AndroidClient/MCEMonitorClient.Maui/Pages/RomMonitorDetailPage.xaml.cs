using MCEMonitorClient.Maui.Services;
using MCEMonitorClient.Maui.ViewModels;

namespace MCEMonitorClient.Maui.Pages;

[QueryProperty(nameof(ServerId), "id")]
public partial class RomMonitorDetailPage : ContentPage
{
    private RomMonitorDetailViewModel? _vm;

    public string ServerId { get; set; } = "";

    public RomMonitorDetailPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_vm == null)
        {
            var config = await ConfigService.LoadAsync();
            var server = config.Servers.FirstOrDefault(s => s.Id == ServerId);

            if (server == null)
            {
                await DisplayAlert("Erreur", "Serveur introuvable.", "OK");
                await Navigation.PopAsync();
                return;
            }

            _vm = new RomMonitorDetailViewModel(server);
            BindingContext = _vm;

            Title = server.Name;
        }

        await _vm.StartAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm?.Stop();
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        if (_vm != null)
            await _vm.RefreshManuallyAsync();

        refreshView.IsRefreshing = false;
    }
}