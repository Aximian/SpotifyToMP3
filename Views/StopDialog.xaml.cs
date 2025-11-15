using System.Windows;

namespace SpotifyToMP3.Views
{
    public partial class StopDialog : Window
    {
        public bool StopConfirmed { get; private set; }

        public StopDialog(string message = "Are you sure you want to stop all downloads?")
        {
            InitializeComponent();
            MessageText.Text = message;
            StopConfirmed = false;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            StopConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}

