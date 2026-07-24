using System;
using System.Threading;
using MediaMonitor.Core.Language;
using MediaMonitor.Core.Services;

namespace MediaMonitor.Service
{
    internal class ReportScheduler
    {
        private readonly MediaMonitorEngine _engine;
        private Timer _timer;
        private DateTime _lastReportSent = DateTime.MinValue;
        private bool _isSending = false;

        public ReportScheduler(MediaMonitorEngine engine)
        {
            _engine = engine;
        }

        // ------------------------------------------------------------
        // DÉMARRAGE INITIAL
        // ------------------------------------------------------------
        public void Start()
        {
            ScheduleNext();
        }

        // ------------------------------------------------------------
        // RÉACTION : changement de Shutdown.config
        // ------------------------------------------------------------
        public void OnShutdownConfigChanged()
        {
            Write("Reprogrammation suite à modification de Shutdown.config");
            ScheduleNext();
        }

        // ------------------------------------------------------------
        // RÉACTION : sortie de veille
        // ------------------------------------------------------------
        public void OnWakeUp()
        {
            Write("Sortie de veille détectée ? reprogrammation du timer");
            ScheduleNext();
        }

        // ------------------------------------------------------------
        // CALCUL DE L’HEURE D’ENVOI
        // ------------------------------------------------------------
        private DateTime ComputeNextSendTime()
        {
            var shutdown = Program.LoadShutdownTime();

            if (shutdown == null)
            {
                Write("Aucune heure de shutdown ? envoi dans 1 minute");
                return DateTime.Now.AddMinutes(1);
            }

            var target = DateTime.Today
                .AddHours(shutdown.Value.hour)
                .AddMinutes(shutdown.Value.minute)
                .AddMinutes(-10);

            if (target < DateTime.Now)
                target = target.AddDays(1);

            var remaining = target - DateTime.Now;

            Write($"Prochain envoi prévu à {target:HH:mm} (dans {remaining.Hours}h {remaining.Minutes}min)");
            
            Program.WriteScheduleLog(
                "[CODE01] " +
                (LanguageManager.Get("Prochain envoi du rapport prévu à") ?? "Prochain envoi du rapport prévu à") +
                $" {target:HH:mm} " +
                "(" +
                (LanguageManager.Get("dans") ?? "dans") +
                $" {remaining.Hours}h {remaining.Minutes}min)"
            );

            return target;
        }

        // ------------------------------------------------------------
        // PROGRAMMATION DU TIMER
        // ------------------------------------------------------------
        private void ScheduleNext()
        {
            try
            {
                _timer?.Dispose();

                DateTime next = ComputeNextSendTime();
                TimeSpan delay = next - DateTime.Now;

                if (delay.TotalMilliseconds < 0)
                    delay = TimeSpan.FromMinutes(1);

                _timer = new Timer(async _ =>
                {
                    if (_isSending)
                        return;

                    _isSending = true;

                    try
                    {
                        // Anti-double envoi
                        if ((DateTime.Now - _lastReportSent) < TimeSpan.FromSeconds(30))
                        {
                            Write($"Double envoi évité (last={_lastReportSent:HH:mm:ss}, now={DateTime.Now:HH:mm:ss})");
                            return;
                        }

                        Write("Envoi du rapport…");

                        await _engine.SendReportEmail();

                        _lastReportSent = DateTime.Now;

                        Program._lastReportStatus = "[CODE02] " +
                            (LanguageManager.Get("Rapport envoyé à") ?? "Rapport envoyé à") +
                            $" {_lastReportSent:yyyy-MM-dd HH:mm:ss}";

                        Write(Program._lastReportStatus);

                        Write("DEBUG: Count=" + _engine.GetHistory().Count);

                        Program.SaveBackup(_engine);
                        _engine.ClearHistory();

                        // Empêcher un deuxième rapport le même jour
                        if (_lastReportSent.Date == DateTime.Now.Date)
                        {
                            Write("Rapport déjà envoyé aujourd'hui ? pas de reprogrammation.");
                            return;
                        }

                        ScheduleNext();
                    }
                    catch (Exception ex)
                    {
                        Write("ERREUR SendReportEmail : " + ex.Message);
                    }
                    finally
                    {
                        _isSending = false;
                    }

                }, null, delay, Timeout.InfiniteTimeSpan);
            }
            catch (Exception ex)
            {
                Write("ERREUR ScheduleNext : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        // LOG
        // ------------------------------------------------------------
        private void Write(string msg)
        {
            Program.WriteScheduleLog(msg);
        }
    }
}

