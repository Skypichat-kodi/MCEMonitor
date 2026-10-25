using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetMonitor.Service.Models;

namespace NetMonitor.Service
{
    /// <summary>
    /// Gère l'envoi groupé (digest) des emails d'alerte.
    ///
    /// Mode AUTO (AutoBlock=true) :
    ///   - Un mail toutes les 2 heures contenant toutes les alertes accumulées.
    ///   - Timer tick toutes les minutes pour être robuste à la veille et au redémarrage.
    ///   - Date du dernier flush persistée sur disque → survit à un restart.
    ///
    /// Mode MANUEL (AutoBlock=false) :
    ///   - Débounce de 10 min à la première alerte du cycle.
    ///   - À l'expiration : un mail avec toutes les alertes accumulées.
    ///   - Anti-doublon IP + Détecteur dans la fenêtre.
    ///
    /// Changement de mode à chaud (via NetMonitorEngine.ReloadConfig) :
    ///   - Flush immédiat avec mail dédié pour ne rien perdre.
    /// </summary>
    public sealed class EmailDigestScheduler : IDisposable
    {
        private static readonly TimeSpan AutoDigestInterval  = TimeSpan.FromHours(2);
        private static readonly TimeSpan ManualDebounceDelay = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan TickInterval        = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan FirstTickDelay      = TimeSpan.FromSeconds(30);

        private static readonly string StateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor", "email_digest_state.txt");

        private readonly NetMonitorSettings _settings;
        private readonly ConcurrentQueue<IntrusionEvent> _pending = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly object _manualLock = new();

        private Timer? _tickTimer;
        private DateTime? _manualCycleDeadline;   // null si pas de cycle manuel en cours
        private DateTime _lastAutoFlush = DateTime.MinValue;

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
            _lastAutoFlush = LoadLastAutoFlush();
            if (_lastAutoFlush == DateTime.MinValue)
            {
                // Premier démarrage : on démarre le compteur à maintenant
                _lastAutoFlush = DateTime.UtcNow;
                SaveLastAutoFlush(_lastAutoFlush);
            }

            CoreLog.Info(
                $"[EMAIL-DIGEST] Démarrage - mode = {(_settings.AutoBlock ? "AUTO (2h)" : "MANUEL (10min)")}, " +
                $"dernier flush AUTO = {_lastAutoFlush.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

            _tickTimer = new Timer(_ => Tick(), null, FirstTickDelay, TickInterval);
        }

        public void Stop()
        {
            _tickTimer?.Dispose();
            _tickTimer = null;

            lock (_manualLock)
            {
                _manualCycleDeadline = null;
            }

            int leftover = _pending.Count;
            if (leftover > 0)
                CoreLog.Warn($"[EMAIL-DIGEST] Arrêt avec {leftover} alerte(s) non envoyée(s) - drop");

            while (_pending.TryDequeue(out _)) { }
        }

        // ============================================================
        //  Enqueue (appelé par NetMonitorEngine)
        // ============================================================
        public void Enqueue(IntrusionEvent evt)
        {
            if (_disposed) return;

            // Fix Bug F : snapshot avant Any() pour éviter "Collection was modified"
            var snapshot = _pending.ToArray();
            if (snapshot.Any(p =>
                    p.RemoteIp == evt.RemoteIp &&
                    p.Source == evt.Source))
            {
                CoreLog.Debug($"[EMAIL-DIGEST] Doublon ignoré : {evt.Source} / {evt.RemoteIp}");
                return;
            }

            _pending.Enqueue(evt);
            CoreLog.Debug($"[EMAIL-DIGEST] Alerte en attente ({_pending.Count} en file) : {evt.Source} / {evt.RemoteIp}");

            if (!_settings.AutoBlock)
                EnsureManualDebounce();
        }

        // ============================================================
        //  Appelé par NetMonitorEngine quand la config a changé de mode
        // ============================================================
        public void OnModeChanged(bool oldAutoBlock, bool newAutoBlock)
        {
            if (oldAutoBlock == newAutoBlock) return;

            string reason = newAutoBlock
                ? "changement-manuel-vers-auto"
                : "changement-auto-vers-manuel";

            CoreLog.Info($"[EMAIL-DIGEST] Changement de mode détecté : {reason}");

            // Reset du cycle manuel quel que soit le sens du changement
            lock (_manualLock)
            {
                _manualCycleDeadline = null;
            }

            // Flush immédiat avec la file en cours.
            // wasAutoMode = newAutoBlock : si on vient de basculer en AUTO, on traite les
            // alertes accumulées comme un flush AUTO (mais _lastAutoFlush ne sera pas mis à
            // jour car la fenêtre écoulée n'est pas de 2 h — voir SendDigestEmailAsync).
            _ = Task.Run(() => FlushAsync(reason, updateLastAutoFlush: false));
        }

        // ============================================================
        //  Mode manuel : débounce de 10 min
        // ============================================================
        private void EnsureManualDebounce()
        {
            lock (_manualLock)
            {
                if (_manualCycleDeadline.HasValue) return;

                _manualCycleDeadline = DateTime.UtcNow + ManualDebounceDelay;
                CoreLog.Debug($"[EMAIL-DIGEST] Nouveau cycle manuel - échéance {_manualCycleDeadline.Value.ToLocalTime():HH:mm:ss}");
            }
        }

