using System.Windows;

namespace MediaConverterToMP3.Views
{
    public partial class ErrorDialog : Window
    {
        public ErrorDialog(string title, string message)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

