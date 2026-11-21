using System.Windows;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Façade de remplacement de MessageBox : même signature, donc les appels
    /// existants se contentent d'ajouter la fenêtre propriétaire.
    /// </summary>
    public static class Boite
    {
        public static MessageBoxResult Show(
            Window proprietaire,
            string message,
            string titre,
            MessageBoxButton boutons = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.Information)
        {
            bool confirmation = boutons == MessageBoxButton.YesNo;
            bool alerte = image == MessageBoxImage.Warning || image == MessageBoxImage.Error;

            var fenetre = new FenetreConfirmation(message, titre, confirmation, alerte);

            if (proprietaire != null && proprietaire.IsLoaded)
                fenetre.Owner = proprietaire;

            fenetre.ShowDialog();

            return fenetre.Resultat;
        }
    }
}
