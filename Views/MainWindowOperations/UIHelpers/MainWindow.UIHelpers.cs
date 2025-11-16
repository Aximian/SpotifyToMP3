using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MediaConverterToMP3.Models;
using MediaConverterToMP3.Views.MainWindowOperations.Utilities;

namespace MediaConverterToMP3.Views
{
    public partial class MainWindow
    {
        private void UpdateSourceSelector()
        {
            if (_selectedSource == "Spotify")
            {
                // Spotify selected - green and black
                SpotifySelector.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954"));
                SpotifySelector.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(29, 185, 84),
                    Direction = 270,
                    ShadowDepth = 3,
                    BlurRadius = 8,
                    Opacity = 0.5
                };
                ((System.Windows.Controls.TextBlock)((System.Windows.Controls.StackPanel)SpotifySelector.Child).Children[1]).Foreground =
                    System.Windows.Media.Brushes.White;

                // YouTube unselected - dark
                YouTubeSelector.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#282828"));
                YouTubeSelector.Effect = null;
                ((System.Windows.Controls.TextBlock)((System.Windows.Controls.StackPanel)YouTubeSelector.Child).Children[1]).Foreground =
                    new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B3B3B3"));

                TitleText.Text = "🎵 Spotify to MP3";
                SubtitleText.Text = "Convert your favorite Spotify tracks to MP3";

                // Hide format selector for Spotify
                FormatSelectorPanel.Visibility = Visibility.Collapsed;

                // Update tooltip
                if (SearchTextBox.ToolTip is ToolTip tooltip)
                {
                    tooltip.Content = "Enter a song name, artist, or paste a Spotify track/playlist URL. Plain text will search Spotify for similar songs.";
                }

                // Update empty state text
                EmptyStateText.Text = "🎵 Search for tracks, or paste a Spotify track/playlist URL to get started";
            }
            else // YouTube
            {
                // YouTube selected - blue and red gradient
                var gradientBrush = new System.Windows.Media.LinearGradientBrush();
                gradientBrush.StartPoint = new System.Windows.Point(0, 0);
                gradientBrush.EndPoint = new System.Windows.Point(1, 0);
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromRgb(255, 0, 0), 0.0)); // Red
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromRgb(0, 0, 255), 1.0)); // Blue
                YouTubeSelector.Background = gradientBrush;
                YouTubeSelector.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(0, 0, 255),
                    Direction = 270,
                    ShadowDepth = 3,
                    BlurRadius = 8,
                    Opacity = 0.5
                };
                ((System.Windows.Controls.TextBlock)((System.Windows.Controls.StackPanel)YouTubeSelector.Child).Children[1]).Foreground =
                    System.Windows.Media.Brushes.White;

                // Spotify unselected - dark
                SpotifySelector.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#282828"));
                SpotifySelector.Effect = null;
                ((System.Windows.Controls.TextBlock)((System.Windows.Controls.StackPanel)SpotifySelector.Child).Children[1]).Foreground =
                    new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B3B3B3"));

                // Show format selector for YouTube
                FormatSelectorPanel.Visibility = Visibility.Visible;
                UpdateFormatSelector();

                string formatText = _selectedFormat == "MP4" ? "MP4" : "MP3";
                TitleText.Text = $"▶️ YouTube to {formatText}";
                SubtitleText.Text = $"Convert your favorite YouTube videos to {formatText}";

                // Update tooltip
                if (SearchTextBox.ToolTip is ToolTip tooltip)
                {
                    tooltip.Content = "Enter a song name or paste a YouTube video URL. Plain text will search YouTube for similar videos.";
                }

                // Update empty state text
                EmptyStateText.Text = "▶️ Search for videos, or paste a YouTube video URL to get started";
            }
        }

        private void UpdateFormatSelector()
        {
            if (_selectedFormat == "MP3")
            {
                // MP3 selected - green
                MP3FormatSelector.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954"));
                MP3FormatSelector.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(29, 185, 84),
                    Direction = 270,
                    ShadowDepth = 2,
                    BlurRadius = 6,
                    Opacity = 0.4
                };
                ((System.Windows.Controls.TextBlock)MP3FormatSelector.Child).Foreground = System.Windows.Media.Brushes.White;

                // MP4 unselected - dark
                MP4FormatSelector.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#282828"));
                MP4FormatSelector.Effect = null;
                ((System.Windows.Controls.TextBlock)MP4FormatSelector.Child).Foreground =
                    new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B3B3B3"));
            }
            else // MP4
            {
                // MP4 selected - green
                MP4FormatSelector.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954"));
                MP4FormatSelector.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(29, 185, 84),
                    Direction = 270,
                    ShadowDepth = 2,
                    BlurRadius = 6,
                    Opacity = 0.4
                };
                ((System.Windows.Controls.TextBlock)MP4FormatSelector.Child).Foreground = System.Windows.Media.Brushes.White;

                // MP3 unselected - dark
                MP3FormatSelector.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#282828"));
                MP3FormatSelector.Effect = null;
                ((System.Windows.Controls.TextBlock)MP3FormatSelector.Child).Foreground =
                    new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B3B3B3"));
            }

            // Refresh track status when format changes
            RefreshTrackDownloadStatus();
        }

        private void ShowErrorDialog(string title, string message)
        {
            var errorDialog = new ErrorDialog(title, message)
            {
                Owner = this
            };
            errorDialog.ShowDialog();
        }

        private void RefreshTrackDownloadStatus()
        {
            // Refresh tracks for both YouTube and Spotify sources
            foreach (var track in ResultsList.Items.OfType<TrackItem>())
            {
                // Skip tracks that are currently downloading or just downloaded in this session
                if (track.IsDownloading || track.DownloadButtonText == "Downloaded ✓")
                    continue;
                
                // Update status for all other tracks (including "Already Downloaded ✓" ones)
                CheckAndUpdateTrackDownloadStatus(track);
            }
        }
        
        private void CheckAndUpdateTrackDownloadStatus(TrackItem track)
        {
            string fileNameTitle = string.IsNullOrWhiteSpace(track.Title) ? "Unknown Title" : track.Title;
            string fileNameArtist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown Artist" : track.Artist;
            string baseFileName = $"{FileUtilities.SanitizeFileName(fileNameTitle)} - {FileUtilities.SanitizeFileName(fileNameArtist)}";
            
            // Hide Spotify button for Spotify source (no need to add Spotify tracks to Spotify Local)
            if (_selectedSource == "Spotify")
            {
                track.SpotifyButtonText = "";
            }
            // Hide Spotify button when MP4 format is selected (MP4 cannot be added to Spotify)
            else if (_selectedSource == "YouTube" && _selectedFormat == "MP4")
            {
                track.SpotifyButtonText = "";
            }
            // Check Spotify button status for YouTube MP3 tracks
            else if (!string.IsNullOrEmpty(_spotifyLocalFilesPath))
            {
                string spotifyFilePath = Path.Combine(_spotifyLocalFilesPath, $"{baseFileName}.mp3");
                if (File.Exists(spotifyFilePath))
                {
                    track.SpotifyButtonText = "Already in Spotify ✓";
                }
                else
                {
                    // Check if MP3 exists in download folder (can be added to Spotify)
                    string mp3Path = Path.Combine(_downloadPath, $"{baseFileName}.mp3");
                    if (File.Exists(mp3Path))
                    {
                        track.SpotifyButtonText = "Add to Spotify Local";
                    }
                    else
                    {
                        track.SpotifyButtonText = "Add to Spotify Local";
                    }
                }
            }
            else
            {
                track.SpotifyButtonText = "Add to Spotify Local";
            }
            
            if (_selectedSource == "YouTube")
            {
                // For YouTube: Check both MP3 and MP4
                string mp3Path = Path.Combine(_downloadPath, $"{baseFileName}.mp3");
                string mp4Path = Path.Combine(_downloadPath, $"{baseFileName}.mp4");
                bool mp3Exists = File.Exists(mp3Path);
                bool mp4Exists = File.Exists(mp4Path);
                
                // Check if the currently selected format exists
                bool selectedFormatExists = (_selectedFormat == "MP4") ? mp4Exists : mp3Exists;
                
                // Allow download if the selected format doesn't exist (even if the other format exists)
                track.CanDownload = !selectedFormatExists;
                
                // Update button text to show which formats are available
                if (mp3Exists && mp4Exists)
                {
                    track.DownloadButtonText = "Already Downloaded ✓ (MP3+MP4)";
                }
                else if (mp3Exists)
                {
                    track.DownloadButtonText = _selectedFormat == "MP3" ? "Already Downloaded ✓ (MP3)" : "Download (MP3 exists)";
                }
                else if (mp4Exists)
                {
                    track.DownloadButtonText = _selectedFormat == "MP4" ? "Already Downloaded ✓ (MP4)" : "Download (MP4 exists)";
                }
                else
                {
                    track.DownloadButtonText = "Download";
                }
            }
            else
            {
                // For Spotify: Only check MP3
                string mp3Path = Path.Combine(_downloadPath, $"{baseFileName}.mp3");
                bool mp3Exists = File.Exists(mp3Path);
                
                track.CanDownload = !mp3Exists;
                track.DownloadButtonText = mp3Exists ? "Already Downloaded ✓" : "Download";
            }
        }

        private void LoadFilterIcon()
        {
            try
            {
                if (FilterIcon == null) return;

                // Try multiple paths
                string[] possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "filter.png"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "filter.png"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Assets", "filter.png"),
                    Path.Combine(Directory.GetCurrentDirectory(), "filter.png")
                };

                string? filterPath = null;
                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        filterPath = path;
                        break;
                    }
                }

                if (filterPath != null)
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(filterPath, UriKind.Absolute);
                    bitmap.DecodePixelWidth = 24;
                    bitmap.DecodePixelHeight = 24;
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    Dispatcher.Invoke(() =>
                    {
                        FilterIcon.Source = bitmap;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load filter icon: {ex.Message}");
            }
        }
    }
}

