using System.Windows;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NetMonitor.UI
{
    public partial class InputBoxWindow : Window
    {
        // ============================================================
        //  Barre de titre personnalisée (WindowStyle=None)
        //  Ce dialogue n'a pas d'en-tête natif : le XAML en ajoute un (titre lié
        //  à Window.Title + bouton Fermer). Pas de Réduire/Agrandir.
        // ============================================================

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr fenetre, int attribut, ref int valeur, int taille);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;   // Windows 11
        private const int DWMWCP_ROUND                   = 2;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                int arrondi = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref arrondi, sizeof(int));
            }
            catch
            {
                // Windows 10 : rien à faire, la fenêtre reste à angles droits.
            }
        }

        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { }
            }
        }

        private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

        public string Value { get; private set; } = "";

        public InputBoxWindow(string title, string message, string initial = "")
        {
            InitializeComponent();
            Title = title;
            LblMessage.Text = message;
            TxtValue.Text = initial;
            TxtValue.SelectAll();
            TxtValue.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Value = TxtValue.Text.Trim();
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}