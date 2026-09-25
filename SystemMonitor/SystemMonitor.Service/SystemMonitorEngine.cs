using System;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Orchestrateur principal : boucle de rafraîchissement toutes les X secondes.
    /// </summary>
    public class SystemMonitorEngine
    {
        private readonly SystemMonitorSettings _settings;
        private readonly HardwareMonitorService _hardware;

        private Timer _timer;
        private bool _isRunning;
        private SystemSnapshot _lastSnapshot = new();

        public SystemSnapshot LastSnapshot => _lastSnapshot;
        public DateTime LastUpdateTime { get; private set; } = DateTime.MinValue;
        public bool IsRunning => _isRunning;

        public event Action OnUpdate;

        public SystemMonitorEngine(SystemMonitorSettings settings)
        {
            _settings = settings;
            _hardware = new HardwareMonitorService();
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            CoreLog.Write($"SystemMonitorEngine démarré (intervalle = {_settings.Interval} s)");

            _timer = new Timer(_settings.Interval * 1000);
            _timer.Elapsed += (s, e) => Tick();
            _timer.AutoReset = true;
            _timer.Start();

            // Premier check immédiat
            Task.Run(() => Tick());
        }

        public void Stop()
        {
            _isRunning = false;
            _timer?.Stop();
            CoreLog.Write("SystemMonitorEngine arrêté");
        }

        /// <summary>
        /// Force un Tick immédiat (appelé par IPC).
        /// </summary>
        public void ForceTick()
        {
            CoreLog.Write("ForceTick demandé via IPC");
            Task.Run(() => Tick());
        }

        private void Tick()
        {
            if (!_isRunning) return;

            try
            {
                var snap = _hardware.GetSnapshot();
                _lastSnapshot = snap;
                LastUpdateTime = DateTime.Now;

                OnUpdate?.Invoke();
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Tick : " + ex.Message);
            }
        }

        public void Dispose()
        {
            Stop();
            _hardware?.Dispose();
        }
    }
}