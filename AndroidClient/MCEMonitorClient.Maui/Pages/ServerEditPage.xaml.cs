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
    private int _autoDetectVersion = 0;
    private string _detectedServiceType = "";

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

            SetDetectedService(existing.ServiceType);

            // Indicateur : mot de passe absent alors qu'un login existe
            if (string.IsNullOrEmpty(existing.Password) && !string.IsNullOrEmpty(existing.Username))
            {
                lblTestResult.TextColor = Color.FromArgb("#FFB900");
                lblTestResult.Text = "Mot de passe à re-saisir (stockage sécurisé vide).";
            }
        }

        txtUrl.Unfocused  += async (s, e) => await AutoDetectAsync();
        txtPort.Unfocused += async (s, e) => await AutoDetectAsync();
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

        var config = await ConfigService.LoadAsync();

        if (_existing == null)
        {
            var entry = new ServerEntry
            {
                Name        = name,
                BaseUrl     = url.TrimEnd('/'),
                Port        = port,
                Username    = txtUsername.Text?.Trim() ?? "",
                Password    = txtPassword.Text ?? "",
                ServiceType = GetDetectedService(),
                Enabled     = swEnabled.IsToggled
            };
            config.Servers.Add(entry);
        }
        else
        {
            var existing = config.Servers.FirstOrDefault(s => s.Id == _existing!.Id);
            if (existing != null)
            {
                existing.Name        = name;
                existing.BaseUrl     = url.TrimEnd('/');
                existing.Port        = port;
                existing.Username    = txtUsername.Text?.Trim() ?? "";
                existing.Password    = txtPassword.Text ?? "";
                existing.ServiceType = GetDetectedService();
                existing.Enabled     = swEnabled.IsToggled;
            }
            else
            {
                await DisplayAlert("Erreur", "Serveur introuvable.", "OK");
                return;
            }
        }

        await ConfigService.SaveAsync(config);
        await Navigation.PopModalAsync();
    }

    private async void OnTestClicked(object sender, EventArgs e)
    {
        await TestConnectionAsync(autoMode: false);
    }

    private async Task AutoDetectAsync()
    {
        if (!btnTest.IsEnabled)
            return;

        string url = txtUrl.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url) || url == "http://" || url == "https://")
            return;

        int myVersion = ++_autoDetectVersion;
        await Task.Delay(400);

        if (!IsVisible) return;

        if (myVersion != _autoDetectVersion)
            return;

        url = txtUrl.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url) || url == "http://" || url == "https://")
            return;

        if (!btnTest.IsEnabled)
            return;

        await TestConnectionAsync(autoMode: true);
    }

    private async Task TestConnectionAsync(bool autoMode)
    {
        string url     = txtUrl.Text?.Trim() ?? "";
        string portStr = txtPort.Text?.Trim() ?? "8083";
        string user    = txtUsername.Text?.Trim() ?? "";
        string pass    = txtPassword.Text ?? "";

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

        string baseUrl = url.TrimEnd('/');
        if (!baseUrl.Contains("://"))
            baseUrl = "http://" + baseUrl;

        string testUrl = $"{baseUrl}:{port}/api/summary";

        btnTest.IsEnabled = false;
        if (!autoMode) btnTest.Text = "Test en cours...";

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

            if (!resp.IsSuccessStatusCode)
            {
                lblTestResult.TextColor = Color.FromArgb("#FF6347");
                lblTestResult.Text = $"[KO] Erreur HTTP {(int)resp.StatusCode}";
                return;
            }

            string json = await resp.Content.ReadAsStringAsync();
            string detectedService = "Inconnu";
            string detectedMachine = "";

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("service", out var serviceProp))
                    detectedService = serviceProp.GetString() ?? "Inconnu";
                if (doc.RootElement.TryGetProperty("machine", out var machineProp))
                    detectedMachine = machineProp.GetString() ?? "";
            }
            catch { }

            string hostFallback = baseUrl;
            try { hostFallback = new Uri(baseUrl).Host; } catch { }

            string machine = !string.IsNullOrEmpty(detectedMachine) ? detectedMachine : hostFallback;

            SetDetectedService(detectedService);

            string proposedName = $"Service {detectedService} sur {machine}";
            string currentName  = txtName.Text?.Trim() ?? "";

            bool shouldUpdateName =
                string.IsNullOrWhiteSpace(currentName)
                || currentName == "http://"
                || currentName.StartsWith("Service ",        StringComparison.OrdinalIgnoreCase)
                || currentName.StartsWith("SystemMonitor (", StringComparison.OrdinalIgnoreCase)
                || currentName.StartsWith("RomMonitor (",    StringComparison.OrdinalIgnoreCase)
                || currentName.StartsWith("MediaMonitor (",  StringComparison.OrdinalIgnoreCase)
                || currentName.StartsWith("NetMonitor (",    StringComparison.OrdinalIgnoreCase)
                || currentName.StartsWith("Inconnu (",       StringComparison.OrdinalIgnoreCase);

            if (shouldUpdateName)
                txtName.Text = proposedName;

            lblTestResult.TextColor = Color.FromArgb("#6CCB5F");
            lblTestResult.Text = $"[OK] Service {detectedService} sur {machine}";
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

    private void SetDetectedService(string? serviceType)
    {
        _detectedServiceType = serviceType ?? "";

        lblDetectedService.Text = string.IsNullOrWhiteSpace(serviceType)
            ? "— Non détecté —"
            : serviceType!;

        lblDetectedService.TextColor = string.IsNullOrWhiteSpace(serviceType)
            ? Color.FromArgb("#888888")
            : Color.FromArgb("#6CCB5F");
    }

    private string GetDetectedService()
    {
        return string.IsNullOrWhiteSpace(_detectedServiceType)
            ? "Inconnu"
            : _detectedServiceType;
    }
}