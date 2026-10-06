using MCEMonitorClient.Maui.Services;
using MCEMonitorClient.Shared.Models;

namespace MCEMonitorClient.Maui.Pages;

public partial class ServerEditPage : ContentPage
{
    private readonly ServerEntry? _existing;

    public ServerEditPage(ServerEntry? existing)
    {
        InitializeComponent();

        _existing = existing;

        if (existing != null)
        {
            lblTitle.Text = "Modifier le serveur";
            txtName.Text = existing.Name;
            txtUrl.Text = existing.BaseUrl;
            txtPort.Text = existing.Port.ToString();
            txtUsername.Text = existing.Username;
            txtPassword.Text = existing.Password;
            swEnabled.IsToggled = existing.Enabled;

            int idx = cmbType.ItemsSource?.IndexOf(existing.ServiceType) ?? -1;
            if (idx >= 0) cmbType.SelectedIndex = idx;
            else cmbType.SelectedIndex = 0;
        }
        else
        {
            cmbType.SelectedIndex = 0;
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        string name = txtName.Text?.Trim() ?? "";
        string url = txtUrl.Text?.Trim() ?? "";
        string portStr = txtPort.Text?.Trim() ?? "8083";

        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert("Erreur", "Le nom est obligatoire.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            await DisplayAlert("Erreur", "L'URL est obligatoire.", "OK");
            return;
        }

        if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
        {
            await DisplayAlert("Erreur", "Port invalide.", "OK");
            return;
        }

        var config = ConfigService.Load();

        if (_existing == null)
        {
            var entry = new ServerEntry
            {
                Name = name,
                BaseUrl = url.TrimEnd('/'),
                Port = port,
                Username = txtUsername.Text?.Trim() ?? "",
                Password = txtPassword.Text ?? "",
                ServiceType = cmbType.SelectedItem?.ToString() ?? "SystemMonitor",
                Enabled = swEnabled.IsToggled
            };
            config.Servers.Add(entry);
        }
        else
        {
            _existing.Name = name;
            _existing.BaseUrl = url.TrimEnd('/');
            _existing.Port = port;
            _existing.Username = txtUsername.Text?.Trim() ?? "";
            _existing.Password = txtPassword.Text ?? "";
            _existing.ServiceType = cmbType.SelectedItem?.ToString() ?? "SystemMonitor";
            _existing.Enabled = swEnabled.IsToggled;
        }

        ConfigService.Save(config);
        await Navigation.PopModalAsync();
    }
}