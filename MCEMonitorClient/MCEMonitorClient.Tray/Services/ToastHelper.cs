using System;
using System.Diagnostics;
using Microsoft.Toolkit.Uwp.Notifications;

namespace MCEMonitorClient.Tray.Services
{
    /// <summary>
    /// Gère les notifications Windows Toast modernes.
    /// </summary>
    public static class ToastHelper
    {
        public const string AppUserModelId = "MCEMonitor.Client";

        /// <summary>
        /// À appeler UNE FOIS au démarrage du Tray.
        /// Enregistre l'app pour recevoir des notifications persistantes.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                ToastNotificationManagerCompat.OnActivated += args =>
                {
                    try
                    {
                        var parsedArgs = ToastArguments.Parse(args.Argument);

                        if (parsedArgs.Contains("url"))
                        {
                            string url = parsedArgs["url"];
                            if (!string.IsNullOrEmpty(url))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = url,
                                    UseShellExecute = true
                                });
                            }
                        }
                    }
                    catch { }
                };
            }
            catch { }
        }

        /// <summary>
        /// Affiche une notification Windows Toast.
        /// </summary>
        public static void Show(
            string title,
            string message,
            string url = "",
            string icon = "")
        {
            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message);

                if (!string.IsNullOrEmpty(url))
                    builder.AddArgument("url", url);

                if (!string.IsNullOrEmpty(icon))
                    builder.AddAppLogoOverride(new Uri(icon));

                builder.Show();
            }
            catch { }
        }
    }
}