        // ============================================================
        //  Tick périodique (toutes les minutes)
        // ============================================================
        private void Tick()
        {
            if (_disposed) return;

            try
            {
                if (_settings.AutoBlock)
                {
                    // Mode AUTO : flush si intervalle écoulé et file non vide
                    if (DateTime.UtcNow - _lastAutoFlush >= AutoDigestInterval && !_pending.IsEmpty)
                    {
                        _ = FlushAsync("périodique-auto", updateLastAutoFlush: true);
                    }
                }
                else
                {
                    // Mode MANUEL : flush si le débounce a expiré
                    bool expired = false;
                    lock (_manualLock)
                    {
                        if (_manualCycleDeadline.HasValue &&
                            DateTime.UtcNow >= _manualCycleDeadline.Value)
                        {
                            _manualCycleDeadline = null;
                            expired = true;
                        }
                    }

                    if (expired)
                        _ = FlushAsync("délai-manuel-10min", updateLastAutoFlush: false);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Error($"[EMAIL-DIGEST] Erreur Tick : {ex.Message}");
            }
        }

        // ============================================================
        //  Flush effectif
        // ============================================================
        private async Task FlushAsync(string reason, bool updateLastAutoFlush)
        {
            if (!await _sendLock.WaitAsync(0))
            {
                CoreLog.Debug($"[EMAIL-DIGEST] Flush({reason}) ignoré - un envoi est déjà en cours");
                return;
            }

            var batch = new List<IntrusionEvent>();

            try
            {
                // Vidage atomique
                while (_pending.TryDequeue(out var evt))
                    batch.Add(evt);

                if (batch.Count == 0)
                {
                    CoreLog.Info($"[EMAIL-DIGEST] Flush({reason}) : aucun mail envoyé - file vide");
                    return;
                }

                var cfg = EmailConfig.Load();
                if (string.IsNullOrEmpty(cfg.Server))
                {
                    CoreLog.Warn($"[EMAIL-DIGEST] Pas de serveur SMTP configuré - {batch.Count} alerte(s) droppée(s)");
                    batch.Clear();   // drop intentionnel
                    return;
                }

                CoreLog.Info($"[EMAIL-DIGEST] Envoi de {batch.Count} alerte(s) (raison: {reason})");
                await SendDigestEmailAsync(cfg, batch, reason);

                if (updateLastAutoFlush)
                {
                    _lastAutoFlush = DateTime.UtcNow;
                    SaveLastAutoFlush(_lastAutoFlush);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Error($"[EMAIL-DIGEST] Erreur flush : {ex.Message}");

                // Échec SMTP → remettre les alertes en file pour retry au prochain tick
                if (batch.Count > 0)
                {
                    CoreLog.Warn($"[EMAIL-DIGEST] {batch.Count} alerte(s) remise(s) en file après échec");
                    foreach (var e in batch)
                        _pending.Enqueue(e);
                }
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
            var ordered = batch
                .OrderByDescending(e => e.Severity)
                .ThenByDescending(e => e.Timestamp)
                .ToList();

            int critical = ordered.Count(e => e.Severity == Severity.Critical);
            int high     = ordered.Count(e => e.Severity == Severity.High);
            int warning  = ordered.Count(e => e.Severity == Severity.Warning);
            int info     = ordered.Count(e => e.Severity == Severity.Info);

            bool isAutoMode = reason.Contains("auto") || reason.Contains("vers-auto");

            var byIp = ordered
                .Where(e => !string.IsNullOrWhiteSpace(e.RemoteIp))
                .GroupBy(e => e.RemoteIp!)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToList();

            var sb = new System.Text.StringBuilder();

            // Bandeau de contexte selon le mode
            string modeLabel = isAutoMode ? "Bilan automatique" : "Rapport manuel";
            string intro = isAutoMode
                ? "Toutes les alertes ci-dessous ont été <b>bloquées automatiquement</b> par le pare-feu."
                : "Les alertes ci-dessous ont été détectées pendant la fenêtre manuelle de 10 minutes.";

            sb.Append($"<p style='margin:0 0 10px 0;color:#555'><b>{modeLabel}</b> — {intro}</p>");

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

            string subject = isAutoMode
                ? $"[NetMonitor] Bilan auto (2h) : {batch.Count} alerte(s) - {critical} critique(s), {high} élevée(s) - {Environment.MachineName}"
                : $"[NetMonitor] {batch.Count} alerte(s) - {critical} critique(s), {high} élevée(s) - {Environment.MachineName}";

            await EmailSender.SendAsync(cfg, subject, sb.ToString(), isHtml: true);
            CoreLog.Write($"[EMAIL-DIGEST] Digest envoyé ({batch.Count} alertes, raison: {reason})");
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }

        // ============================================================
        //  Persistance du dernier flush AUTO
        // ============================================================
        private static DateTime LoadLastAutoFlush()
        {
            try
            {
                if (!File.Exists(StateFile)) return DateTime.MinValue;
                string raw = File.ReadAllText(StateFile).Trim();
                if (DateTime.TryParse(raw, null,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out DateTime dt))
                    return dt.ToUniversalTime();
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"[EMAIL-DIGEST] LoadLastAutoFlush : {ex.Message}");
            }
            return DateTime.MinValue;
        }

        private static void SaveLastAutoFlush(DateTime dt)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
                File.WriteAllText(StateFile, dt.ToUniversalTime().ToString("O"));
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"[EMAIL-DIGEST] SaveLastAutoFlush : {ex.Message}");
            }
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