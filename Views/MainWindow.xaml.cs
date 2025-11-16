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
using MediaConverterToMP3.Views.MainWindowOperations.Utilities;

namespace MediaConverterToMP3.Views
{
    public partial class MainWindow : Window
    {
        private HttpClient _httpClient;
        private string? _accessToken;
        private ObservableCollection<TrackItem> _tracks;
        private ObservableCollection<TrackItem> _allTracks; // Store all tracks for filtering
        private string _downloadPath;
        private string? _spotifyLocalFilesPath;
        private string _clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? "YOUR_CLIENT_ID_HERE";
        private string _clientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET") ?? "YOUR_CLIENT_SECRET_HERE";
        private CancellationTokenSource? _downloadCancellationTokenSource;
        private List<System.Diagnostics.Process>? _activeProcesses;
        private readonly object _processLock = new object();
        private string _selectedSource = "Spotify"; // "Spotify" or "YouTube"
        private string _selectedFormat = "MP3"; // "MP3" or "MP4" (only for YouTube)
        private bool _isSpotifyPlaylist = false;
        private bool _isDownloadingAll = false;
        private bool _isDownloadAllStopped = false; // Track if Download All was stopped
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
            _spotifyLocalFilesPath = settings.SpotifyLocalFilesPath;
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

            // Delete all temp files and clear cache entries on app close
            try
            {
                var cache = Models.DownloadCache.Load();
                
                // Delete all temp files before clearing cache
                foreach (var entry in cache.Entries.Values)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(entry.TempFilePattern))
                        {
                            string? dir = Path.GetDirectoryName(entry.TempFilePattern);
                            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            {
                                // Try to find files matching the pattern
                                string pattern = Path.GetFileName(entry.TempFilePattern) + ".*";
                                var files = Directory.GetFiles(dir, pattern);
                                foreach (var file in files)
                                {
                                    try
                                    {
                                        File.Delete(file);
                                    }
                                    catch { /* Ignore individual file deletion errors */ }
                                }
                                
                                // Also try to find files with just the GUID pattern (in case pattern format differs)
                                string guidPattern = Path.GetFileName(entry.TempFilePattern);
                                if (!string.IsNullOrEmpty(guidPattern) && guidPattern.StartsWith("temp_"))
                                {
                                    var guidFiles = Directory.GetFiles(dir, guidPattern + ".*");
                                    foreach (var file in guidFiles)
                                    {
                                        try
                                        {
                                            if (!files.Contains(file)) // Don't delete twice
                                            {
                                                File.Delete(file);
                                            }
                                        }
                                        catch { /* Ignore individual file deletion errors */ }
                                    }
                                }
                            }
                        }
                        
                        // Also check TempFilePath if it exists
                        if (!string.IsNullOrEmpty(entry.TempFilePath) && File.Exists(entry.TempFilePath))
                        {
                            try
                            {
                                File.Delete(entry.TempFilePath);
                            }
                            catch { /* Ignore individual file deletion errors */ }
                        }
                    }
                    catch { /* Ignore errors for individual entries */ }
                }
                
                // Also clean up any orphaned temp files in common temp directories
                try
                {
                    // Check download directory for temp files
                    if (!string.IsNullOrEmpty(_downloadPath) && Directory.Exists(_downloadPath))
                    {
                        var tempFiles = Directory.GetFiles(_downloadPath, "temp_*.*");
                        foreach (var file in tempFiles)
                        {
                            try
                            {
                                File.Delete(file);
                            }
                            catch { }
                        }
                    }
                    
                    // Check system temp directory for temp files from this app
                    string systemTemp = Path.GetTempPath();
                    if (Directory.Exists(systemTemp))
                    {
                        var systemTempFiles = Directory.GetFiles(systemTemp, "temp_*.*");
                        foreach (var file in systemTempFiles)
                        {
                            try
                            {
                                // Only delete if file is recent (within last 24 hours) to avoid deleting other apps' files
                                var fileInfo = new FileInfo(file);
                                if (fileInfo.CreationTime > DateTime.Now.AddHours(-24))
                                {
                                    File.Delete(file);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { /* Ignore cleanup errors */ }
                
                // Clear all cache entries
                cache.ClearAll();
            }
            catch { }

            // Force immediate exit - don't wait for anything
            e.Cancel = false;
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

    }
}

