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

#if ANDROID
        UpdateBatteryButton();
#endif
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

    private async void OnExportClicked(object sender, EventArgs e)
    {
        bool ok = await ImportExportService.ExportAsync();

        if (ok)
            await DisplayAlert("Export", "Configuration exportée avec succès.", "OK");
        else
            await DisplayAlert("Export", "Export annulé ou échec.", "OK");
    }

    private async void OnImportClicked(object sender, EventArgs e)
    {
        var imported = await ImportExportService.ImportAsync();

        if (imported == null)
        {
            await DisplayAlert("Import", "Import annulé ou fichier invalide.", "OK");
            return;
        }

        bool confirm = await DisplayAlert(
            "Confirmation",
            $"Remplacer la configuration actuelle par celle du fichier ?\n\n" +
            $"Serveurs dans le fichier : {imported.Servers.Count}",
            "Oui, remplacer",
            "Annuler");

        if (!confirm) return;

        ConfigService.Save(imported);

        await DisplayAlert("Import", $"Configuration importée ({imported.Servers.Count} serveur(s)).", "OK");
    }

#if ANDROID
    private void UpdateBatteryButton()
    {
        var ctx = global::Android.App.Application.Context;

        if (Platforms.Android.Services.BatteryHelper.IsIgnoringBatteryOptimizations(ctx))
        {
            btnBattery.Text = "Optimisation batterie : OK";
            btnBattery.BackgroundColor = Color.FromArgb("#4CAF50");
        }
        else
        {
            btnBattery.Text = "Autoriser l'optimisation batterie";
            btnBattery.BackgroundColor = Color.FromArgb("#F44336");
        }
    }

    private void OnBatteryClicked(object sender, EventArgs e)
    {
        var ctx = global::Android.App.Application.Context;
        Platforms.Android.Services.BatteryHelper.RequestIgnoreBatteryOptimizations(ctx);

        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1), () =>
        {
            UpdateBatteryButton();
        });
    }

    private void OnStartupManagerClicked(object sender, EventArgs e)
    {
        var ctx = global::Android.App.Application.Context;
        Platforms.Android.Services.BatteryHelper.OpenStartupManager(ctx);
    }

    private void OnNotificationSettingsClicked(object sender, EventArgs e)
    {
        var ctx = global::Android.App.Application.Context;
        Platforms.Android.Services.BatteryHelper.OpenNotificationSettings(ctx);
    }
#endif
}