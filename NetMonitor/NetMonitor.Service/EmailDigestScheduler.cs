using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetMonitor.Service.Models;

namespace NetMonitor.Service
{
    /// <summary>
    /// Gère l'envoi groupé (digest) des emails d'alerte.
    /// 
    /// Mode AUTO-BLOCK activé (blocage automatique) :
    ///   ? Les alertes sont accumulées et envoyées toutes les 6 heures dans un seul mail.
    ///   ? Si une alerte Critique arrive, un envoi immédiat est déclenché (optionnel, voir CriticalBypass).
    ///
    /// Mode MANUEL (blocage manuel) :
    ///   ? Chaque nouvelle alerte démarre un timer de 10 minutes.
    ///   ? Toutes les alertes détectées pendant ces 10 minutes sont regroupées dans un seul mail.
    ///   ? Si une alerte arrive APRÈS l'envoi, un nouveau cycle de 10 min démarre.
    ///   ? Les alertes identiques (même IP + même détecteur) dans la fenêtre ne sont pas dupliquées.
    /// </summary>
    public sealed class EmailDigestScheduler : IDisposable
    {
        private readonly NetMonitorSettings _settings;

        // Fenêtre d'agrégation (constantes ajustables)
        private static readonly TimeSpan AutoBlockDigestInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan ManualDigestDelay      = TimeSpan.FromMinutes(10);

        // File des alertes en attente d'envoi
        private readonly ConcurrentQueue<IntrusionEvent> _pending = new();

        // Timer périodique (mode auto-block)
        private Timer? _periodicTimer;

        // Timer one-shot (mode manuel) : déclenché à la première alerte
        private Timer? _manualDebounceTimer;
        private readonly object _manualLock = new();
        private bool _manualCycleActive;

        // Verrou global d'envoi (évite deux envois simultanés)
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private bool _disposed;

        public EmailDigestScheduler(NetMonitorSettings settings)
        {
            _settings = settings;
        }

        // ============================================================
        //  Démarrage / arrêt
        // ============================================================
        public void Start()
        {
            CoreLog.Info($"[EMAIL-DIGEST] Démarrage - mode = {(_settings.AutoBlock ? "AUTO (6h)" : "MANUEL (10min)")}");

            if (_settings.AutoBlock)
            {
                // Mode auto : envoi périodique toutes les 6h
                _periodicTimer = new Timer(
                    _ => _ = FlushAsync("périodique-auto"),
                    null,
                    AutoBlockDigestInterval,
                    AutoBlockDigestInterval);
            }
            // Mode manuel : pas de timer périodique, c'est la première alerte qui déclenche le debounce
        }

        public void Stop()
        {
            _periodicTimer?.Dispose();
            _periodicTimer = null;

            lock (_manualLock)
            {
                _manualDebounceTimer?.Dispose();
                _manualDebounceTimer = null;
                _manualCycleActive = false;
            }

            // Vide la file sans envoyer (le service s'arrête)
            while (_pending.TryDequeue(out _)) { }
        }

        // ============================================================
        //  Point d'entrée : appelé par NetMonitorEngine
        // ============================================================
        public void Enqueue(IntrusionEvent evt)
        {
            if (_disposed) return;

            // Anti-doublon : si la même IP + même détecteur est déjà en attente, on skip
            if (_pending.Any(p =>
                    p.RemoteIp == evt.RemoteIp &&
                    p.Source == evt.Source))
            {
                CoreLog.Debug($"[EMAIL-DIGEST] Doublon ignoré : {evt.Source} / {evt.RemoteIp}");
                return;
            }

            _pending.Enqueue(evt);
            CoreLog.Debug($"[EMAIL-DIGEST] Alerte en attente ({_pending.Count} en file) : {evt.Source} / {evt.RemoteIp}");

            if (!_settings.AutoBlock)
                EnsureManualDebounceTimer();
        }

        // ============================================================
        //  Mode manuel : démarre un timer de 10 min à la 1ère alerte
        // ============================================================
        private void EnsureManualDebounceTimer()
        {
            lock (_manualLock)
            {
                if (_manualCycleActive) return;

                _manualCycleActive = true;

                _manualDebounceTimer = new Timer(
                    _ => _ = FlushAsync("délai-manuel-10min"),
                    null,
                    ManualDigestDelay,
                    Timeout.InfiniteTimeSpan);
            }
        }

