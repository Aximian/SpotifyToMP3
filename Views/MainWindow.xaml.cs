using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using MediaConverterToMP3.Models;

namespace MediaConverterToMP3.Views
{
    public partial class MainWindow : Window
    {
        private HttpClient _httpClient;
        private string? _accessToken;
        private ObservableCollection<TrackItem> _tracks;
        private ObservableCollection<TrackItem> _allTracks; // Store all tracks for filtering
        private string _downloadPath;
        private string _clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? "YOUR_CLIENT_ID_HERE";
        private string _clientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET") ?? "YOUR_CLIENT_SECRET_HERE";
        private CancellationTokenSource? _downloadCancellationTokenSource;
        private List<System.Diagnostics.Process>? _activeProcesses;
        private readonly object _processLock = new object();
        private string _selectedSource = "Spotify"; // "Spotify" or "YouTube"
        private string _selectedFormat = "MP3"; // "MP3" or "MP4" (only for YouTube)
        private bool _isSpotifyPlaylist = false;
        private bool _isDownloadingAll = false;
        private CancellationTokenSource? _downloadAllCancellationTokenSource = null;
        private int _displayedProgressPercent = 0;
        private System.Windows.Threading.DispatcherTimer? _progressTimer;
        private TrackItem? _currentDownloadingTrack;

        public MainWindow()
        {
            InitializeComponent();
            _tracks = new ObservableCollection<TrackItem>();
            _allTracks = new ObservableCollection<TrackItem>();
            _activeProcesses = new List<System.Diagnostics.Process>();
            ResultsList.ItemsSource = _tracks;

            // Load settings
            var settings = Models.AppSettings.Load();
            _downloadPath = settings.DownloadPath;
            _httpClient = new HttpClient();

            // Load credentials from settings (with fallback to environment variables)
            if (!string.IsNullOrEmpty(settings.SpotifyClientId) && !string.IsNullOrEmpty(settings.SpotifyClientSecret))
            {
                _clientId = settings.SpotifyClientId;
                _clientSecret = settings.SpotifyClientSecret;
            }
            else
            {
                // Fallback to environment variables
                var envClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
                var envClientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET");
                if (!string.IsNullOrEmpty(envClientId)) _clientId = envClientId;
                if (!string.IsNullOrEmpty(envClientSecret)) _clientSecret = envClientSecret;
            }

            if (!Directory.Exists(_downloadPath))
            {
                Directory.CreateDirectory(_downloadPath);
            }

            // Try to set window icon if PNG file exists
            try
            {
                string pngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.png");
                if (File.Exists(pngPath))
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(pngPath);
                    bitmap.DecodePixelWidth = 32; // Set size to prevent distortion
                    bitmap.DecodePixelHeight = 32;
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze(); // Freeze for better performance
                    this.Icon = bitmap;
                }
            }
            catch
            {
                // Ignore icon errors - app will work without icon
            }

            InitializeSpotify();
            UpdateSourceSelector();
            LoadFilterIcon();
            
            // Focus search box when window loads so caret is visible immediately
            this.Loaded += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SearchTextBox.Focus();
                    Keyboard.Focus(SearchTextBox);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };
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

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cancel any ongoing downloads
            _downloadCancellationTokenSource?.Cancel();

