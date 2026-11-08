using MCEMonitorClient.Maui.Services;
using MCEMonitorClient.Maui.ViewModels;

namespace MCEMonitorClient.Maui.Pages;

[QueryProperty(nameof(ServerId), "id")]
public partial class MediaMonitorDetailPage : ContentPage
{
    private MediaMonitorDetailViewModel? _vm;

    public string ServerId { get; set; } = "";

    public MediaMonitorDetailPage()
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

            _vm = new MediaMonitorDetailViewModel(server);
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

    private void OnFilterAllClicked(object sender, EventArgs e)    => _vm?.SetFilter("all");
    private void OnFilterAudioClicked(object sender, EventArgs e)  => _vm?.SetFilter("audio");
    private void OnFilterSerieClicked(object sender, EventArgs e)  => _vm?.SetFilter("serie");
    private void OnFilterVideoClicked(object sender, EventArgs e)  => _vm?.SetFilter("video");
    private void OnFilterImageClicked(object sender, EventArgs e)  => _vm?.SetFilter("image");
    private void OnFilterTvRecClicked(object sender, EventArgs e)  => _vm?.SetFilter("tvrec");
}