        // ============================================================
        //  Envoi effectif du digest
        // ============================================================
        private async Task FlushAsync(string reason)
        {
            // Empêche deux flushs concurrents
            if (!await _sendLock.WaitAsync(0))
                return;

            try
            {
                // Réinitialise l'état manuel AVANT de vider (nouvelle alerte = nouveau cycle)
                if (!_settings.AutoBlock)
                {
                    lock (_manualLock)
                    {
                        _manualDebounceTimer?.Dispose();
                        _manualDebounceTimer = null;
                        _manualCycleActive = false;
                    }
                }

                // Vide la file
                var batch = new List<IntrusionEvent>();
                while (_pending.TryDequeue(out var evt))
                    batch.Add(evt);

                if (batch.Count == 0)
                    return;

                // Charge la config email : si pas de serveur, on drop (mais on loggue)
                var cfg = EmailConfig.Load();
                if (string.IsNullOrEmpty(cfg.Server))
                {
                    CoreLog.Debug($"[EMAIL-DIGEST] Pas de serveur SMTP configuré - {batch.Count} alerte(s) droppée(s)");
                    return;
                }

                CoreLog.Info($"[EMAIL-DIGEST] Envoi de {batch.Count} alerte(s) (raison: {reason})");
                await SendDigestEmailAsync(cfg, batch, reason);
            }
            catch (Exception ex)
            {
                CoreLog.Error($"[EMAIL-DIGEST] Erreur flush : {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // ============================================================
        //  Construction et envoi du mail digest
        // ============================================================
        private static async Task SendDigestEmailAsync(
            EmailConfig cfg,
            List<IntrusionEvent> batch,
            string reason)
        {
            // Tri par sévérité puis date
            var ordered = batch
                .OrderByDescending(e => e.Severity)
                .ThenByDescending(e => e.Timestamp)
                .ToList();

            int critical = ordered.Count(e => e.Severity == Severity.Critical);
            int high     = ordered.Count(e => e.Severity == Severity.High);
            int warning  = ordered.Count(e => e.Severity == Severity.Warning);
            int info     = ordered.Count(e => e.Severity == Severity.Info);

            // Compteur par IP (déduplication visuelle)
            var byIp = ordered
                .Where(e => !string.IsNullOrWhiteSpace(e.RemoteIp))
                .GroupBy(e => e.RemoteIp!)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToList();

            var sb = new System.Text.StringBuilder();

            // Résumé
            sb.Append("<h3 style='margin-top:0;color:#2c3e50'>Récapitulatif</h3>");
            sb.Append("<table style='border-collapse:collapse;margin-bottom:15px'>");
            sb.Append($"<tr><td style='padding:3px 10px'><b>Total alertes</b></td><td>{batch.Count}</td></tr>");
            if (critical > 0) sb.Append($"<tr><td style='padding:3px 10px;color:#c0392b'><b>Critiques</b></td><td style='color:#c0392b'>{critical}</td></tr>");
            if (high > 0)     sb.Append($"<tr><td style='padding:3px 10px;color:#e67e22'><b>Élevées</b></td><td style='color:#e67e22'>{high}</td></tr>");
            if (warning > 0)  sb.Append($"<tr><td style='padding:3px 10px;color:#f39c12'><b>Avertissements</b></td><td style='color:#f39c12'>{warning}</td></tr>");
            if (info > 0)     sb.Append($"<tr><td style='padding:3px 10px;color:#7f8c8d'><b>Info</b></td><td style='color:#7f8c8d'>{info}</td></tr>");
            sb.Append("</table>");

            // Top IPs
            if (byIp.Count > 0)
            {
                sb.Append("<h4 style='color:#2c3e50'>Top IPs détectées</h4>");
                sb.Append("<table style='border-collapse:collapse;font-size:13px'>");
                sb.Append("<tr style='background:#ecf0f1'><th style='padding:4px 8px;text-align:left'>IP</th><th style='padding:4px 8px;text-align:left'>Occurrences</th><th style='padding:4px 8px;text-align:left'>Détecteurs</th></tr>");
                foreach (var g in byIp)
                {
                    var sources = string.Join(", ", g.Select(e => e.Source).Distinct());
                    sb.Append($"<tr><td style='padding:4px 8px;border-bottom:1px solid #eee'>{g.Key}</td>");
                    sb.Append($"<td style='padding:4px 8px;border-bottom:1px solid #eee'>{g.Count()}</td>");
                    sb.Append($"<td style='padding:4px 8px;border-bottom:1px solid #eee'>{sources}</td></tr>");
                }
                sb.Append("</table>");
            }

            // Détail complet
            sb.Append("<h4 style='color:#2c3e50;margin-top:20px'>Détail des alertes</h4>");
            sb.Append("<table style='border-collapse:collapse;font-size:12px;width:100%'>");
            sb.Append("<tr style='background:#ecf0f1'>");
            sb.Append("<th style='padding:4px 6px;text-align:left'>Date</th>");
            sb.Append("<th style='padding:4px 6px;text-align:left'>Sévérité</th>");
            sb.Append("<th style='padding:4px 6px;text-align:left'>Détecteur</th>");
            sb.Append("<th style='padding:4px 6px;text-align:left'>IP distante</th>");
            sb.Append("<th style='padding:4px 6px;text-align:left'>Message</th>");
            sb.Append("</tr>");

            foreach (var e in ordered.Take(50))
            {
                string color = e.Severity switch
                {
                    Severity.Critical => "#c0392b",
                    Severity.High     => "#e67e22",
                    Severity.Warning  => "#f39c12",
                    _                 => "#7f8c8d"
                };
                sb.Append("<tr>");
                sb.Append($"<td style='padding:3px 6px;border-bottom:1px solid #eee'>{e.Timestamp.ToLocalTime():dd/MM HH:mm}</td>");
                sb.Append($"<td style='padding:3px 6px;border-bottom:1px solid #eee;color:{color}'><b>{e.Severity}</b></td>");
                sb.Append($"<td style='padding:3px 6px;border-bottom:1px solid #eee'>{System.Net.WebUtility.HtmlEncode(e.Source)}</td>");
                sb.Append($"<td style='padding:3px 6px;border-bottom:1px solid #eee'>{e.RemoteIp}:{e.RemotePort}</td>");
                sb.Append($"<td style='padding:3px 6px;border-bottom:1px solid #eee'>{System.Net.WebUtility.HtmlEncode(Truncate(e.Message, 80))}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</table>");

            if (ordered.Count > 50)
                sb.Append($"<p style='color:#888;font-style:italic'>... et {ordered.Count - 50} autre(s) alerte(s) (voir l'interface pour le détail complet).</p>");

            string subject = $"[NetMonitor] {batch.Count} alerte(s) - {critical} critique(s), {high} élevée(s) - {Environment.MachineName}";

            await EmailSender.SendAsync(cfg, subject, sb.ToString(), isHtml: true);
            CoreLog.Write($"[EMAIL-DIGEST] Digest envoyé ({batch.Count} alertes, raison: {reason})");
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _sendLock.Dispose();
        }
    }
}