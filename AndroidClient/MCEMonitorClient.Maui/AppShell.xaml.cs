namespace MCEMonitorClient.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Routes pour les pages de détail par type de service.
        Routing.RegisterRoute("systemmonitor/detail",       typeof(Pages.SystemMonitorDetailPage));
        Routing.RegisterRoute("rommonitor/detail",          typeof(Pages.RomMonitorDetailPage));
        Routing.RegisterRoute("netmonitor/detail",          typeof(Pages.NetMonitorDetailPage));
        // Routing.RegisterRoute("mediamonitor/detail",     typeof(Pages.MediaMonitor.MediaMonitorDetailPage));
    }
}