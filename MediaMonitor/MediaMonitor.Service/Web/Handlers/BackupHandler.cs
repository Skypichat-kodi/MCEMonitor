using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Models;
using MediaMonitor.Service.Web.Templates;

namespace MediaMonitor.Service.Web.Handlers
{
    public static class BackupHandler
    {
        public static void Handle(HttpListenerContext ctx, MediaMonitorEngine engine)
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";
            var files = Directory.GetFiles(folder, "history_*.json");

            if (files.Length == 0)
            {
                string html = TemplateEngine.Translate("<html><body><h2>{{tr:Aucune sauvegarde}}</h2></body></html>");
                WebServer.SendHtml(ctx, html);
                return;
            }

            string lastFile = files.OrderByDescending(f => f).First();
            string json = File.ReadAllText(lastFile);

            BackupFileModel? backup = JsonSerializer.Deserialize<BackupFileModel>(json);

            if (backup == null)
            {
                WebServer.SendHtml(ctx, "<html><body><h2>{{tr:Sauvegarde invalide}}</h2></body></html>");
                return;
            }

            // Page simple (tu me diras si tu veux la vraie page backup)
            WebServer.SendHtml(ctx, "<html><body><h2>Backup chargé</h2></body></html>");
        }
    }
}
