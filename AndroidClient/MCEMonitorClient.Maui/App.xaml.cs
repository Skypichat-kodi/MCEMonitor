namespace MCEMonitorClient.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        MainPage = new AppShell();
    }

    protected override void OnStart()
    {
    #if ANDROID
        try
        {
            var ctx = global::Android.App.Application.Context;
            Platforms.Android.Services.ServiceHelper.Start(ctx);
            Preferences.Set("service_running", true);
        }
        catch { }
    #endif
    }
}