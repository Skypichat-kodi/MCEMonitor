using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Linq;
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
            // Récupère la même instance dans la liste fraîchement chargée
            var existing = config.Servers.FirstOrDefault(s => s.Id == _existing!.Id);

            if (existing != null)
            {
                existing.Name = name;
                existing.BaseUrl = url.TrimEnd('/');
                existing.Port = port;
                existing.Username = txtUsername.Text?.Trim() ?? "";
                existing.Password = txtPassword.Text ?? "";
                existing.ServiceType = cmbType.SelectedItem?.ToString() ?? "SystemMonitor";
                existing.Enabled = swEnabled.IsToggled;
            }
            else
            {
                // Cas improbable : le serveur a été supprimé entre-temps
                await DisplayAlert("Erreur", "Serveur introuvable.", "OK");
                return;
            }
        }

        ConfigService.Save(config);
        await Navigation.PopModalAsync();
    }
    
    private async void OnTestClicked(object sender, EventArgs e)
    {
        string url = txtUrl.Text?.Trim() ?? "";
        string portStr = txtPort.Text?.Trim() ?? "8083";
        string user = txtUsername.Text?.Trim() ?? "";
        string pass = txtPassword.Text ?? "";

        if (string.IsNullOrWhiteSpace(url))
        {
            lblTestResult.TextColor = Color.FromArgb("#FF6347");
            lblTestResult.Text = "URL vide.";
            return;
        }

        if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
        {
            lblTestResult.TextColor = Color.FromArgb("#FF6347");
            lblTestResult.Text = "Port invalide.";
            return;
        }

        // Construit l'URL de test
        string baseUrl = url.TrimEnd('/');
        if (!baseUrl.Contains("://"))
            baseUrl = "http://" + baseUrl;

        string testUrl = $"{baseUrl}:{port}/api/summary";

        btnTest.IsEnabled = false;
        btnTest.Text = "Test en cours...";
        lblTestResult.TextColor = Color.FromArgb("#B4B4B4");
        lblTestResult.Text = "Connexion à " + testUrl;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

            if (!string.IsNullOrEmpty(user))
            {
                string raw = $"{user}:{pass}";
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", b64);
            }

            using var resp = await http.GetAsync(testUrl);

            if (resp.IsSuccessStatusCode)
            {
                lblTestResult.TextColor = Color.FromArgb("#6CCB5F");
                lblTestResult.Text = $"[OK] Connexion réussie ({(int)resp.StatusCode})";
            }
            else
            {
                lblTestResult.TextColor = Color.FromArgb("#FF6347");
                lblTestResult.Text = $"[KO] Erreur HTTP {(int)resp.StatusCode}";
            }
        }
        catch (TaskCanceledException)
        {
            lblTestResult.TextColor = Color.FromArgb("#FFB900");
            lblTestResult.Text = "[KO] Timeout (8s)";
        }
        catch (Exception ex)
        {
            lblTestResult.TextColor = Color.FromArgb("#FF6347");
            lblTestResult.Text = "[KO] " + ex.Message;
        }
        finally
        {
            btnTest.IsEnabled = true;
            btnTest.Text = "Tester la connexion";
        }
    }
    
    private void OnTogglePasswordClicked(object sender, EventArgs e)
    {
        txtPassword.IsPassword = !txtPassword.IsPassword;
        btnTogglePass.Source = txtPassword.IsPassword ? "eye.png" : "eye_off.png";
    }       
}