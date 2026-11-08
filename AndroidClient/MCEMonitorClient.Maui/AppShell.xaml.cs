namespace MCEMonitorClient.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Routes pour les pages de détail par type de service.
        // Ajouter ici chaque nouvelle page de détail (RomMonitor, NetMonitor, MediaMonitor).
        Routing.RegisterRoute("systemmonitor/detail", typeof(Pages.SystemMonitorDetailPage));
    }
}