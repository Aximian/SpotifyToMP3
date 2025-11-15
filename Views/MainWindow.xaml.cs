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
using SpotifyToMP3.Models;

namespace SpotifyToMP3.Views
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

        public MainWindow()
        {
            InitializeComponent();
            _tracks = new ObservableCollection<TrackItem>();
            _allTracks = new ObservableCollection<TrackItem>();
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
                string pngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spotify.png");
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
            if (string.IsNullOrEmpty(_accessToken))
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

                // Check if input is a Spotify playlist URL
                string? playlistId = ExtractPlaylistId(searchQuery);
                if (!string.IsNullOrEmpty(playlistId))
                {
                    // Load playlist tracks
                    await LoadPlaylistTracks(playlistId);
                }
                else
                {
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
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                System.Windows.MessageBox.Show($"Operation failed:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    imageUrl = mediumImage?.Url ?? trackResponse.Album.Images[0].Url;
                }

                string outputPath = Path.Combine(_downloadPath, $"{SanitizeFileName(trackResponse.Name)} - {SanitizeFileName(string.Join(", ", trackResponse.Artists?.Select(a => a.Name) ?? Array.Empty<string>()))}.mp3");
                bool alreadyExists = File.Exists(outputPath);

                var trackItem = new TrackItem
                {
                    Id = trackResponse.Id,
                    Title = trackResponse.Name,
                    Artist = string.Join(", ", trackResponse.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                    Album = trackResponse.Album?.Name ?? "Unknown Album",
                    Duration = TimeSpan.FromMilliseconds(trackResponse.DurationMs),
                    ImageUrl = imageUrl,
                    CanDownload = !alreadyExists,
                    DownloadButtonText = alreadyExists ? "Already Downloaded ✓" : "Download"
                };

                _allTracks.Clear();
                _tracks.Clear();
                _allTracks.Add(trackItem);
                _tracks.Add(trackItem);

                StatusText.Text = $"Loaded track: {trackItem.Title}";

                // Show filter and download all button (even for single track)
                FilterTextBox.Visibility = Visibility.Visible;
                DownloadAllButton.Visibility = Visibility.Visible;
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
                                imageUrl = mediumImage?.Url ?? item.Track.Album.Images[0].Url;
                            }

                            string outputPath = Path.Combine(_downloadPath, $"{SanitizeFileName(item.Track.Name)} - {SanitizeFileName(string.Join(", ", item.Track.Artists?.Select(a => a.Name) ?? Array.Empty<string>()))}.mp3");
                            bool alreadyExists = File.Exists(outputPath);

                            var trackItem = new TrackItem
                            {
                                Id = item.Track.Id,
                                Title = item.Track.Name,
                                Artist = string.Join(", ", item.Track.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                                Album = item.Track.Album?.Name ?? "Unknown Album",
                                Duration = TimeSpan.FromMilliseconds(item.Track.DurationMs),
                                ImageUrl = imageUrl,
                                CanDownload = !alreadyExists,
                                DownloadButtonText = alreadyExists ? "Already Downloaded ✓" : "Download"
                            };
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
                        imageUrl = mediumImage?.Url ?? track.Album.Images[0].Url;
                    }

                    string outputPath = Path.Combine(_downloadPath, $"{SanitizeFileName(track.Name)} - {SanitizeFileName(string.Join(", ", track.Artists.Select(a => a.Name)))}.mp3");
                    bool alreadyExists = File.Exists(outputPath);

                    var trackItem = new TrackItem
                    {
                        Id = track.Id,
                        Title = track.Name,
                        Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
                        Album = track.Album?.Name ?? "Unknown Album",
                        Duration = TimeSpan.FromMilliseconds(track.DurationMs),
                        ImageUrl = imageUrl,
                        CanDownload = !alreadyExists,
                        DownloadButtonText = alreadyExists ? "Already Downloaded ✓" : "Download"
                    };
                    _allTracks.Add(trackItem);
                    _tracks.Add(trackItem);
                }
                StatusText.Text = $"Found {_tracks.Count} similar tracks";

                // Show filter only (no download all for search results)
                if (_tracks.Count > 0)
                {
                    FilterTextBox.Visibility = Visibility.Visible;
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

            // Use saved download path
            string outputPath = Path.Combine(_downloadPath, $"{SanitizeFileName(track.Title)} - {SanitizeFileName(track.Artist)}.mp3");

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
                track.CanDownload = false;
                track.DownloadButtonText = "Downloading...";
                StatusText.Text = $"Downloading: {track.Title} - {track.Artist}";

                await DownloadAndConvertTrack(track, outputPath);

                track.DownloadButtonText = "Downloaded ✓";
                StatusText.Text = $"Successfully downloaded: {track.Title}";
            }
            catch (Exception ex)
            {
                track.CanDownload = true;
                track.DownloadButtonText = "Download";
                StatusText.Text = $"Download failed: {ex.Message}";
                System.Windows.MessageBox.Show($"Failed to download {track.Title}:\n{ex.Message}",
                    "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0)
            {
                System.Windows.MessageBox.Show("No tracks to download.", "No Tracks", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Use saved download path
            string downloadFolder = _downloadPath;

            // Ensure directory exists
            if (!Directory.Exists(downloadFolder))
            {
                Directory.CreateDirectory(downloadFolder);
            }

            var tracksToDownload = _tracks.Where(t => t.CanDownload).ToList();
            int totalTracks = tracksToDownload.Count;

            if (totalTracks == 0)
            {
                System.Windows.MessageBox.Show("No tracks to download.", "No Tracks", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int downloaded = 0;
            int failed = 0;
            object lockObject = new object();

            // Create cancellation token source
            _downloadCancellationTokenSource = new CancellationTokenSource();
            _activeProcesses = new List<System.Diagnostics.Process>();
            var cancellationToken = _downloadCancellationTokenSource.Token;

            DownloadAllButton.IsEnabled = false;
            DownloadAllButton.Visibility = Visibility.Collapsed;
            StopDownloadButton.Visibility = Visibility.Visible;
            StatusText.Text = $"Downloading all tracks... (0/{totalTracks})";

            // Use semaphore to limit concurrent downloads (max 5 at a time for faster downloads)
            var semaphore = new SemaphoreSlim(5, 5);
            var downloadTasks = new List<Task>();

            foreach (var track in tracksToDownload)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                track.CanDownload = false;
                track.DownloadButtonText = "Queued...";

                var task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        string outputPath = Path.Combine(downloadFolder, $"{SanitizeFileName(track.Title)} - {SanitizeFileName(track.Artist)}.mp3");

                        // Check if file already exists
                        if (File.Exists(outputPath))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                track.CanDownload = false;
                                track.DownloadButtonText = "Already Downloaded ✓";
                            });

                            lock (lockObject)
                            {
                                downloaded++;
                            }

                            Dispatcher.Invoke(() =>
                            {
                                StatusText.Text = $"Downloading all tracks... ({downloaded}/{totalTracks})";
                            });
                            return;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            track.DownloadButtonText = "Downloading...";
                        });

                        await DownloadAndConvertTrack(track, outputPath, cancellationToken);

                        if (cancellationToken.IsCancellationRequested)
                            return;

                        lock (lockObject)
                        {
                            downloaded++;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            track.DownloadButtonText = "Downloaded ✓";
                            StatusText.Text = $"Downloading all tracks... ({downloaded}/{totalTracks})";
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            track.CanDownload = true;
                            track.DownloadButtonText = "Cancelled";
                        });
                    }
                    catch (Exception)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        lock (lockObject)
                        {
                            failed++;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            track.CanDownload = true;
                            track.DownloadButtonText = "Download";
                            StatusText.Text = $"Downloading all tracks... ({downloaded}/{totalTracks}) - Failed: {track.Title}";
                        });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                downloadTasks.Add(task);
            }

            try
            {
                // Wait for all downloads to complete or cancellation
                await Task.WhenAll(downloadTasks);
            }
            catch (OperationCanceledException)
            {
                // Cancellation was requested
            }

            // Clean up
            DownloadAllButton.IsEnabled = true;
            DownloadAllButton.Visibility = Visibility.Visible;
            StopDownloadButton.Visibility = Visibility.Collapsed;

            if (cancellationToken.IsCancellationRequested)
            {
                StatusText.Text = $"Download cancelled! {downloaded} downloaded, {failed} failed";

                // Show custom completion dialog
                var completionDialog = new Views.CompletionDialog(
                    "⏹️ Downloads Cancelled",
                    $"Download cancelled!\n\nDownloaded: {downloaded}\nFailed: {failed}",
                    false)
                {
                    Owner = this
                };
                completionDialog.ShowDialog();
            }
            else
            {
                StatusText.Text = $"Download complete! {downloaded} downloaded, {failed} failed";

                // Show custom completion dialog
                var completionDialog = new Views.CompletionDialog(
                    "✅ Download Complete",
                    $"Download complete!\n\nDownloaded: {downloaded}\nFailed: {failed}",
                    true)
                {
                    Owner = this
                };
                completionDialog.ShowDialog();
            }

            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;
            _activeProcesses = null;
        }

        private async Task DownloadAndConvertTrack(TrackItem track, string outputPath, CancellationToken cancellationToken = default)
        {
            await Task.Run(async () =>
            {
                // Search query for YouTube
                string searchQuery = $"{track.Title} {track.Artist}";

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

                // Step 1: Download best audio available (without conversion)
                string tempDir = outputDir ?? Path.GetTempPath();
                string tempGuid = Guid.NewGuid().ToString();
                string tempAudioPathPattern = Path.Combine(tempDir, $"temp_{tempGuid}");

                // Download best audio format available (yt-dlp will choose best format)
                // Using --no-playlist and --concurrent-fragments 4 for faster downloads
                string downloadArgs = $"-x -f bestaudio --no-playlist --concurrent-fragments 4 --progress --newline --default-search \"ytsearch\" --output \"{tempAudioPathPattern}.%(ext)s\" \"ytsearch:{searchQuery}\"";

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
                                // Update progress more frequently (download phase is 50% of total)
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    track.DownloadProgress = progress * 0.5; // Download is 50% of total process
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
                                // Update progress more frequently (download phase is 50% of total)
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    track.DownloadProgress = progress * 0.5; // Download is 50% of total process
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
                    throw new Exception("Downloaded audio file not found. The download may have failed.");
                }

                // Step 2: Convert to 320kbps MP3 using ffmpeg directly
                // This gives us full control over the encoding
                // Update progress to show we're converting (50% done)
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    track.DownloadProgress = 50;
                }, System.Windows.Threading.DispatcherPriority.Background);

                string ffmpegArgs = $"-i \"{downloadedFile}\" -codec:a libmp3lame -b:a 320k -ar 44100 -y \"{outputPath}\"";

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
                                    track.DownloadProgress = 50 + conversionProgress; // 50% (download) + conversion progress
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

