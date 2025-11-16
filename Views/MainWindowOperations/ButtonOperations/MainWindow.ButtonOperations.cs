using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediaConverterToMP3.Models;
using MediaConverterToMP3.Views.MainWindowOperations.Utilities;

namespace MediaConverterToMP3.Views
{
    public partial class MainWindow
    {
        private void SourceSelector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as System.Windows.Controls.Border;
            if (border?.Tag?.ToString() == "Spotify")
            {
                _selectedSource = "Spotify";
            }
            else if (border?.Tag?.ToString() == "YouTube")
            {
                _selectedSource = "YouTube";
            }
            UpdateSourceSelector();
        }

        private void FormatSelector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as System.Windows.Controls.Border;
            if (border?.Tag?.ToString() == "MP3")
            {
                _selectedFormat = "MP3";
            }
            else if (border?.Tag?.ToString() == "MP4")
            {
                _selectedFormat = "MP4";
            }
            UpdateFormatSelector();
            
            // Update title and subtitle
            if (_selectedSource == "YouTube")
            {
                string formatText = _selectedFormat == "MP4" ? "MP4" : "MP3";
                TitleText.Text = $"▶️ YouTube to {formatText}";
                SubtitleText.Text = $"Convert your favorite YouTube videos to {formatText}";
            }
            
