using System.Windows;

namespace MediaConverterToMP3.Views
{
    public partial class CompletionDialog : Window
    {
        public CompletionDialog(string title, string message, bool isSuccess)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;

            // Change color based on success/cancellation
            if (isSuccess)
            {
                TitleText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954"));
            }
            else
            {
                TitleText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF6B00"));
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

