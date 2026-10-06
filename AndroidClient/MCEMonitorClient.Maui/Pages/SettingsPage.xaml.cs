using MCEMonitorClient.Maui.Services;

namespace MCEMonitorClient.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();

        var config = ConfigService.Load();
        txtInterval.Text = config.PollIntervalSeconds.ToString();
        swNotify.IsToggled = config.NotifyOnStateChange;
        swSound.IsToggled = config.PlaySoundOnCritical;
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (!int.TryParse(txtInterval.Text, out int interval) || interval < 15)
        {
            await DisplayAlert("Erreur", "Intervalle minimum : 15 secondes.", "OK");
            return;
        }

        var config = ConfigService.Load();
        config.PollIntervalSeconds = interval;
        config.NotifyOnStateChange = swNotify.IsToggled;
        config.PlaySoundOnCritical = swSound.IsToggled;
        ConfigService.Save(config);

        await Navigation.PopModalAsync();
    }
}