            // Kill all active processes immediately
            lock (_processLock)
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
                            process.Dispose();
                        }
                        catch { /* Ignore errors when killing processes */ }
                    }
                    _activeProcesses.Clear();
                }
            }

            // Dispose cancellation token
            try
            {
                _downloadCancellationTokenSource?.Dispose();
            }
            catch { }

            // Dispose HTTP client
            try
            {
                _httpClient?.Dispose();
            }
            catch { }

            // Force immediate exit - don't wait for anything
            e.Cancel = false;
        }

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

        private void SourceSelector_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
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

        private void UpdateFormatSelector()
        {
            if (_selectedFormat == "MP3")
            {
                // MP3 selected
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

                // MP4 unselected
                MP4FormatSelector.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#282828"));
                MP4FormatSelector.Effect = null;
                ((System.Windows.Controls.TextBlock)MP4FormatSelector.Child).Foreground =
                    new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B3B3B3"));
            }
            else // MP4
            {
                // MP4 selected
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

                // MP3 unselected
                MP3FormatSelector.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#282828"));
                MP3FormatSelector.Effect = null;
                ((System.Windows.Controls.TextBlock)MP3FormatSelector.Child).Foreground =
                    new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B3B3B3"));
            }
        }

        private void FormatSelector_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
        
        private void RefreshTrackDownloadStatus()
        {
            // Only refresh if we're on YouTube (where format matters)
            if (_selectedSource != "YouTube")
                return;
            
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
            string baseFileName = $"{SanitizeFileName(fileNameTitle)} - {SanitizeFileName(fileNameArtist)}";
            
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

        private async void InitializeSpotify()
        {
            // Check if credentials are set
            if (string.IsNullOrEmpty(_clientId) || _clientId == "YOUR_CLIENT_ID_HERE" ||
                string.IsNullOrEmpty(_clientSecret) || _clientSecret == "YOUR_CLIENT_SECRET_HERE")
            {
                StatusText.Text = "⚠️ Spotify credentials not set. Go to Settings (⚙️) to configure.";
                return;
            }

            try
            {
                StatusText.Text = "Connecting to Spotify...";

                // Get access token using client credentials flow
                var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
                tokenRequest.Headers.Add("Authorization", $"Basic {credentials}");
                tokenRequest.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

                var tokenResponse = await _httpClient.SendAsync(tokenRequest);
                var tokenContent = await tokenResponse.Content.ReadAsStringAsync();

                if (!tokenResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to get access token: {tokenContent}");
                }

                var tokenData = JsonConvert.DeserializeObject<Models.TokenResponse>(tokenContent);
                _accessToken = tokenData?.AccessToken;

                StatusText.Text = "Connected to Spotify. Ready to search!";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error connecting to Spotify: {ex.Message}";
                System.Windows.MessageBox.Show($"Failed to connect to Spotify:\n{ex.Message}\n\nPlease check your Client ID and Secret in Settings (⚙️ button).",
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SearchButton_Click(sender, e);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Optional: Enable/disable search button based on text
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filterText = FilterTextBox.Text.ToLower();

            _tracks.Clear();

            foreach (var track in _allTracks)
            {
                if (string.IsNullOrEmpty(filterText) ||
                    track.Title.ToLower().Contains(filterText) ||
                    track.Artist.ToLower().Contains(filterText) ||
                    track.Album.ToLower().Contains(filterText))
                {
                    _tracks.Add(track);
                }
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSource == "Spotify" && string.IsNullOrEmpty(_accessToken))
            {
                System.Windows.MessageBox.Show("Spotify credentials not configured.\n\nPlease go to Settings (⚙️ button) and enter your Spotify Client ID and Client Secret.\n\nSee README.md for instructions on getting credentials.",
                    "Credentials Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string searchQuery = SearchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(searchQuery))
            {
                // Do nothing if search box is empty
                return;
            }

            try
            {
                StatusText.Text = "Loading...";
                SearchButton.IsEnabled = false;
                _tracks.Clear();
                _allTracks.Clear();
                FilterTextBox.Visibility = Visibility.Collapsed;
                DownloadAllButton.Visibility = Visibility.Collapsed;

                if (_selectedSource == "Spotify")
                {
                    // Check if input looks like a URL but is not Spotify
                    if (IsUrl(searchQuery) && !IsSpotifyUrl(searchQuery))
                    {
                        ShowErrorDialog("Invalid URL",
                            $"The URL you entered is not a valid Spotify URL.\n\nPlease enter:\n• A Spotify track URL (e.g., https://open.spotify.com/track/...)\n• A Spotify playlist URL (e.g., https://open.spotify.com/playlist/...)\n• Or a song name to search");
                        return;
                    }

                    // Check if input is a Spotify playlist URL
                    string? playlistId = ExtractPlaylistId(searchQuery);
                    if (!string.IsNullOrEmpty(playlistId))
                    {
                        // Load playlist tracks
                        _isSpotifyPlaylist = true;
                        await LoadPlaylistTracks(playlistId);
                    }
                    else
                    {
                        _isSpotifyPlaylist = false;
                        // Check if input is a Spotify track URL
                        string? trackId = ExtractTrackId(searchQuery);
                        if (!string.IsNullOrEmpty(trackId))
                        {
                            // Load single track
                            await LoadTrack(trackId);
                        }
                        else
                        {
                            // Regular search
                            await SearchTracks(searchQuery);
                        }
                    }
                }
                else // YouTube
                {
                    // Check if input looks like a URL but is not YouTube
                    if (IsUrl(searchQuery) && !IsYouTubeUrl(searchQuery))
                    {
                        ShowErrorDialog("Invalid URL",
                            $"The URL you entered is not a valid YouTube URL.\n\nPlease enter:\n• A YouTube video URL (e.g., https://www.youtube.com/watch?v=...)\n• Or a song name to search");
                        return;
                    }

                    // Check if input is a YouTube URL
                    string? videoId = ExtractYouTubeVideoId(searchQuery);
                    if (!string.IsNullOrEmpty(videoId))
                    {
                        // Load YouTube video
                        await LoadYouTubeVideo(videoId);
                    }
                    else if (IsYouTubePlaylistUrl(searchQuery))
                    {
                        // Show error for playlist (not supported yet)
                        ShowErrorDialog("Invalid URL",
                            $"YouTube playlist URLs are not currently supported.\n\nPlease use a single YouTube video URL or search for a song name.");
                    }
                    else
                    {
                        // Regular search - treat as song name
                        await LoadYouTubeSearch(searchQuery);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                ShowErrorDialog("Error", $"Operation failed:\n{ex.Message}");
            }
            finally
            {
                SearchButton.IsEnabled = true;
            }
        }

        private string? ExtractPlaylistId(string url)
        {
            // Handle various Spotify URL formats:
            // https://open.spotify.com/playlist/7a25lJG0TwlKrx5IgmcF0s?si=...
            // spotify:playlist:7a25lJG0TwlKrx5IgmcF0s
            // https://open.spotify.com/playlist/7a25lJG0TwlKrx5IgmcF0s

            if (string.IsNullOrEmpty(url))
                return null;

            // Check for spotify:playlist: format first
            if (url.StartsWith("spotify:playlist:"))
            {
                return url.Replace("spotify:playlist:", "").Split('?')[0]; // Remove query params if any
            }

            // Check for open.spotify.com URL
            if (url.Contains("open.spotify.com/playlist/"))
            {
                try
                {
                    var uri = new Uri(url);
                    var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var playlistIndex = Array.IndexOf(segments, "playlist");
                    if (playlistIndex >= 0 && playlistIndex < segments.Length - 1)
                    {
                        return segments[playlistIndex + 1];
                    }
                }
                catch (UriFormatException)
                {
                    // If URL parsing fails, try regex extraction
                    var match = System.Text.RegularExpressions.Regex.Match(url, @"playlist/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }

            return null;
        }

        private string? ExtractTrackId(string url)
        {
            // Handle various Spotify track URL formats:
            // https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC?si=...
            // spotify:track:4uLU6hMCjMI75M1A2tKUQC
            // https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC

            if (string.IsNullOrEmpty(url))
                return null;

            // Check for spotify:track: format first
            if (url.StartsWith("spotify:track:"))
            {
                return url.Replace("spotify:track:", "").Split('?')[0]; // Remove query params if any
            }

            // Check for open.spotify.com URL
            if (url.Contains("open.spotify.com/track/"))
            {
                try
                {
                    var uri = new Uri(url);
                    var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var trackIndex = Array.IndexOf(segments, "track");
                    if (trackIndex >= 0 && trackIndex < segments.Length - 1)
                    {
                        return segments[trackIndex + 1];
                    }
                }
                catch (UriFormatException)
                {
                    // If URL parsing fails, try regex extraction
                    var match = System.Text.RegularExpressions.Regex.Match(url, @"track/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }

            return null;
        }

        private string? ExtractYouTubeVideoId(string url)
        {
            // Handle various YouTube URL formats:
            // https://www.youtube.com/watch?v=dQw4w9WgXcQ
            // https://youtu.be/dQw4w9WgXcQ
            // https://www.youtube.com/embed/dQw4w9WgXcQ
            // https://m.youtube.com/watch?v=dQw4w9WgXcQ

            if (string.IsNullOrEmpty(url))
                return null;

            try
            {
                // Standard watch URL
                var watchMatch = System.Text.RegularExpressions.Regex.Match(url, @"[?&]v=([a-zA-Z0-9_-]{11})");
                if (watchMatch.Success)
                {
                    return watchMatch.Groups[1].Value;
                }

                // Short youtu.be URL
                var shortMatch = System.Text.RegularExpressions.Regex.Match(url, @"youtu\.be/([a-zA-Z0-9_-]{11})");
                if (shortMatch.Success)
                {
                    return shortMatch.Groups[1].Value;
                }

                // Embed URL
                var embedMatch = System.Text.RegularExpressions.Regex.Match(url, @"youtube\.com/embed/([a-zA-Z0-9_-]{11})");
                if (embedMatch.Success)
                {
                    return embedMatch.Groups[1].Value;
                }
            }
            catch
            {
                // Ignore regex errors
            }

            return null;
        }

        private bool IsYouTubePlaylistUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("youtube.com/playlist") || url.Contains("list=");
        }

        private async Task LoadYouTubeVideo(string videoId)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Loading YouTube video...";
            });

            // Run the process on a background thread to avoid blocking UI
            await Task.Run(() =>
            {
                // Use yt-dlp to get video info
                string? ytDlpPath = FindYtDlp();
                if (string.IsNullOrEmpty(ytDlpPath))
                {
                    throw new Exception("yt-dlp not found. Please place yt-dlp.exe in the application directory.");
                }

                // Get video information
                string infoArgs = $"--dump-json --no-playlist --extractor-args \"youtube:player_client=android\" \"https://www.youtube.com/watch?v={videoId}\"";
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = infoArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                System.Diagnostics.Process? process = null;
                try
                {
                    process = System.Diagnostics.Process.Start(processInfo);
                    if (process == null)
                    {
                        throw new Exception("Failed to start yt-dlp process.");
                    }

                    var outputBuilder = new System.Text.StringBuilder();
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    process.BeginOutputReadLine();

                    // Wait with timeout (30 seconds)
                    bool exited = process.WaitForExit(30000);
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        throw new Exception("Loading video timed out after 30 seconds.");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"Failed to get video information. Please check if the URL is valid.");
                    }

                    string jsonOutput = outputBuilder.ToString();
                    if (string.IsNullOrWhiteSpace(jsonOutput))
                    {
                        throw new Exception("Failed to get video information. The video may be unavailable.");
                    }

                    try
                    {
                        var videoInfo = JsonConvert.DeserializeObject<dynamic>(jsonOutput);
                        string title = videoInfo?.title?.ToString() ?? "";
                        string uploader = videoInfo?.uploader?.ToString() ?? "";
                        string channel = videoInfo?.channel?.ToString() ?? "";
                        string channelId = videoInfo?.channel_id?.ToString() ?? "";

                        // Extract year from upload_date or release_date
                        string year = "";
                        try
                        {
                            string? uploadDate = videoInfo?.upload_date?.ToString();
                            string? releaseDate = videoInfo?.release_date?.ToString();

                            string dateStr = releaseDate ?? uploadDate ?? "";
                            if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 4)
                            {
                                // Format: YYYYMMDD or YYYY-MM-DD
                                year = dateStr.Substring(0, 4);
                            }
                        }
                        catch { }

                        // Extract genre from tags or categories
                        string genre = "";
                        try
                        {
                            var tags = videoInfo?.tags;
                            var categories = videoInfo?.categories;

                            if (tags != null)
                            {
                                var tagsArray = tags as Newtonsoft.Json.Linq.JArray;
                                if (tagsArray != null && tagsArray.Count > 0)
                                {
                                    // Use first tag as genre, or look for common music genres
                                    string firstTag = tagsArray[0]?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(firstTag))
                                    {
                                        // Check if it's a common genre
                                        string[] commonGenres = { "Pop", "Rock", "Hip Hop", "Rap", "Country", "Jazz", "Classical", "Electronic", "R&B", "Blues", "Reggae", "Metal", "Punk", "Folk", "Indie", "Alternative" };
                                        foreach (var commonGenre in commonGenres)
                                        {
                                            if (firstTag.Contains(commonGenre, StringComparison.OrdinalIgnoreCase))
                                            {
                                                genre = commonGenre;
                                                break;
                                            }
                                        }
                                        if (string.IsNullOrEmpty(genre))
                                        {
                                            genre = firstTag;
                                        }
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(genre) && categories != null)
                            {
                                var categoriesArray = categories as Newtonsoft.Json.Linq.JArray;
                                if (categoriesArray != null && categoriesArray.Count > 0)
                                {
                                    genre = categoriesArray[0]?.ToString() ?? "";
                                }
                            }
                        }
                        catch { }

                        // Try to extract album from description or use channel name
                        string album = "";
                        try
                        {
                            // Check if there's an album field
                            string? albumField = videoInfo?.album?.ToString();
                            if (!string.IsNullOrWhiteSpace(albumField))
                            {
                                album = albumField;
                            }
                            else
                            {
                                // Try to extract from description (look for patterns like "Album: ..." or "from ...")
                                string? description = videoInfo?.description?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(description))
                                {
                                    // Look for common patterns
                                    var albumMatch = System.Text.RegularExpressions.Regex.Match(description, @"(?:Album|ALBUM|from)\s*:?\s*([^\n\r]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    if (albumMatch.Success)
                                    {
                                        album = albumMatch.Groups[1].Value.Trim();
                                    }
                                }

                                // Fallback to channel name if no album found
                                if (string.IsNullOrEmpty(album) && !string.IsNullOrEmpty(channel))
                                {
                                    album = channel;
                                }
                            }
                        }
                        catch { }

                        // Use YouTube's fast thumbnail URL format for instant loading
                        string? thumbnail = null;
                        if (!string.IsNullOrEmpty(videoId))
                        {
                            thumbnail = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";
                        }
                        else
                        {
                            thumbnail = videoInfo?.thumbnail?.ToString();
                        }
                        double? duration = videoInfo?.duration != null ? (double?)videoInfo.duration : null;

                        // Use fallback for filename if empty, but keep metadata empty
                        string fileNameTitle = string.IsNullOrWhiteSpace(title) ? "Unknown Title" : title;
                        string fileNameUploader = string.IsNullOrWhiteSpace(uploader) ? "Unknown Artist" : uploader;

                        var trackItem = new TrackItem
                        {
                            Id = videoId,
                            Title = title,
                            Artist = uploader,
                            Album = album, // Use extracted album or empty
                            AlbumArtist = channel, // Use channel as album artist
                            Year = year,
                            Genre = genre,
                            Duration = duration.HasValue ? TimeSpan.FromSeconds(duration.Value) : TimeSpan.Zero,
                            ImageUrl = thumbnail,
                            CanDownload = true, // Will be updated by CheckAndUpdateTrackDownloadStatus
                            DownloadButtonText = "Download" // Will be updated by CheckAndUpdateTrackDownloadStatus
                        };
                        
                        // Check download status for both formats
                        CheckAndUpdateTrackDownloadStatus(trackItem);

                        // Update UI on the main thread
                        Dispatcher.Invoke(() =>
                        {
                            _allTracks.Clear();
                            _tracks.Clear();
                            _allTracks.Add(trackItem);
                            _tracks.Add(trackItem);

                            StatusText.Text = $"Loaded video: {title}";
                            FilterTextBox.Visibility = Visibility.Visible;
                        });
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to parse video information: {ex.Message}");
                    }
                }
                finally
                {
                    // Ensure process is disposed
                    try
                    {
                        process?.Dispose();
                    }
                    catch { }
                }
            });
        }

        private async Task LoadYouTubeSearch(string searchQuery)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Searching YouTube...";
            });

            // Run the process on a background thread to avoid blocking UI
            await Task.Run(async () =>
            {
                // Use yt-dlp to search YouTube and get results
                string? ytDlpPath = FindYtDlp();
                if (string.IsNullOrEmpty(ytDlpPath))
                {
                    throw new Exception("yt-dlp not found. Please place yt-dlp.exe in the application directory.");
                }

                string searchUrl = $"ytsearch30:{searchQuery}";
                string infoArgs = $"--flat-playlist --dump-json --no-playlist --no-warnings --quiet --no-progress --extractor-args \"youtube:player_client=android\" \"{searchUrl}\"";

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = infoArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                System.Diagnostics.Process? process = null;
                try
                {
                    process = System.Diagnostics.Process.Start(processInfo);
                    if (process == null)
                    {
                        throw new Exception("Failed to start yt-dlp process.");
                    }

                    var outputBuilder = new System.Text.StringBuilder();
                    var errorBuilder = new System.Text.StringBuilder();
                    var outputComplete = new System.Threading.ManualResetEvent(false);
                    var errorComplete = new System.Threading.ManualResetEvent(false);

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            outputComplete.Set();
                        }
                        else if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            errorComplete.Set();
                        }
                        else if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorBuilder.AppendLine(e.Data);
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Wait for process - flat-playlist is fast
                    bool exited = process.WaitForExit(10000); // 10 seconds max
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        throw new Exception("YouTube search timed out after 10 seconds.");
                    }

                    // Wait for streams to close (with shorter timeout for speed)
                    outputComplete.WaitOne(1000);
                    errorComplete.WaitOne(1000);

                    if (process.ExitCode != 0)
                    {
                        string error = errorBuilder.ToString();
                        if (string.IsNullOrWhiteSpace(error))
                            error = outputBuilder.ToString();
                        if (string.IsNullOrWhiteSpace(error))
                            error = $"Process exited with code {process.ExitCode}";
                        throw new Exception($"YouTube search failed: {error}");
                    }

                    string output = outputBuilder.ToString();
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusText.Text = "No results found. Try different search terms.";
                        });
                        return;
                    }

                    // Parse JSON lines (each line is a separate JSON object)
                    var tracksToAdd = new List<TrackItem>();
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    int resultCount = 0;
                    const int maxResults = 30; // 30 results with full metadata

                    foreach (string line in lines)
                    {
                        if (resultCount >= maxResults)
                            break;

                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        try
                        {
                            var videoInfo = JsonConvert.DeserializeObject<dynamic>(line);
                            if (videoInfo == null)
                                continue;

                            string? videoId = videoInfo?.id?.ToString();
                            string title = videoInfo?.title?.ToString() ?? videoInfo?.name?.ToString() ?? "";
                            string uploader = videoInfo?.uploader?.ToString() ?? videoInfo?.channel?.ToString() ?? "";
                            string channel = videoInfo?.channel?.ToString() ?? "";

                            // Extract year from upload_date or release_date
                            string year = "";
                            try
                            {
                                string? uploadDate = videoInfo?.upload_date?.ToString();
                                string? releaseDate = videoInfo?.release_date?.ToString();

                                string dateStr = releaseDate ?? uploadDate ?? "";
                                if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 4)
                                {
                                    // Format: YYYYMMDD or YYYY-MM-DD
                                    year = dateStr.Substring(0, 4);
                                }
                            }
                            catch { }

                            // Extract genre from tags or categories
                            string genre = "";
                            try
                            {
                                var tags = videoInfo?.tags;
                                var categories = videoInfo?.categories;

                                if (tags != null)
                                {
                                    var tagsArray = tags as Newtonsoft.Json.Linq.JArray;
                                    if (tagsArray != null && tagsArray.Count > 0)
                                    {
                                        // Use first tag as genre, or look for common music genres
                                        string firstTag = tagsArray[0]?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(firstTag))
                                        {
                                            // Check if it's a common genre
                                            string[] commonGenres = { "Pop", "Rock", "Hip Hop", "Rap", "Country", "Jazz", "Classical", "Electronic", "R&B", "Blues", "Reggae", "Metal", "Punk", "Folk", "Indie", "Alternative" };
                                            foreach (var commonGenre in commonGenres)
                                            {
                                                if (firstTag.Contains(commonGenre, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    genre = commonGenre;
                                                    break;
                                                }
                                            }
                                            if (string.IsNullOrEmpty(genre))
                                            {
                                                genre = firstTag;
                                            }
                                        }
                                    }
                                }

                                if (string.IsNullOrEmpty(genre) && categories != null)
                                {
                                    var categoriesArray = categories as Newtonsoft.Json.Linq.JArray;
                                    if (categoriesArray != null && categoriesArray.Count > 0)
                                    {
                                        genre = categoriesArray[0]?.ToString() ?? "";
                                    }
                                }
                            }
                            catch { }

                            // Try to extract album from description or use channel name
                            string album = "";
                            try
                            {
                                // Check if there's an album field
                                string? albumField = videoInfo?.album?.ToString();
                                if (!string.IsNullOrWhiteSpace(albumField))
                                {
                                    album = albumField;
                                }
                                else
                                {
                                    // Try to extract from description (look for patterns like "Album: ..." or "from ...")
                                    string? description = videoInfo?.description?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(description))
                                    {
                                        // Look for common patterns
                                        var albumMatch = System.Text.RegularExpressions.Regex.Match(description, @"(?:Album|ALBUM|from)\s*:?\s*([^\n\r]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                        if (albumMatch.Success)
                                        {
                                            album = albumMatch.Groups[1].Value.Trim();
                                        }
                                    }

                                    // Fallback to channel name if no album found
                                    if (string.IsNullOrEmpty(album) && !string.IsNullOrEmpty(channel))
                                    {
                                        album = channel;
                                    }
                                }
                            }
                            catch { }

                            // Use YouTube's fast thumbnail URL format for instant loading
                            // Format: https://img.youtube.com/vi/{VIDEO_ID}/mqdefault.jpg (medium quality, fast)
                            string? thumbnail = null;
                            if (!string.IsNullOrEmpty(videoId))
                            {
                                thumbnail = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";
                            }
                            else
                            {
                                // Fallback to API thumbnail if videoId not available
                                try
                                {
                                    thumbnail = videoInfo?.thumbnail?.ToString();
                                    if (string.IsNullOrEmpty(thumbnail))
                                    {
                                        var thumbnails = videoInfo?.thumbnails;
                                        if (thumbnails != null)
                                        {
                                            // Safely access thumbnails array
                                            var thumbnailsArray = thumbnails as Newtonsoft.Json.Linq.JArray;
                                            if (thumbnailsArray != null && thumbnailsArray.Count > 0)
                                            {
                                                var firstThumb = thumbnailsArray[0];
                                                if (firstThumb != null)
                                                {
                                                    thumbnail = firstThumb.ToString();
                                                }
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    thumbnail = null;
                                }
                            }
                            double? duration = null;
                            if (videoInfo?.duration != null)
                                duration = (double?)videoInfo.duration;
                            else if (videoInfo?.duration_string != null)
                            {
                                // Try to parse duration string (e.g., "3:45")
                                var durationStr = videoInfo.duration_string.ToString();
                                if (System.TimeSpan.TryParse(durationStr, out TimeSpan parsedDuration))
                                    duration = parsedDuration.TotalSeconds;
                            }

                            if (string.IsNullOrEmpty(videoId))
                                continue;

                            var trackItem = new TrackItem
                            {
                                Id = videoId,
                                Title = title,
                                Artist = uploader,
                                Album = album,
                                AlbumArtist = channel,
                                Year = year,
                                Genre = genre,
                                Duration = duration.HasValue ? TimeSpan.FromSeconds(duration.Value) : TimeSpan.Zero,
                                ImageUrl = thumbnail,
                                CanDownload = true, // Will be updated by CheckAndUpdateTrackDownloadStatus
                                DownloadButtonText = "Download" // Will be updated by CheckAndUpdateTrackDownloadStatus
                            };
                            
                            // Check download status for both formats
                            CheckAndUpdateTrackDownloadStatus(trackItem);

                            tracksToAdd.Add(trackItem);
                            resultCount++;
                        }
                        catch (Exception ex)
                        {
                            // Skip invalid JSON lines
                            System.Diagnostics.Debug.WriteLine($"Failed to parse YouTube result: {ex.Message}");
                            continue;
                        }
                    }

                    // Update UI on the main thread
                    Dispatcher.Invoke(() =>
                    {
                        _allTracks.Clear();
                        _tracks.Clear();

                        foreach (var track in tracksToAdd)
                        {
                            _allTracks.Add(track);
                            _tracks.Add(track);
                        }

                        if (_tracks.Count > 0)
                        {
                            StatusText.Text = $"Found {_tracks.Count} YouTube videos";
                            FilterTextBox.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            StatusText.Text = "No results found. Try different search terms.";
                        }
                    });
                }
                finally
                {
                    // Ensure process is disposed
                    try
                    {
                        process?.Dispose();
                    }
                    catch { }
                }
            });
        }

        private bool IsUrl(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            return input.StartsWith("http://") || input.StartsWith("https://") ||
                   input.StartsWith("www.") || input.Contains("://");
        }

        private bool IsSpotifyUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("spotify.com") || url.StartsWith("spotify:");
        }

        private bool IsYouTubeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("youtube.com") || url.Contains("youtu.be");
        }

        private void ShowErrorDialog(string title, string message)
        {
            var errorDialog = new Views.ErrorDialog(title, message)
            {
                Owner = this
            };
            errorDialog.ShowDialog();
        }

        private async Task LoadTrack(string trackId)
        {
            StatusText.Text = "Loading track...";

            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.spotify.com/v1/tracks/{trackId}");
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to load track: {content}");
            }

            var trackResponse = JsonConvert.DeserializeObject<Models.TrackResponse>(content);

            if (trackResponse != null && trackResponse.Name != null)
            {
                // Get album art URL (prefer medium size, fallback to first available)
                string? imageUrl = null;
                if (trackResponse.Album?.Images != null && trackResponse.Album.Images.Length > 0)
                {
                    // Prefer medium size (around 300x300), or largest available
                    var mediumImage = trackResponse.Album.Images.FirstOrDefault(img => img.Height >= 200 && img.Height <= 400);
                    imageUrl = mediumImage?.Url ?? (trackResponse.Album.Images.Length > 0 ? trackResponse.Album.Images[0].Url : null);
                }

                var trackItem = new TrackItem
                {
                    Id = trackResponse.Id,
                    Title = trackResponse.Name,
                    Artist = string.Join(", ", trackResponse.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                    Album = trackResponse.Album?.Name ?? "",
                    AlbumArtist = string.Join(", ", trackResponse.Album?.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                    Year = ExtractYearFromReleaseDate(trackResponse.Album?.ReleaseDate, trackResponse.Album?.ReleaseDatePrecision),
                    Duration = TimeSpan.FromMilliseconds(trackResponse.DurationMs),
                    ImageUrl = imageUrl,
                    CanDownload = true, // Will be updated by CheckAndUpdateTrackDownloadStatus
                    DownloadButtonText = "Download" // Will be updated by CheckAndUpdateTrackDownloadStatus
                };
                
                // Check download status (Spotify only supports MP3)
                CheckAndUpdateTrackDownloadStatus(trackItem);

                _allTracks.Clear();
                _tracks.Clear();
                _allTracks.Add(trackItem);
                _tracks.Add(trackItem);

                StatusText.Text = $"Loaded track: {trackItem.Title}";

                // Show filter and download all button (even for single track)
                FilterTextBox.Visibility = Visibility.Visible;
            }
            else
            {
                throw new Exception("Failed to parse track information");
            }
        }

        private async Task LoadPlaylistTracks(string playlistId)
        {
            StatusText.Text = "Loading playlist...";

            // Fetch playlist tracks (handle pagination)
            string? nextUrl = $"https://api.spotify.com/v1/playlists/{playlistId}/tracks?limit=50";
            int totalLoaded = 0;

            while (!string.IsNullOrEmpty(nextUrl))
            {
                var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                request.Headers.Add("Authorization", $"Bearer {_accessToken}");

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to load playlist: {content}");
                }

                var playlistResponse = JsonConvert.DeserializeObject<Models.PlaylistTracksResponse>(content);

                if (playlistResponse?.Items != null)
                {
                    foreach (var item in playlistResponse.Items)
                    {
                        if (item?.Track != null && item.Track.Name != null)
                        {
                            // Get album art URL (prefer medium size, fallback to first available)
                            string? imageUrl = null;
                            if (item.Track.Album?.Images != null && item.Track.Album.Images.Length > 0)
                            {
                                // Prefer medium size (around 300x300), or largest available
                                var mediumImage = item.Track.Album.Images.FirstOrDefault(img => img.Height >= 200 && img.Height <= 400);
                                imageUrl = mediumImage?.Url ?? (item.Track.Album.Images.Length > 0 ? item.Track.Album.Images[0].Url : null);
                            }

                            var trackItem = new TrackItem
                            {
                                Id = item.Track.Id,
                                Title = item.Track.Name,
                                Artist = string.Join(", ", item.Track.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                                Album = item.Track.Album?.Name ?? "",
                                AlbumArtist = string.Join(", ", item.Track.Album?.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                                Year = ExtractYearFromReleaseDate(item.Track.Album?.ReleaseDate, item.Track.Album?.ReleaseDatePrecision),
                                Duration = TimeSpan.FromMilliseconds(item.Track.DurationMs),
                                ImageUrl = imageUrl,
                                CanDownload = true, // Will be updated by CheckAndUpdateTrackDownloadStatus
                                DownloadButtonText = "Download" // Will be updated by CheckAndUpdateTrackDownloadStatus
                            };
                            
                            // Check download status (Spotify only supports MP3)
                            CheckAndUpdateTrackDownloadStatus(trackItem);
                            _allTracks.Add(trackItem);
                            _tracks.Add(trackItem);
                            totalLoaded++;
                        }
                    }
                }

                // Check for next page
                nextUrl = playlistResponse?.Next;
                if (!string.IsNullOrEmpty(nextUrl))
                {
                    StatusText.Text = $"Loading playlist... ({totalLoaded} tracks loaded)";
                }
            }

            StatusText.Text = $"Loaded {_tracks.Count} tracks from playlist";

            // Show filter and download all button
            if (_tracks.Count > 0)
            {
                FilterTextBox.Visibility = Visibility.Visible;
                DownloadAllButton.Visibility = Visibility.Visible;
            }
        }

        private async Task SearchTracks(string searchQuery)
        {
            StatusText.Text = "Searching Spotify...";

            // Build the search URL with proper query string encoding using UriBuilder
            // Increase limit to 50 to show more similar songs
            var uriBuilder = new UriBuilder("https://api.spotify.com/v1/search");
            uriBuilder.Query = $"q={Uri.EscapeDataString(searchQuery)}&type=track&limit=50";

            var searchRequest = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
            searchRequest.Headers.Add("Authorization", $"Bearer {_accessToken}");

            var searchResponse = await _httpClient.SendAsync(searchRequest);
            var searchContent = await searchResponse.Content.ReadAsStringAsync();

            if (!searchResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Search failed: {searchContent}");
            }

            var searchResult = JsonConvert.DeserializeObject<Models.SpotifySearchResponse>(searchContent);

            if (searchResult?.Tracks?.Items != null && searchResult.Tracks.Items.Any())
            {
                foreach (var track in searchResult.Tracks.Items)
                {
                    // Get album art URL (prefer medium size, fallback to first available)
                    string? imageUrl = null;
                    if (track.Album?.Images != null && track.Album.Images.Length > 0)
                    {
                        // Prefer medium size (around 300x300), or largest available
                        var mediumImage = track.Album.Images.FirstOrDefault(img => img.Height >= 200 && img.Height <= 400);
                        imageUrl = mediumImage?.Url ?? (track.Album.Images.Length > 0 ? track.Album.Images[0].Url : null);
                    }

                    var trackItem = new TrackItem
                    {
                        Id = track.Id,
                        Title = track.Name,
                        Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
                        Album = track.Album?.Name ?? "",
                        AlbumArtist = string.Join(", ", track.Album?.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                        Year = ExtractYearFromReleaseDate(track.Album?.ReleaseDate, track.Album?.ReleaseDatePrecision),
                        Duration = TimeSpan.FromMilliseconds(track.DurationMs),
                        ImageUrl = imageUrl,
                        CanDownload = true, // Will be updated by CheckAndUpdateTrackDownloadStatus
                        DownloadButtonText = "Download" // Will be updated by CheckAndUpdateTrackDownloadStatus
                    };
                    
                    // Check download status (Spotify only supports MP3)
                    CheckAndUpdateTrackDownloadStatus(trackItem);
                    _allTracks.Add(trackItem);
                    _tracks.Add(trackItem);
                }
                StatusText.Text = $"Found {_tracks.Count} similar tracks";

                // Show filter only (no download all for search results)
                if (_tracks.Count > 0)
                {
                    FilterTextBox.Visibility = Visibility.Visible;
                    DownloadAllButton.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                StatusText.Text = "No results found. Try different search terms.";
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var track = button?.Tag as TrackItem;

            if (track == null) return;
            
            // Prevent individual downloads when Download All is running
            if (_isDownloadingAll)
            {
                StatusText.Text = "Please wait for Download All to complete or stop it first.";
                return;
            }

            // If already downloading, stop it
            if (track.IsDownloading && _downloadCancellationTokenSource != null)
            {
                _downloadCancellationTokenSource.Cancel();

                // Kill all active processes for this track
                lock (_processLock)
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

                track.IsDownloading = false;
                track.CanDownload = true;
                track.DownloadButtonText = "Download";
                track.ShowProgress = false;
                StatusText.Text = "Download cancelled successfully.";

                _downloadCancellationTokenSource?.Dispose();
                _downloadCancellationTokenSource = null;
                _progressTimer?.Stop();
                _progressTimer = null;
                _currentDownloadingTrack = null;
                return;
            }

            // Use saved download path - determine extension based on source and format
            string extension = ".mp3";
            if (_selectedSource == "YouTube" && _selectedFormat == "MP4")
            {
                extension = ".mp4";
            }
            string outputPath = Path.Combine(_downloadPath, $"{SanitizeFileName(track.Title)} - {SanitizeFileName(track.Artist)}{extension}");

            // Ensure directory exists
            if (!Directory.Exists(_downloadPath))
            {
                Directory.CreateDirectory(_downloadPath);
            }

            // Check if file already exists
            if (File.Exists(outputPath))
            {
                track.CanDownload = false;
                track.DownloadButtonText = "Already Downloaded ✓";
                StatusText.Text = $"File already exists: {track.Title}";
                return;
            }

            try
            {
                // Initialize cancellation token source for this download first
                _downloadCancellationTokenSource = new CancellationTokenSource();

                track.CanDownload = false;
                track.IsDownloading = true;
                track.DownloadButtonText = "⏹️ Stop";
                _currentDownloadingTrack = track;
                _displayedProgressPercent = 0;

                // Start progressive percentage timer
                _progressTimer = new System.Windows.Threading.DispatcherTimer();
                _progressTimer.Interval = TimeSpan.FromMilliseconds(50); // Update every 50ms for smoother animation
                _progressTimer.Tick += (s, e) =>
                {
                    if (_currentDownloadingTrack != null)
                    {
                        // Get actual progress from track
                        double actualProgress = _currentDownloadingTrack.DownloadProgress;
                        int actualPercent = (int)Math.Round(actualProgress);

                        // Smoothly catch up to actual progress
                        if (_displayedProgressPercent < actualPercent)
                        {
                            // Calculate how far behind we are
                            int gap = actualPercent - _displayedProgressPercent;

                            // If we're far behind, catch up faster (increment by 2-5% depending on gap)
                            // If we're close, increment by 1% for smoothness
                            int increment = gap > 10 ? 5 : (gap > 5 ? 3 : (gap > 2 ? 2 : 1));

                            _displayedProgressPercent = Math.Min(_displayedProgressPercent + increment, actualPercent);
                            StatusText.Text = $"Downloading: {_currentDownloadingTrack.Title} - {_currentDownloadingTrack.Artist} ({_displayedProgressPercent}%)";
                        }
                        else if (_displayedProgressPercent > actualPercent)
                        {
                            // If we somehow got ahead, set to actual (shouldn't happen, but safety check)
                            _displayedProgressPercent = actualPercent;
                            StatusText.Text = $"Downloading: {_currentDownloadingTrack.Title} - {_currentDownloadingTrack.Artist} ({_displayedProgressPercent}%)";
                        }
                        else if (_displayedProgressPercent >= 100)
                        {
                            _progressTimer?.Stop();
                        }
                    }
                };
                _progressTimer.Start();

                StatusText.Text = $"Downloading: {track.Title} - {track.Artist} (0%)";

                await DownloadAndConvertTrack(track, outputPath, _downloadCancellationTokenSource.Token);

                // Stop progress timer
                _progressTimer?.Stop();
                _progressTimer = null;
                _currentDownloadingTrack = null;
                _displayedProgressPercent = 0;

                // Reset download state
                track.IsDownloading = false;
                track.ShowProgress = false; // Hide progress bar
                track.DownloadProgress = 0; // Reset progress
                _downloadCancellationTokenSource?.Dispose();
                _downloadCancellationTokenSource = null;

                track.DownloadButtonText = "Downloaded ✓";
                track.CanDownload = false;
                StatusText.Text = $"Successfully downloaded: {track.Title}";
            }
            catch (Exception ex)
            {
                // Stop progress timer
                _progressTimer?.Stop();
                _progressTimer = null;
                _currentDownloadingTrack = null;
                _displayedProgressPercent = 0;

                // Reset download state
                track.IsDownloading = false;
                _downloadCancellationTokenSource?.Dispose();
                _downloadCancellationTokenSource = null;

                // Check if it was a cancellation (not an error)
                if (ex is OperationCanceledException || ex is TaskCanceledException)
                {
                    track.CanDownload = true;
                    track.DownloadButtonText = "Download";
                    track.ShowProgress = false;
                    track.DownloadProgress = 0;
                    StatusText.Text = "Download cancelled successfully.";
                }
                else
                {
                    track.CanDownload = true;
                    track.DownloadButtonText = "Download";
                    track.ShowProgress = false;
                    track.DownloadProgress = 0;
                    StatusText.Text = $"Download failed: {ex.Message}";

                    // Show custom error dialog only for actual errors, not cancellations
                    // For MP4 downloads, don't show error dialog on cancellation (handled above)
                    bool isMP4Cancel = _selectedSource == "YouTube" && _selectedFormat == "MP4" && 
                                      (ex.Message.Contains("cancelled") || ex.Message.Contains("canceled"));
                    
                    if (!isMP4Cancel)
                    {
                        var errorDialog = new Views.ErrorDialog("Download Error", $"Failed to download {track.Title}:\n{ex.Message}")
                        {
                            Owner = this
                        };
                        errorDialog.ShowDialog();
                    }
                }
            }
        }


        private async Task DownloadAndConvertTrack(TrackItem track, string outputPath, CancellationToken cancellationToken = default)
        {
            await Task.Run(async () =>
            {
                // Determine search query based on source
                string searchQuery;
                if (_selectedSource == "YouTube" && !string.IsNullOrEmpty(track.Id) && track.Id.Length == 11)
                {
                    // If it's a YouTube video ID, use it directly
                    searchQuery = $"https://www.youtube.com/watch?v={track.Id}";
                }
                else if (_selectedSource == "YouTube" && track.Album == "YouTube" && track.Artist == "YouTube Search")
                {
                    // If it's a YouTube search query, use the title
                    searchQuery = track.Title;
                }
                else
                {
                    // For Spotify or general search, use title and artist
                    searchQuery = $"{track.Title} {track.Artist}";
                }

                // Use yt-dlp to download audio
                string? ytDlpPath = FindYtDlp();
                if (string.IsNullOrEmpty(ytDlpPath))
                {
                    string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                    string projectDir = Directory.GetParent(currentDir)?.Parent?.Parent?.FullName ?? currentDir;
                    throw new Exception($"yt-dlp not found.\n\nPlease place yt-dlp.exe in one of these locations:\n1. {currentDir}\n2. {projectDir}\n\nOr add it to your system PATH.");
                }

                // Find ffmpeg
                string? ffmpegPath = FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                    throw new Exception($"FFmpeg not found.\n\nPlease place ffmpeg.exe in: {currentDir}\n\nOr add it to your system PATH.");
                }

                // Ensure output directory exists
                string? outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // For YouTube videos, fetch full metadata before downloading
                // Skip metadata extraction during Download All to reduce delay
                if (_selectedSource == "YouTube" && !string.IsNullOrEmpty(track.Id) && track.Id.Length == 11 && !_isDownloadingAll)
                {
                    try
                    {
                        // Fetch full metadata for this video
                        // Using android client to avoid JS runtime requirements
                        string infoArgs = $"--dump-json --no-playlist --no-warnings --quiet --extractor-args \"youtube:player_client=android\" \"https://www.youtube.com/watch?v={track.Id}\"";
                        var infoProcessInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = ytDlpPath,
                            Arguments = infoArgs,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        using (var infoProcess = System.Diagnostics.Process.Start(infoProcessInfo))
                        {
                            if (infoProcess != null)
                            {
                                var infoOutput = new System.Text.StringBuilder();
                                infoProcess.OutputDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                        infoOutput.AppendLine(e.Data);
                                };
                                infoProcess.BeginOutputReadLine();
                                infoProcess.WaitForExit(2000); // 2 second timeout (reduced for faster downloads)

                                if (infoProcess.ExitCode == 0)
                                {
                                    string jsonOutput = infoOutput.ToString();
                                    if (!string.IsNullOrWhiteSpace(jsonOutput))
                                    {
                                        try
                                        {
                                            var videoInfo = JsonConvert.DeserializeObject<dynamic>(jsonOutput);
                                            string channel = videoInfo?.channel?.ToString() ?? track.AlbumArtist ?? "";

                                            // Extract year
                                            string year = "";
                                            try
                                            {
                                                string? uploadDate = videoInfo?.upload_date?.ToString();
                                                string? releaseDate = videoInfo?.release_date?.ToString();
                                                string dateStr = releaseDate ?? uploadDate ?? "";
                                                if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 4)
                                                    year = dateStr.Substring(0, 4);
                                            }
                                            catch { }

                                            // Extract genre
                                            string genre = "";
                                            try
                                            {
                                                var tags = videoInfo?.tags;
                                                if (tags != null)
                                                {
                                                    var tagsArray = tags as Newtonsoft.Json.Linq.JArray;
                                                    if (tagsArray != null && tagsArray.Count > 0)
                                                    {
                                                        string firstTag = tagsArray[0]?.ToString() ?? "";
                                                        if (!string.IsNullOrEmpty(firstTag))
                                                        {
                                                            string[] commonGenres = { "Pop", "Rock", "Hip Hop", "Rap", "Country", "Jazz", "Classical", "Electronic", "R&B", "Blues", "Reggae", "Metal", "Punk", "Folk", "Indie", "Alternative" };
                                                            foreach (var commonGenre in commonGenres)
                                                            {
                                                                if (firstTag.Contains(commonGenre, StringComparison.OrdinalIgnoreCase))
                                                                {
                                                                    genre = commonGenre;
                                                                    break;
                                                                }
                                                            }
                                                            if (string.IsNullOrEmpty(genre))
                                                                genre = firstTag;
                                                        }
                                                    }
                                                }
                                            }
                                            catch { }

                                            // Extract album
                                            string album = track.Album ?? "";
                                            try
                                            {
                                                string? albumField = videoInfo?.album?.ToString();
                                                if (!string.IsNullOrWhiteSpace(albumField))
                                                {
                                                    album = albumField;
                                                }
                                                else
                                                {
                                                    string? description = videoInfo?.description?.ToString() ?? "";
                                                    if (!string.IsNullOrEmpty(description))
                                                    {
                                                        var albumMatch = System.Text.RegularExpressions.Regex.Match(description, @"(?:Album|ALBUM|from)\s*:?\s*([^\n\r]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                                        if (albumMatch.Success)
                                                            album = albumMatch.Groups[1].Value.Trim();
                                                    }
                                                    if (string.IsNullOrEmpty(album) && !string.IsNullOrEmpty(channel))
                                                        album = channel;
                                                }
                                            }
                                            catch { }

                                            // Update track metadata
                                            if (!string.IsNullOrEmpty(year)) track.Year = year;
                                            if (!string.IsNullOrEmpty(genre)) track.Genre = genre;
                                            if (!string.IsNullOrEmpty(album)) track.Album = album;
                                            if (!string.IsNullOrEmpty(channel)) track.AlbumArtist = channel;
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Step 1: Download based on format
                string tempDir = outputDir ?? Path.GetTempPath();
                string tempGuid = Guid.NewGuid().ToString();
                string tempPathPattern = Path.Combine(tempDir, $"temp_{tempGuid}");

                // Determine if searchQuery is a direct URL or needs ytsearch
                string downloadUrl;
                if (searchQuery.StartsWith("http://") || searchQuery.StartsWith("https://"))
                {
                    // Direct URL - use as is
                    downloadUrl = searchQuery;
                }
                else
                {
                    // Search query - use ytsearch
                    downloadUrl = $"ytsearch:{searchQuery}";
                }

                // Determine format and download arguments
                bool isMP4 = _selectedSource == "YouTube" && _selectedFormat == "MP4";
                string downloadArgs;
                if (isMP4)
                {
                         // Use format selector that avoids problematic formats
                    downloadArgs = $"-f \"bestvideo[height<=1080]+bestaudio/best[height<=1080]/bestvideo+bestaudio/best\" --no-playlist --concurrent-fragments 8 --progress --newline --default-search \"ytsearch\" --output \"{tempPathPattern}.%(ext)s\" \"{downloadUrl}\"";
                }
                else
                {
                    // For MP3: Download best audio format (will convert later)
                    // Don't use android client for MP3 as it requires GVS PO Token
                    downloadArgs = $"-x -f bestaudio --no-playlist --concurrent-fragments 4 --progress --newline --default-search \"ytsearch\" --output \"{tempPathPattern}.%(ext)s\" \"{downloadUrl}\"";
                }

                // Show progress bar
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    track.ShowProgress = true;
                    track.DownloadProgress = 0;
                });

                // Step 1: Download with yt-dlp
                var downloadProcessInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = downloadArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = tempDir
                };

                System.Diagnostics.Process? downloadProcess = System.Diagnostics.Process.Start(downloadProcessInfo);
                if (downloadProcess == null)
                {
                    throw new Exception("Failed to start yt-dlp process. Please check if yt-dlp.exe is accessible.");
                }

                // Register process for cancellation
                lock (_activeProcesses ?? new List<System.Diagnostics.Process>())
                {
                    _activeProcesses?.Add(downloadProcess);
                }

                try
                {
                    // Read output and error streams asynchronously
                    var outputBuilder = new System.Text.StringBuilder();
                    var errorBuilder = new System.Text.StringBuilder();

                    downloadProcess.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);

                            // Parse progress from yt-dlp output
                            // Format: [download]  45.2% of 5.23MiB at 1.23MiB/s ETA 00:03
                            var progressMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"\[download\]\s+(\d+\.?\d*)%");
                            if (progressMatch.Success && double.TryParse(progressMatch.Groups[1].Value, out double progress))
                            {
                                // Update progress - for MP4, download is 100% of process; for MP3, it's 50%
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    bool isMP4Local = _selectedSource == "YouTube" && _selectedFormat == "MP4";
                                    double totalProgress = isMP4Local ? progress : (progress * 0.5); // MP4: 100%, MP3: 50% (conversion is other 50%)
                                    track.DownloadProgress = totalProgress;
                                    // Don't update StatusText here - let the timer handle progressive display
                                }, System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                    };

                    downloadProcess.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorBuilder.AppendLine(e.Data);

                            // Also check error stream for progress (yt-dlp sometimes outputs to stderr)
                            var progressMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"\[download\]\s+(\d+\.?\d*)%");
                            if (progressMatch.Success && double.TryParse(progressMatch.Groups[1].Value, out double progress))
                            {
                                // Update progress - for MP4, download is 100% of process; for MP3, it's 50%
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    bool isMP4Local = _selectedSource == "YouTube" && _selectedFormat == "MP4";
                                    track.DownloadProgress = isMP4Local ? progress : (progress * 0.5); // MP4: 100%, MP3: 50% (conversion is other 50%)
                                }, System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                    };

                    downloadProcess.BeginOutputReadLine();
                    downloadProcess.BeginErrorReadLine();

                    // Wait for process with cancellation support
                    while (!downloadProcess.HasExited && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(100, cancellationToken);
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { downloadProcess.Kill(); } catch { }
                        throw new OperationCanceledException();
                    }

                    downloadProcess.WaitForExit();
                    System.Threading.Thread.Sleep(200);

                    if (downloadProcess.ExitCode != 0)
                    {
                        string error = errorBuilder.ToString();
                        if (string.IsNullOrWhiteSpace(error))
                            error = outputBuilder.ToString();
                        if (string.IsNullOrWhiteSpace(error))
                            error = $"Process exited with code {downloadProcess.ExitCode}";
                        throw new Exception($"yt-dlp download error (exit code {downloadProcess.ExitCode}):\n{error}");
                    }
                }
                finally
                {
                    lock (_activeProcesses ?? new List<System.Diagnostics.Process>())
                    {
                        _activeProcesses?.Remove(downloadProcess);
                    }
                    downloadProcess?.Dispose();
                }

                // Find the downloaded temp file
                string? downloadedFile = null;
                var tempFiles = Directory.GetFiles(tempDir, $"temp_{tempGuid}.*");
                if (tempFiles.Length > 0)
                {
                    downloadedFile = tempFiles[0];
                }
                else
                {
                    throw new Exception("Downloaded file not found. The download may have failed.");
                }

                // Check if we're downloading MP4 (no conversion needed)
                if (isMP4)
                {
                    // For MP4: Just move/rename the file to the output path
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        track.DownloadProgress = 100;
                    }, System.Windows.Threading.DispatcherPriority.Background);

                    // Add metadata to MP4 file using FFmpeg
                    // Build metadata arguments - only include if not "Unknown" or empty
                    var mp4MetadataArgs = new System.Text.StringBuilder();

                    // Helper function to escape and add metadata for MP4
                    void AddMP4Metadata(string key, string value)
                    {
                        if (!string.IsNullOrWhiteSpace(value) &&
                            !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                            !value.Equals("Unknown Title", StringComparison.OrdinalIgnoreCase) &&
                            !value.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) &&
                            !value.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase) &&
                            !value.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
                        {
                            string escaped = value.Replace("\"", "\\\"").Replace("$", "\\$");
                            mp4MetadataArgs.Append($" -metadata {key}=\"{escaped}\"");
                        }
                    }

                    AddMP4Metadata("title", track.Title);
                    AddMP4Metadata("artist", track.Artist);
                    AddMP4Metadata("album", track.Album);
                    AddMP4Metadata("album_artist", track.AlbumArtist);
                    AddMP4Metadata("date", track.Year);
                    AddMP4Metadata("genre", track.Genre);
                    AddMP4Metadata("track", track.TrackNumber);

                    // Use FFmpeg to copy video, re-encode audio to 256kbps (max for MP4/AAC), and add metadata
                    string tempOutputPath = Path.Combine(tempDir, $"temp_metadata_{tempGuid}.mp4");
                    string mp4FfmpegArgs = $"-i \"{downloadedFile}\" -c:v copy -c:a aac -b:a 256k{mp4MetadataArgs} -y \"{tempOutputPath}\"";

                    var metadataProcessInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = mp4FfmpegArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = tempDir
                    };

                    System.Diagnostics.Process? metadataProcess = System.Diagnostics.Process.Start(metadataProcessInfo);
                    if (metadataProcess == null)
                    {
                        throw new Exception("Failed to start FFmpeg process for metadata.");
                    }

                    lock (_processLock)
                    {
                        _activeProcesses?.Add(metadataProcess);
                    }

                    try
                    {
                        var errorBuilder = new System.Text.StringBuilder();
                        metadataProcess.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                                errorBuilder.AppendLine(e.Data);
                        };
                        metadataProcess.BeginErrorReadLine();

                        // Wait for process with cancellation support
                        while (!metadataProcess.HasExited && !cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(100, cancellationToken);
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { metadataProcess.Kill(); } catch { }
                            throw new OperationCanceledException();
                        }

                        await Task.Run(() => metadataProcess.WaitForExit(), cancellationToken);

                        if (metadataProcess.ExitCode != 0)
                        {
                            string error = errorBuilder.ToString();
                            // If metadata addition fails, just use the original file (non-critical)
                            System.Diagnostics.Debug.WriteLine($"Warning: Failed to add metadata to MP4: {error}");
                            if (File.Exists(downloadedFile))
                            {
                                File.Move(downloadedFile, outputPath, true);
                            }
                        }
                        else
                        {
                            // Move the file with metadata to the final output path
                            if (File.Exists(tempOutputPath))
                            {
                                File.Move(tempOutputPath, outputPath, true);
                            }
                            else
                            {
                                // Fallback to original file if metadata file not found
                                if (File.Exists(downloadedFile))
                                {
                                    File.Move(downloadedFile, outputPath, true);
                                }
                            }
                        }
                    }
                    finally
                    {
                        lock (_processLock ?? new List<System.Diagnostics.Process>())
                        {
                            _activeProcesses?.Remove(metadataProcess);
                        }
                        metadataProcess?.Dispose();

                        // Clean up temp files
                        try
                        {
                            if (File.Exists(downloadedFile))
                                File.Delete(downloadedFile);
                            if (File.Exists(tempOutputPath) && File.Exists(outputPath))
                                File.Delete(tempOutputPath);
                        }
                        catch { }
                    }

                    // Hide progress bar for MP4
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        track.ShowProgress = false;
                        track.DownloadProgress = 0;
                    }, System.Windows.Threading.DispatcherPriority.Background);

                    return; // Done for MP4
                }

                // Step 2: Convert to 320kbps MP3 using ffmpeg directly (only for MP3)
                // This gives us full control over the encoding
                // Update progress to show we're converting (50% done)
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    track.DownloadProgress = 50;
                    // Don't update StatusText here - let the timer handle progressive display
                }, System.Windows.Threading.DispatcherPriority.Background);

                // Build metadata arguments - only include if not "Unknown" or empty
                var metadataArgs = new System.Text.StringBuilder();

                // Helper function to escape and add metadata
                void AddMetadata(string key, string value)
                {
                    if (!string.IsNullOrWhiteSpace(value) &&
                        !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                        !value.Equals("Unknown Title", StringComparison.OrdinalIgnoreCase) &&
                        !value.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) &&
                        !value.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase) &&
                        !value.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
                    {
                        string escaped = value.Replace("\"", "\\\"").Replace("$", "\\$");
                        metadataArgs.Append($" -metadata {key}=\"{escaped}\"");
                    }
                }

                AddMetadata("title", track.Title);
                AddMetadata("artist", track.Artist);
                AddMetadata("album", track.Album);
                AddMetadata("album_artist", track.AlbumArtist);
                AddMetadata("date", track.Year);
                AddMetadata("genre", track.Genre);
                AddMetadata("track", track.TrackNumber);

                string ffmpegArgs = $"-i \"{downloadedFile}\" -codec:a libmp3lame -b:a 320k -ar 44100{metadataArgs} -y \"{outputPath}\"";

                var convertProcessInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = ffmpegArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = tempDir
                };

                System.Diagnostics.Process? convertProcess = System.Diagnostics.Process.Start(convertProcessInfo);
                if (convertProcess == null)
                {
                    throw new Exception("Failed to start ffmpeg process. Please check if ffmpeg.exe is accessible.");
                }

                // Register process for cancellation
                lock (_activeProcesses ?? new List<System.Diagnostics.Process>())
                {
                    _activeProcesses?.Add(convertProcess);
                }

                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { convertProcess.Kill(); } catch { }
                        throw new OperationCanceledException();
                    }

                    var convertErrorBuilder = new System.Text.StringBuilder();
                    long? totalDuration = null;
                    long? currentTime = null;

                    convertProcess.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            convertErrorBuilder.AppendLine(e.Data);

                            // Parse ffmpeg progress: Duration: 00:03:45.23, start: 0.000000, bitrate: 320 kb/s
                            // time=00:01:23.45 bitrate= 320.0kbits/s
                            var durationMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"Duration:\s+(\d+):(\d+):(\d+\.\d+)");
                            if (durationMatch.Success)
                            {
                                int hours = int.Parse(durationMatch.Groups[1].Value);
                                int minutes = int.Parse(durationMatch.Groups[2].Value);
                                double seconds = double.Parse(durationMatch.Groups[3].Value);
                                totalDuration = (long)(hours * 3600 + minutes * 60 + seconds) * 1000;
                            }

                            var timeMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"time=(\d+):(\d+):(\d+\.\d+)");
                            if (timeMatch.Success && totalDuration.HasValue)
                            {
                                int hours = int.Parse(timeMatch.Groups[1].Value);
                                int minutes = int.Parse(timeMatch.Groups[2].Value);
                                double seconds = double.Parse(timeMatch.Groups[3].Value);
                                currentTime = (long)(hours * 3600 + minutes * 60 + seconds) * 1000;

                                double conversionProgress = (currentTime.Value / (double)totalDuration.Value) * 50; // Conversion is 50% of total

                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    double totalProgress = 50 + conversionProgress; // 50% (download) + conversion progress
                                    track.DownloadProgress = totalProgress;
                                    // Don't update StatusText here - let the timer handle progressive display
                                }, System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                    };

                    convertProcess.BeginErrorReadLine();

                    // Wait for process with cancellation support
                    while (!convertProcess.HasExited && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(100, cancellationToken);
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { convertProcess.Kill(); } catch { }
                        throw new OperationCanceledException();
                    }

                    convertProcess.WaitForExit();
                    System.Threading.Thread.Sleep(200);

                    if (convertProcess.ExitCode != 0)
                    {
                        string error = convertErrorBuilder.ToString();
                        if (string.IsNullOrWhiteSpace(error))
                            error = $"Process exited with code {convertProcess.ExitCode}";
                        throw new Exception($"FFmpeg conversion error (exit code {convertProcess.ExitCode}):\n{error}");
                    }
                }
                finally
                {
                    lock (_activeProcesses ?? new List<System.Diagnostics.Process>())
                    {
                        _activeProcesses?.Remove(convertProcess);
                    }
                    convertProcess?.Dispose();
                }

                // Clean up temp file
                try
                {
                    if (File.Exists(downloadedFile))
                    {
                        File.Delete(downloadedFile);
                    }
                }
                catch { /* Ignore cleanup errors */ }

                // Hide progress bar
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    track.ShowProgress = false;
                    track.DownloadProgress = 0;
                });

                // Verify output file exists
                if (!File.Exists(outputPath))
                {
                    throw new Exception("Converted MP3 file not found. The conversion may have failed.");
                }
            });
        }

        private string? FindFfmpeg()
        {
            // Check current directory (where app is running from)
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possibleNames = { "ffmpeg.exe", "ffmpeg" };

            foreach (var name in possibleNames)
            {
                string path = Path.Combine(currentDir, name);
                if (File.Exists(path))
                    return path;
            }

            // Check bin folder - try multiple approaches
            try
            {
                // Approach 1: 2 levels up (bin/Debug/net8.0-windows -> bin)
                string? binDir1 = Directory.GetParent(currentDir)?.Parent?.FullName;
                if (!string.IsNullOrEmpty(binDir1))
                {
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(binDir1, name);
                        if (File.Exists(path))
                            return path;
                    }
                }

                // Approach 2: Look for a folder named "bin" in parent directories
                DirectoryInfo? current = new DirectoryInfo(currentDir);
                for (int i = 0; i < 5 && current != null; i++)
                {
                    current = current.Parent;
                    if (current != null && current.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var name in possibleNames)
                        {
                            string path = Path.Combine(current.FullName, name);
                            if (File.Exists(path))
                                return path;
                        }
                        break;
                    }
                }
            }
            catch { }

            // Check project root directory (3 levels up from bin/Debug/net8.0-windows)
            try
            {
                string? projectDir = Directory.GetParent(currentDir)?.Parent?.Parent?.FullName;
                if (!string.IsNullOrEmpty(projectDir))
                {
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(projectDir, name);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }
            catch { }

            // Check PATH
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(dir, name);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }

            return null;
        }

        private string? FindYtDlp()
        {
            // Check current directory (where app is running from)
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possibleNames = { "yt-dlp.exe", "yt-dlp" };

            foreach (var name in possibleNames)
            {
                string path = Path.Combine(currentDir, name);
                if (File.Exists(path))
                    return path;
            }

            // Check bin folder - try multiple approaches
            try
            {
                // Approach 1: 2 levels up (bin/Debug/net8.0-windows -> bin)
                string? binDir1 = Directory.GetParent(currentDir)?.Parent?.FullName;
                if (!string.IsNullOrEmpty(binDir1))
                {
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(binDir1, name);
                        if (File.Exists(path))
                            return path;
                    }
                }

                // Approach 2: Look for a folder named "bin" in parent directories
                DirectoryInfo? current = new DirectoryInfo(currentDir);
                for (int i = 0; i < 5 && current != null; i++)
                {
                    current = current.Parent;
                    if (current != null && current.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var name in possibleNames)
                        {
                            string path = Path.Combine(current.FullName, name);
                            if (File.Exists(path))
                                return path;
                        }
                        break;
                    }
                }
            }
            catch { }

            // Check project root directory (3 levels up from bin/Debug/net8.0-windows)
            try
            {
                string? projectDir = Directory.GetParent(currentDir)?.Parent?.Parent?.FullName;
                if (!string.IsNullOrEmpty(projectDir))
                {
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(projectDir, name);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }
            catch { }

            // Check PATH
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(dir, name);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }

            return null;
        }

        private string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        private string ExtractYearFromReleaseDate(string? releaseDate, string? precision)
        {
            if (string.IsNullOrWhiteSpace(releaseDate))
                return "";

            // Spotify release_date can be in formats: "2023", "2023-06", "2023-06-15"
            // precision can be "year", "month", "day"
            if (precision == "year" || releaseDate.Length == 4)
            {
                return releaseDate.Substring(0, 4);
            }
            else if (releaseDate.Length >= 4)
            {
                return releaseDate.Substring(0, 4);
            }
            return "";
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Load current settings to pass to settings window
            var currentSettings = Models.AppSettings.Load();
            var settingsWindow = new Views.SettingsWindow(_downloadPath, currentSettings.SpotifyClientId, currentSettings.SpotifyClientSecret)
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

                // Save settings
                var settings = new Models.AppSettings
                {
                    DownloadPath = _downloadPath,
                    SpotifyClientId = _clientId,
                    SpotifyClientSecret = _clientSecret
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
                var stopDialog = new Views.StopDialog("Are you sure you want to stop all downloads? Progress will be lost for tracks that haven't finished.")
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

        private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
        {
            // If already downloading, stop it
            if (_isDownloadingAll && _downloadAllCancellationTokenSource != null)
            {
                _downloadAllCancellationTokenSource.Cancel();

                // Kill all active processes
                lock (_processLock)
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

                // Also kill any yt-dlp or ffmpeg processes
                try
                {
                    foreach (var proc in System.Diagnostics.Process.GetProcessesByName("yt-dlp"))
                    {
                        try { proc.Kill(); } catch { }
                    }
                    foreach (var proc in System.Diagnostics.Process.GetProcessesByName("ffmpeg"))
                    {
                        try { proc.Kill(); } catch { }
                    }
                }
                catch { }

                _isDownloadingAll = false;
                DownloadAllButton.Content = "Download All";
                DownloadAllButton.Style = (System.Windows.Style)FindResource("PrimaryButtonStyle");
                StatusText.Text = "Download cancelled successfully.";

                // Reset all queued/downloading tracks (but keep already downloaded ones)
                foreach (var track in _tracks)
                {
                    if (track.DownloadButtonText == "Downloaded ✓" || track.DownloadButtonText == "Already Downloaded ✓")
                    {
                        // Keep as downloaded
                        continue;
                    }
                    else
                    {
                        // Reset to Download
                        track.IsDownloading = false;
                        track.CanDownload = true;
                        track.DownloadButtonText = "Download";
                    }
                }

                _downloadAllCancellationTokenSource?.Dispose();
                _downloadAllCancellationTokenSource = null;
                return;
            }

            // Start downloading all tracks
            if (!_isSpotifyPlaylist || _tracks.Count == 0)
            {
                return;
            }

            try
            {
                _downloadAllCancellationTokenSource = new CancellationTokenSource();
                _isDownloadingAll = true;
                DownloadAllButton.Content = "⏹️ Stop";
                DownloadAllButton.Style = (System.Windows.Style)FindResource("StopButtonStyle");

                var tracksToDownload = _tracks?.Where(t => t != null && t.CanDownload).ToList() ?? new List<TrackItem>();
                int totalTracks = tracksToDownload.Count;
                int downloadedCount = 0;

                if (totalTracks == 0)
                {
                    StatusText.Text = "No tracks to download.";
                    _isDownloadingAll = false;
                    DownloadAllButton.Content = "Download All";
                    DownloadAllButton.Style = (System.Windows.Style)FindResource("PrimaryButtonStyle");
                    _downloadAllCancellationTokenSource?.Dispose();
                    _downloadAllCancellationTokenSource = null;
                    return;
                }

                // Disable all individual download buttons during Download All
                foreach (var track in tracksToDownload)
                {
                    if (track != null)
                    {
                        track.CanDownload = false;
                        track.DownloadButtonText = "Queued...";
                        track.IsDownloading = false;
                    }
                }
                
                // Set up progress tracking for Download All
                _currentDownloadingTrack = null;
                _displayedProgressPercent = 0;
                
                // Start progress timer for Download All
                _progressTimer = new System.Windows.Threading.DispatcherTimer();
                _progressTimer.Interval = TimeSpan.FromMilliseconds(50);
                _progressTimer.Tick += (s, e) =>
                {
                    if (_currentDownloadingTrack != null && _isDownloadingAll)
                    {
                        double actualProgress = _currentDownloadingTrack.DownloadProgress;
                        int actualPercent = (int)Math.Round(actualProgress);
                        
                        if (_displayedProgressPercent < actualPercent)
                        {
                            int gap = actualPercent - _displayedProgressPercent;
                            _displayedProgressPercent += Math.Min(gap, Math.Max(1, gap / 4));
                            int currentTrackNum = downloadedCount + 1;
                            StatusText.Text = $"Downloading {currentTrackNum}/{totalTracks}: {_currentDownloadingTrack.Title} ({_displayedProgressPercent}%)";
                        }
                        else if (_displayedProgressPercent >= 100)
                        {
                            _progressTimer?.Stop();
                        }
                    }
                };
                _progressTimer.Start();

                foreach (var track in tracksToDownload)
                {
                    if (_downloadAllCancellationTokenSource == null || _downloadAllCancellationTokenSource.Token.IsCancellationRequested)
                    {
                        StatusText.Text = "Download cancelled successfully.";
                        break;
                    }

                    try
                    {
                        // Mark track as actively downloading (but don't show stop button for Download All)
                        track.IsDownloading = false; // Keep false so no stop button appears
                        track.DownloadButtonText = "Downloading...";
                        _currentDownloadingTrack = track; // Set for progress tracking
                        _displayedProgressPercent = 0;

                        string fileNameTitle = string.IsNullOrWhiteSpace(track.Title) ? "Unknown Title" : track.Title;
                        string fileNameArtist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown Artist" : track.Artist;
                        // Use the currently selected format (MP3 or MP4 for YouTube)
                        string extension = ".mp3";
                        if (_selectedSource == "YouTube" && _selectedFormat == "MP4")
                        {
                            extension = ".mp4";
                        }
                        string outputPath = Path.Combine(_downloadPath, $"{SanitizeFileName(fileNameTitle)} - {SanitizeFileName(fileNameArtist)}{extension}");

                        if (_downloadAllCancellationTokenSource == null || _downloadAllCancellationTokenSource.Token.IsCancellationRequested)
                        {
                            StatusText.Text = "Download cancelled successfully.";
                            break;
                        }

                        StatusText.Text = $"Downloading {downloadedCount + 1}/{totalTracks}: {track.Title} (0%)";

                        await DownloadAndConvertTrack(track, outputPath, _downloadAllCancellationTokenSource.Token);

                        downloadedCount++;
                        track.IsDownloading = false;
                        track.CanDownload = false;
                        track.DownloadButtonText = "Downloaded ✓";
                        track.ShowProgress = false;
                        track.DownloadProgress = 0;
                        _currentDownloadingTrack = null;
                        _displayedProgressPercent = 0;
                        StatusText.Text = $"Downloaded {downloadedCount}/{totalTracks} tracks...";
                    }
                    catch (Exception ex)
                    {
                        // Check if it was a cancellation or if cancellation was requested
                        bool isCancelled = ex is OperationCanceledException || ex is TaskCanceledException ||
                                          (_downloadAllCancellationTokenSource != null && _downloadAllCancellationTokenSource.Token.IsCancellationRequested);

                        if (isCancelled)
                        {
                            // Reset track state on cancellation
                            if (track != null)
                            {
                                track.IsDownloading = false;
                                track.CanDownload = true;
                                track.DownloadButtonText = "Download";
                            }
                            // Break out of loop on cancellation
                            StatusText.Text = "Download cancelled successfully.";
                            break;
                        }
                        else
                        {
                            // Reset track state on error
                            if (track != null)
                            {
                                track.IsDownloading = false;
                                track.CanDownload = true;
                                track.DownloadButtonText = "Download";
                                System.Diagnostics.Debug.WriteLine($"Failed to download {track.Title}: {ex.Message}");
                            }
                        }
                        // Continue with next track
                    }
                }

                // Only show success message if not cancelled
                if (_downloadAllCancellationTokenSource != null && !_downloadAllCancellationTokenSource.Token.IsCancellationRequested)
                {
                    StatusText.Text = $"Successfully downloaded {downloadedCount}/{totalTracks} tracks";
                }
                else
                {
                    StatusText.Text = "Download cancelled successfully.";
                }
            }
            catch (Exception ex)
            {
                // Check if it was a cancellation (not an error)
                bool isCancelled = ex is OperationCanceledException || ex is TaskCanceledException;

                // Also check if cancellation was requested (even if we got a NullReferenceException)
                if (ex is NullReferenceException)
                {
                    try
                    {
                        if (_downloadAllCancellationTokenSource == null || _downloadAllCancellationTokenSource.Token.IsCancellationRequested)
                        {
                            isCancelled = true;
                        }
                    }
                    catch
                    {
                        // If we can't check, assume it's cancelled if the source is null
                        if (_downloadAllCancellationTokenSource == null)
                        {
                            isCancelled = true;
                        }
                    }
                }

                if (isCancelled)
                {
                    StatusText.Text = "Download cancelled successfully.";
                }
                else
                {
                    StatusText.Text = $"Download failed: {ex.Message}";

                    // Show custom error dialog only for actual errors, not cancellations
                    var errorDialog = new Views.ErrorDialog("Download Error", $"Failed to download all tracks:\n{ex.Message}")
                    {
                        Owner = this
                    };
                    errorDialog.ShowDialog();
                }
            }
            finally
            {
                _isDownloadingAll = false;
                _progressTimer?.Stop();
                _progressTimer = null;
                _currentDownloadingTrack = null;
                _displayedProgressPercent = 0;
                
                DownloadAllButton.Content = "Download All";
                DownloadAllButton.Style = (System.Windows.Style)FindResource("PrimaryButtonStyle");

                // Reset any remaining queued/downloading tracks that didn't complete (but keep downloaded ones)
                foreach (var track in _tracks)
                {
                    if (track.DownloadButtonText == "Downloaded ✓" || track.DownloadButtonText == "Already Downloaded ✓")
                    {
                        // Keep as downloaded
                        continue;
                    }
                    else if (track.DownloadButtonText == "Queued..." || track.DownloadButtonText == "Downloading..." || track.IsDownloading)
                    {
                        // Reset to Download
                        track.IsDownloading = false;
                        track.CanDownload = true;
                        track.DownloadButtonText = "Download";
                        track.ShowProgress = false;
                        track.DownloadProgress = 0;
                    }
                }

                _downloadAllCancellationTokenSource?.Dispose();
                _downloadAllCancellationTokenSource = null;
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(_downloadPath))
                {
                    Directory.CreateDirectory(_downloadPath);
                }

                // Open the folder in Windows Explorer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _downloadPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open folder:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

