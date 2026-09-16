using System;
using System.IO;
using System.Threading;
using MediaMonitor.Core.Services;

namespace MediaMonitor.Service.Service
{
    /// <summary>
    /// Surveille Shutdown.config et MediaMonitor.Web.config.
    /// </summary>
    public class ConfigWatcher : IDisposable
    {
        private readonly BackupService _backupService;
        private readonly Action _onShutdownChanged;

        private FileSystemWatcher _shutdownWatcher;
        private FileSystemWatcher _webConfigWatcher;

        private DateTime _lastConfigChange = DateTime.MinValue;
        private DateTime _lastWebConfigChange = DateTime.MinValue;

        private (int hour, int minute)? _lastShutdownTime;
        private bool _dvbViewerEnabled;

        public ConfigWatcher(BackupService backupService, Action onShutdownChanged)
        {
            _backupService = backupService;
            _onShutdownChanged = onShutdownChanged;
        }

        public void SetDvbViewerState(bool enabled)
        {
            _dvbViewerEnabled = enabled;
        }

        public void Start()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor"
            );

            // --- Shutdown.config ---
            try
            {
                _shutdownWatcher = new FileSystemWatcher(folder, "Shutdown.config")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
                };
                _shutdownWatcher.Changed += OnShutdownChanged;
                _shutdownWatcher.Created += OnShutdownChanged;
                _shutdownWatcher.Renamed += OnShutdownChanged;
                _shutdownWatcher.EnableRaisingEvents = true;

                CoreLog.Write("FileSystemWatcher actif sur Shutdown.config");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR FileSystemWatcher Shutdown : " + ex);
            }

            // --- MediaMonitor.Web.config ---
            try
            {
                _webConfigWatcher = new FileSystemWatcher(folder, "MediaMonitor.Web.config")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
                };
                _webConfigWatcher.Changed += OnWebConfigChanged;
                _webConfigWatcher.Created += OnWebConfigChanged;
                _webConfigWatcher.Renamed += OnWebConfigChanged;
                _webConfigWatcher.EnableRaisingEvents = true;

                CoreLog.Write("FileSystemWatcher actif sur MediaMonitor.Web.config");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR FileSystemWatcher WebConfig : " + ex);
            }
        }

        // ---------------------------------------------------------------
        //  Shutdown.config modifié
        // ---------------------------------------------------------------
        private void OnShutdownChanged(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.Now - _lastConfigChange).TotalMilliseconds < 200)
                return;

            _lastConfigChange = DateTime.Now;

            try
            {
                var newTime = ConfigLoader.LoadShutdownTime();
                if (newTime == null)
                    return;

                bool changed =
                    _lastShutdownTime == null ||
                    _lastShutdownTime.Value.hour != newTime.Value.hour ||
                    _lastShutdownTime.Value.minute != newTime.Value.minute;

                _lastShutdownTime = newTime.Value;

                ScheduleLogger.Clear();

                if (changed)
                {
                    ScheduleLogger.Write(
                        $"Nouvelle heure détectée : {newTime.Value.hour:D2}:{newTime.Value.minute:D2}"
                    );
                }

                ScheduleLogger.Write(
                    $"Shutdown.config chargé : {newTime.Value.hour:D2}:{newTime.Value.minute:D2}"
                );

                ScheduleLogger.Write(Program.LastReportStatus);

                _onShutdownChanged?.Invoke();
            }
            catch (Exception ex)
            {
                ScheduleLogger.Write("ERREUR ShutdownConfigChanged : " + ex.Message);
            }
        }

        // ---------------------------------------------------------------
        //  MediaMonitor.Web.config modifié
        // ---------------------------------------------------------------
        private void OnWebConfigChanged(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.Now - _lastWebConfigChange).TotalMilliseconds < 1000)
                return;

            _lastWebConfigChange = DateTime.Now;

            // --- Rétention ---
            try
            {
                Thread.Sleep(200);

                int days = WebServerSettings.Load().RetentionDays;
                _backupService.Restart();

                ScheduleLogger.Write($"Rétention mise à jour : {days} jours");
                ScheduleLogger.Write("Timer de sauvegarde reprogrammé suite au changement de rétention.");
            }
            catch (Exception ex)
            {
                ScheduleLogger.Write("Erreur WebConfigChanged (retention) : " + ex.Message);
            }

            // --- DVBViewer switch ---
            try
            {
                Thread.Sleep(200);

                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "MediaMonitor.Web.config"
                );

                if (!File.Exists(path))
                    return;

                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("DvbViewerSwitch=", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line.Split('=')[1].Trim();
                        bool enabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);

                        if (enabled != _dvbViewerEnabled)
                        {
                            _dvbViewerEnabled = enabled;
                            Program.Engine.DvbViewerEnabled = enabled;

                            CoreLog.Write("DVBViewer RS " + (enabled ? "ACTIVÉ" : "DÉSACTIVÉ") + " via Web.config");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR WebConfigChanged (DVBViewer) : " + ex);
            }
        }

        public void Dispose()
        {
            _shutdownWatcher?.Dispose();
            _webConfigWatcher?.Dispose();
        }
    }
}