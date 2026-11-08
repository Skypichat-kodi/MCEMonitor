using CommunityToolkit.Maui.Views;

namespace MCEMonitorClient.Maui.Pages;

public partial class ServerActionsPopup : Popup
{
    public string SelectedAction { get; private set; } = "";

    public ServerActionsPopup(string serverName, string serviceType, bool serverOnline)
    {
        InitializeComponent();

        lblServerName.Text = serverName;
        lblServiceType.Text = string.IsNullOrWhiteSpace(serviceType) ? "" : serviceType;

        // Si le serveur est offline, désactiver "Consulter" visuellement
        if (!serverOnline)
        {
            btnConsume.IsEnabled = false;
            btnConsume.BackgroundColor = Color.FromArgb("#2D3E4A");
            btnConsume.TextColor = Color.FromArgb("#6A8A9A");
            btnConsume.Text = "Consulter (hors ligne)";
        }
    }

    private void OnConsumeClicked(object sender, EventArgs e)
    {
        SelectedAction = "Consulter";
        Close();
    }

    private void OnEditClicked(object sender, EventArgs e)
    {
        SelectedAction = "Modifier";
        Close();
    }

    private void OnDeleteClicked(object sender, EventArgs e)
    {
        SelectedAction = "Supprimer";
        Close();
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        SelectedAction = "";
        Close();
    }
}