using System.ComponentModel;
using System.Windows.Media;

namespace MCEMonitorClient.Config.Models
{
    /// <summary>
    /// Ligne affichée dans la grille des serveurs.
    ///
    /// Elle enveloppe une ServerEntry et porte l'état de disponibilité, qui vient
    /// des appels HTTP et doit se rafraîchir sans reconstruire la liste : d'où
    /// INotifyPropertyChanged.
    /// </summary>
    public class LigneServeur : INotifyPropertyChanged
    {
        private static readonly Brush PinceauOk       = Figer(0x6C, 0xCB, 0x5F);
        private static readonly Brush PinceauAlerte   = Figer(0xFF, 0xB9, 0x00);
        private static readonly Brush PinceauCritique = Figer(0xFF, 0x63, 0x47);
        private static readonly Brush PinceauInconnu  = Figer(0x88, 0x88, 0x88);
        private static readonly Brush PinceauInactif  = Figer(0x50, 0x50, 0x50);

        private static Brush Figer(byte r, byte g, byte b)
        {
            var pinceau = new SolidColorBrush(Color.FromRgb(r, g, b));
            pinceau.Freeze();
            return pinceau;
        }

        private string _etat = "offline";

        public LigneServeur(ServerEntry source)
        {
            Source = source;
        }

        public ServerEntry Source { get; }

        public string Name        => Source.Name;
        public string FullUrl     => Source.FullUrl;
        public string ServiceType => Source.ServiceType;
        public string Username    => Source.Username;
        public bool   Enabled     => Source.Enabled;

        /// <summary>« ok », « warning », « critical » ou « offline ».</summary>
        public string Etat
        {
            get => _etat;
            set
            {
                if (_etat == value) return;
                _etat = value;
                OnPropertyChanged(nameof(Etat));
                OnPropertyChanged(nameof(CouleurEtat));
            }
        }

        /// <summary>Couleur de la pastille, grise quand le serveur est inactif.</summary>
        public Brush CouleurEtat =>
            !Enabled ? PinceauInactif :
            Etat switch
            {
                "ok"       => PinceauOk,
                "warning"  => PinceauAlerte,
                "critical" => PinceauCritique,
                _          => PinceauInconnu
            };

        /// <summary>À appeler quand la configuration de la ligne a été modifiée.</summary>
        public void RafraichirProprietes()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(FullUrl));
            OnPropertyChanged(nameof(ServiceType));
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(CouleurEtat));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string nom) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
    }
}
