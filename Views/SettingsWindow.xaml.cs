using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace SpotifyToMP3.Views
{
    public partial class SettingsWindow : Window
    {
        public string DownloadPath { get; private set; }
        public string? SpotifyClientId { get; private set; }
        public string? SpotifyClientSecret { get; private set; }

        public SettingsWindow(string currentPath, string? clientId = null, string? clientSecret = null)
        {
            InitializeComponent();
            DownloadPath = currentPath;
            PathTextBlock.Text = DownloadPath;
            
            // Load existing credentials if provided
            if (!string.IsNullOrEmpty(clientId))
            {
                ClientIdTextBox.Text = clientId;
            }
            if (!string.IsNullOrEmpty(clientSecret))
            {
                ClientSecretPasswordBox.Password = clientSecret;
            }

            // Try to set window icon if PNG file exists
            try
            {
                string pngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.png");
                if (File.Exists(pngPath))
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(pngPath);
                    bitmap.DecodePixelWidth = 32;
                    bitmap.DecodePixelHeight = 32;
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    this.Icon = bitmap;
                }
            }
            catch
            {
                // Ignore icon errors - app will work without icon
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new FolderBrowserDialog
            {
                Description = "Select folder to save downloaded tracks",
                SelectedPath = DownloadPath,
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                DownloadPath = folderDialog.SelectedPath;
                PathTextBlock.Text = DownloadPath;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate path
            if (string.IsNullOrWhiteSpace(DownloadPath))
            {
                System.Windows.MessageBox.Show("Please select a valid download path.",
                    "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Create directory if it doesn't exist
            try
            {
                if (!Directory.Exists(DownloadPath))
                {
                    Directory.CreateDirectory(DownloadPath);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to create directory:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Save credentials (can be empty - user will be warned)
            SpotifyClientId = string.IsNullOrWhiteSpace(ClientIdTextBox.Text) ? null : ClientIdTextBox.Text.Trim();
            SpotifyClientSecret = string.IsNullOrWhiteSpace(ClientSecretPasswordBox.Password) ? null : ClientSecretPasswordBox.Password.Trim();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

