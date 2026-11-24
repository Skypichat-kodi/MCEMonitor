using System.Windows;

namespace SystemMonitor.UI
{
    /// <summary>
    /// Façade de remplacement de MessageBox : même signature, donc les appels
    /// existants se contentent d'ajouter la fenêtre propriétaire. Le titre est
    /// facultatif — à défaut, celui de la fenêtre appelante.
    /// </summary>
    public static class Boite
    {
        public static MessageBoxResult Show(
            Window? proprietaire,
            string message,
            string? titre = null,
            MessageBoxButton boutons = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.Information)
        {
            bool confirmation = boutons == MessageBoxButton.YesNo;
            bool alerte = image == MessageBoxImage.Warning || image == MessageBoxImage.Error;

            var fenetre = new FenetreDialogue(message, titre ?? proprietaire?.Title ?? "SystemMonitor",
                                              confirmation, alerte);

            if (proprietaire != null && proprietaire.IsLoaded)
                fenetre.Owner = proprietaire;

            fenetre.ShowDialog();

            return fenetre.Resultat;
        }
    }
}