            // Re-check all tracks for the new format
            RefreshTrackDownloadStatus();
        }

        // Note: SearchButton_Click, SearchTextBox_KeyDown, SearchTextBox_TextChanged, and FilterTextBox_TextChanged are in SearchOperations
        // Note: DownloadButton_Click and DownloadAndConvertTrack are in DownloadOperations
        // Note: DownloadAllButton_Click is in DownloadOperations

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Load current settings to pass to settings window
            var currentSettings = Models.AppSettings.Load();
            var settingsWindow = new SettingsWindow(_downloadPath, currentSettings.SpotifyClientId, currentSettings.SpotifyClientSecret, currentSettings.SpotifyLocalFilesPath)
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() == true)
            {
                // Update download path
                _downloadPath = settingsWindow.DownloadPath;

                // Update credentials if changed
                bool credentialsChanged = false;
                if (_clientId != settingsWindow.SpotifyClientId || _clientSecret != settingsWindow.SpotifyClientSecret)
                {
                    _clientId = settingsWindow.SpotifyClientId ?? _clientId;
                    _clientSecret = settingsWindow.SpotifyClientSecret ?? _clientSecret;
                    credentialsChanged = true;
                }

                // Update Spotify local files path
                _spotifyLocalFilesPath = settingsWindow.SpotifyLocalFilesPath;

                // Save settings
                var settings = new Models.AppSettings
                {
                    DownloadPath = _downloadPath,
                    SpotifyClientId = _clientId,
                    SpotifyClientSecret = _clientSecret,
                    SpotifyLocalFilesPath = _spotifyLocalFilesPath
                };
                settings.Save();

                // Ensure directory exists
                if (!Directory.Exists(_downloadPath))
                {
                    Directory.CreateDirectory(_downloadPath);
                }

                // Reinitialize Spotify connection if credentials changed
                if (credentialsChanged)
                {
                    StatusText.Text = "Reconnecting to Spotify with new credentials...";
                    InitializeSpotify();
                }
                else
                {
                    StatusText.Text = $"Download path updated to: {_downloadPath}";
                }
            }
        }

        private void StopDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCancellationTokenSource != null && !_downloadCancellationTokenSource.IsCancellationRequested)
            {
                // Show confirmation dialog
                var stopDialog = new StopDialog("Are you sure you want to stop all downloads? Progress will be lost for tracks that haven't finished.")
                {
                    Owner = this
                };

                if (stopDialog.ShowDialog() == true && stopDialog.StopConfirmed)
                {
                    _downloadCancellationTokenSource.Cancel();

                    // Kill all active processes
                    lock (_activeProcesses ?? new List<System.Diagnostics.Process>())
                    {
                        if (_activeProcesses != null)
                        {
                            foreach (var process in _activeProcesses.ToList())
                            {
                                try
                                {
                                    if (!process.HasExited)
                                    {
                                        process.Kill();
                                    }
                                }
                                catch { }
                            }
                            _activeProcesses.Clear();
                        }
                    }

                    StatusText.Text = "Stopping downloads...";
                }
            }
        }

        private void AddToSpotifyButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var track = button?.Tag as TrackItem;

            if (track == null) return;

            // Check if Spotify local files path is configured
            if (string.IsNullOrEmpty(_spotifyLocalFilesPath))
            {
                StatusText.Text = "Please configure Spotify Local Files Path in Settings first.";
                var result = System.Windows.MessageBox.Show(
                    "Spotify Local Files Path is not configured.\n\nWould you like to open Settings?",
                    "Spotify Path Not Configured",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    SettingsButton_Click(sender, e);
                }
                return;
            }

            try
            {
                // Check if the file exists in download folder
                string fileNameTitle = string.IsNullOrWhiteSpace(track.Title) ? "Unknown Title" : track.Title;
                string fileNameArtist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown Artist" : track.Artist;
                string fileName = $"{FileUtilities.SanitizeFileName(fileNameTitle)} - {FileUtilities.SanitizeFileName(fileNameArtist)}.mp3";
                string sourcePath = Path.Combine(_downloadPath, fileName);

                // Check if MP3 exists, if not check MP4
                if (!File.Exists(sourcePath))
                {
                    string mp4Path = Path.Combine(_downloadPath, $"{FileUtilities.SanitizeFileName(fileNameTitle)} - {FileUtilities.SanitizeFileName(fileNameArtist)}.mp4");
                    if (File.Exists(mp4Path))
                    {
                        StatusText.Text = "MP4 files cannot be added to Spotify. Please download as MP3 first.";
                        return;
                    }
                    else
                    {
                        StatusText.Text = $"File not found: {track.Title}. Please download it first.";
                        return;
                    }
                }

                // Check if already in Spotify folder
                string spotifyFilePath = Path.Combine(_spotifyLocalFilesPath, fileName);
                if (File.Exists(spotifyFilePath))
                {
                    track.SpotifyButtonText = "Already in Spotify ✓";
                    StatusText.Text = $"Already in Spotify local: {track.Title}";
                    return;
                }

                // Ensure Spotify folder exists
                if (!Directory.Exists(_spotifyLocalFilesPath))
                {
                    Directory.CreateDirectory(_spotifyLocalFilesPath);
                }

                // Copy file to Spotify folder
                File.Copy(sourcePath, spotifyFilePath, true);

                // Update button text
                track.SpotifyButtonText = "Successfully Added ✓";
                StatusText.Text = $"Successfully added to Spotify local: {track.Title}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to add to Spotify: {ex.Message}";
                var errorDialog = new ErrorDialog("Error", $"Failed to add {track.Title} to Spotify:\n{ex.Message}") { Owner = this };
                errorDialog.ShowDialog();
            }
        }

        private System.Windows.Controls.Primitives.Popup? _folderMenuPopup;

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button == null) return;

            // Close existing popup if open
            if (_folderMenuPopup != null && _folderMenuPopup.IsOpen)
            {
                _folderMenuPopup.IsOpen = false;
                return;
            }

            // Create popup for dropdown menu
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };

            // Create border container with styling - match button width
            var menuBorder = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 40)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 185, 84)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0, 0, 25, 25),
                Padding = new Thickness(4),
                Width = button.ActualWidth > 0 ? button.ActualWidth : button.MinWidth
            };

            // Create stack panel for menu items
            var stackPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical
            };

            // Create download folder button
            var downloadButton = new System.Windows.Controls.Button
            {
                Content = "📁 Download Folder",
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(15, 12, 15, 12),
                FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            downloadButton.Click += (s, args) =>
            {
                popup.IsOpen = false;
                OpenDownloadFolder();
            };
            downloadButton.MouseEnter += (s, args) =>
            {
                downloadButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 185, 84));
            };
            downloadButton.MouseLeave += (s, args) =>
            {
                downloadButton.Background = System.Windows.Media.Brushes.Transparent;
            };
            stackPanel.Children.Add(downloadButton);

            // Create Spotify folder button
            var spotifyButton = new System.Windows.Controls.Button
            {
                Content = "🎵 Spotify Folder",
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(15, 12, 15, 12),
                FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            spotifyButton.Click += (s, args) =>
            {
                popup.IsOpen = false;
                OpenSpotifyFolder();
            };
            spotifyButton.MouseEnter += (s, args) =>
            {
                spotifyButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 185, 84));
            };
            spotifyButton.MouseLeave += (s, args) =>
            {
                spotifyButton.Background = System.Windows.Media.Brushes.Transparent;
            };
            stackPanel.Children.Add(spotifyButton);

            menuBorder.Child = stackPanel;
            popup.Child = menuBorder;

            _folderMenuPopup = popup;
            popup.IsOpen = true;
        }

        private void OpenDownloadFolder()
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(_downloadPath))
                {
                    Directory.CreateDirectory(_downloadPath);
                }

                // Open the folder in Windows Explorer
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_downloadPath}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open download folder:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSpotifyFolder()
        {
            try
            {
                if (string.IsNullOrEmpty(_spotifyLocalFilesPath))
                {
                    System.Windows.MessageBox.Show("Spotify Local Files Path is not configured in Settings.",
                        "Path Not Configured", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Ensure directory exists
                if (!Directory.Exists(_spotifyLocalFilesPath))
                {
                    Directory.CreateDirectory(_spotifyLocalFilesPath);
                }

                // Open the folder in Windows Explorer
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_spotifyLocalFilesPath}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open Spotify folder:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

