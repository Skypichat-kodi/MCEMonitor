using System.Windows;

namespace NetMonitor.UI
{
    public partial class InputBoxWindow : Window
    {
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