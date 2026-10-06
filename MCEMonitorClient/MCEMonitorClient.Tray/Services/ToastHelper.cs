using System;
using System.Diagnostics;
using System.IO;
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
        /// <param name="title">Titre (mis en valeur par Windows)</param>
        /// <param name="message">Corps du message</param>
        /// <param name="iconFileName">Nom du fichier d'icône dans Resources\Icons</param>
        /// <param name="url">URL à ouvrir au clic (optionnel)</param>
        /// <param name="silent">Si true, désactive le son système Windows (pour jouer notre propre son)</param>
        public static void Show(
            string title,
            string message,
            string iconFileName = "",
            string url = "",
            bool silent = false)
        {
            try
            {
                var builder = new ToastContentBuilder();

                // Titre en gras + grande taille
                builder.AddText(title, AdaptiveTextStyle.Title);
                // Corps normal
                builder.AddText(message, AdaptiveTextStyle.Body);

                // Icône à gauche du toast
                if (!string.IsNullOrEmpty(iconFileName))
                {
                    string iconPath = GetIconPath(iconFileName);
                    if (File.Exists(iconPath))
                    {
                        string uri = new Uri(iconPath).AbsoluteUri;
                        builder.AddAppLogoOverride(
                            new Uri(uri),
                            ToastGenericAppLogoCrop.Circle);
                    }
                }

                // URL d'action au clic
                if (!string.IsNullOrEmpty(url))
                    builder.AddArgument("url", url);

                // Désactive le son système Windows
                if (silent)
                    builder.AddAudio(new ToastAudio { Silent = true });

                builder.Show();
            }
            catch { }
        }

        /// <summary>
        /// Construit le chemin complet vers une icône de Resources\Icons.
        /// </summary>
        private static string GetIconPath(string fileName)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(exeDir, "Resources", "Icons", fileName);
        }
    }
}