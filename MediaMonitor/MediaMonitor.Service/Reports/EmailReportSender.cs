using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaMonitor.Core.Models;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Reports;

namespace MediaMonitor.Service.Reports
{
    /// <summary>
    /// Orchestre l'envoi du rapport par email : génération HTML + pagination + appel SMTP.
    /// </summary>
    public static class EmailReportSender
    {
        public static async Task SendAsync(IReadOnlyList<MediaUsageItem> history)
        {
            CoreLog.Write("=== Début envoi rapport automatique ===");

            try
            {
                // 1) Génération du HTML complet
                string html = ReportHtmlBuilder.Build(history);

                CoreLog.Write($"Taille HTML totale : {html.Length} caractères");

                // 2) Découpage en lignes puis en blocs
                var lignes = html.Split('\n').ToList();
                CoreLog.Write($"Nombre de lignes HTML détectées : {lignes.Count}");

                int blocTaille = 200;
                int totalBlocs = (int)Math.Ceiling(lignes.Count / (double)blocTaille);

                CoreLog.Write($"Nombre total de blocs prévus : {totalBlocs}");

                var cfg = EmailConfig.Load();

                for (int i = 0; i < totalBlocs; i++)
                {
                    CoreLog.Write($"--- Préparation du bloc {i + 1}/{totalBlocs} ---");

                    var bloc = lignes
                        .Skip(i * blocTaille)
                        .Take(blocTaille)
                        .ToList();

                    CoreLog.Write($"Bloc {i + 1} : {bloc.Count} lignes");

                    bloc.Add($"<br><div style='font-size:12px;color:#888;'>Partie {i + 1} / {totalBlocs}</div>");

                    string htmlBloc = string.Join("\n", bloc);

                    CoreLog.Write($"Taille HTML du bloc {i + 1} : {htmlBloc.Length} caractères");

                    string sujet = totalBlocs == 1
                        ? "Rapport MediaMonitor"
                        : $"Rapport MediaMonitor (partie {i + 1}/{totalBlocs})";

                    CoreLog.Write($"Sujet du mail : {sujet}");
                    CoreLog.Write($"Envoi du bloc {i + 1}/{totalBlocs}...");

                    await EmailSender.SendAsync(cfg, sujet, htmlBloc, isHtml: true);

                    CoreLog.Write($"Bloc {i + 1}/{totalBlocs} envoyé avec succès.");
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email automatique : " + ex.ToString());
            }

            CoreLog.Write("=== Fin envoi rapport automatique ===");
        }
    }
}