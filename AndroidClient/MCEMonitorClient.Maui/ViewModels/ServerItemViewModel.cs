using System.ComponentModel;
using System.Runtime.CompilerServices;
using MCEMonitorClient.Shared.Models;
using Microsoft.Maui.Graphics;

namespace MCEMonitorClient.Maui.ViewModels
{
    public class ServerItemViewModel : INotifyPropertyChanged
    {
        public ServerEntry Server { get; }

        public ServerItemViewModel(ServerEntry server)
        {
            Server = server;
        }

        public string Name => Server.Name;
        public string FullUrl => Server.FullUrl;
        public string ServiceType => Server.ServiceType;
        public string EnabledText => Server.Enabled ? "Actif" : "Inactif";

        private Color _statusColor = Color.FromArgb("#4C4C4C");
        public Color StatusColor
        {
            get => _statusColor;
            set
            {
                if (_statusColor != value)
                {
                    _statusColor = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}