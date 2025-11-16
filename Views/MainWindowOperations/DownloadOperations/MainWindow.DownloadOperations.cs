using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MediaConverterToMP3.Models;
using MediaConverterToMP3.Views.MainWindowOperations.Utilities;
using Newtonsoft.Json;

namespace MediaConverterToMP3.Views
{
    public partial class MainWindow
    {
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var track = button?.Tag as TrackItem;

            if (track == null) return;
            
            // Prevent individual downloads when Download All is running or stopped
            if (_isDownloadingAll || (_isDownloadAllStopped && _isSpotifyPlaylist && _selectedSource == "Spotify"))
            {
                if (_isDownloadingAll)
                {
                    StatusText.Text = "Please wait for Download All to complete or stop it first.";
                }
                else
                {
                    StatusText.Text = "Use Continue All or Clear All buttons to manage stopped downloads.";
                }
                return;
            }

            // Handle Continue button click
            if (track.ShowContinueButton)
            {
                await ContinueDownload(track);
                return;
            }

            // Handle Clear button click
            if (track.ShowClearButton)
            {
                ClearStoppedDownload(track);
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

                // Save cache entry for stopped download
                var stopCache = DownloadCache.Load();
                
                // Get or create cache entry
                var stopCacheEntry = stopCache.GetEntry(track.Id);
                if (stopCacheEntry == null)
                {
                    // Create new cache entry if it doesn't exist
                    string stopExtension = _selectedSource == "YouTube" && _selectedFormat == "MP4" ? ".mp4" : ".mp3";
                    string stopOutputPath = Path.Combine(_downloadPath, $"{FileUtilities.SanitizeFileName(track.Title)} - {FileUtilities.SanitizeFileName(track.Artist)}{stopExtension}");
                    
                    // Determine search query
                    string stopSearchQuery;
                    if (_selectedSource == "YouTube" && !string.IsNullOrEmpty(track.Id) && track.Id.Length == 11)
                    {
                        stopSearchQuery = $"https://www.youtube.com/watch?v={track.Id}";
                    }
                    else if (_selectedSource == "YouTube" && track.Album == "YouTube" && track.Artist == "YouTube Search")
                    {
                        stopSearchQuery = track.Title;
                    }
                    else
                    {
                        stopSearchQuery = $"{track.Title} {track.Artist}";
                    }
                    
                    string stopDownloadUrl = stopSearchQuery.StartsWith("http://") || stopSearchQuery.StartsWith("https://")
                        ? stopSearchQuery
                        : $"ytsearch:{stopSearchQuery}";
                    
                    string? stopOutputDir = Path.GetDirectoryName(stopOutputPath);
                    string stopTempDir = stopOutputDir ?? Path.GetTempPath();
                    string stopTempGuid = Guid.NewGuid().ToString();
                    string stopTempPathPattern = Path.Combine(stopTempDir, $"temp_{stopTempGuid}");
                    
                    stopCacheEntry = new DownloadCacheEntry
                    {
                        TrackId = track.Id,
                        TrackTitle = track.Title,
                        TrackArtist = track.Artist,
                        OutputPath = stopOutputPath,
                        TempFilePattern = stopTempPathPattern,
                        DownloadUrl = stopDownloadUrl,
                        Source = _selectedSource,
                        Format = _selectedFormat,
                        Progress = track.DownloadProgress,
                        Timestamp = DateTime.Now
                    };
                }
                else
                {
                    // Update existing cache entry with current progress
                    stopCacheEntry.Progress = track.DownloadProgress;
                }
                
                stopCache.SetEntry(track.Id, stopCacheEntry);

                // Update track properties to show Continue/Clear buttons
                // Set these in the correct order to ensure UI updates
                track.IsDownloading = false;
                track.DownloadButtonText = ""; // Hide main button first
                track.CanDownload = false; // Make download button unclickable
                track.HasStoppedDownload = true;
                track.ShowContinueButton = true;
                track.ShowClearButton = true;
                track.ShowProgress = true; // Keep progress visible to show where we stopped
                
                StatusText.Text = $"Download stopped ({track.DownloadProgress:F0}%). Click Continue to resume or Clear to start over.";

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
            string outputPath = Path.Combine(_downloadPath, $"{FileUtilities.SanitizeFileName(track.Title)} - {FileUtilities.SanitizeFileName(track.Artist)}{extension}");

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
                track.HasStoppedDownload = false;
                track.ShowContinueButton = false;
                track.ShowClearButton = false;
                StatusText.Text = $"File already exists: {track.Title}";
                return;
            }

            // Check cache for partial download
            var cache = DownloadCache.Load();
            var cacheEntry = cache.GetEntry(track.Id);
            if (cacheEntry != null && cacheEntry.Source == _selectedSource && cacheEntry.Format == _selectedFormat)
            {
                // Check if temp file still exists
                bool tempFileExists = false;
                if (!string.IsNullOrEmpty(cacheEntry.TempFilePattern))
                {
                    string? dir = Path.GetDirectoryName(cacheEntry.TempFilePattern);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        string pattern = Path.GetFileName(cacheEntry.TempFilePattern) + ".*";
                        var files = Directory.GetFiles(dir, pattern);
                        tempFileExists = files.Length > 0;
                    }
                }

                if (tempFileExists)
                {
                    // Show Continue/Clear buttons
                    track.HasStoppedDownload = true;
                    track.ShowContinueButton = true;
                    track.ShowClearButton = true;
                    track.DownloadButtonText = "";
                    track.CanDownload = false;
                    track.DownloadProgress = cacheEntry.Progress;
                    track.ShowProgress = true;
                    StatusText.Text = $"Found partial download ({cacheEntry.Progress:F0}%). Click Continue to resume or Clear to start over.";
                    return;
                }
                else
                {
                    // Temp file doesn't exist, clear cache entry
                    cache.ClearEntry(track.Id);
                }
            }

            try
            {
                // Initialize cancellation token source for this download first
                _downloadCancellationTokenSource = new CancellationTokenSource();

                track.CanDownload = false;
                track.IsDownloading = true;
                track.DownloadButtonText = "⏹️ Stop";
                track.HasStoppedDownload = false;
                track.ShowContinueButton = false;
                track.ShowClearButton = false;
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
                track.HasStoppedDownload = false;
                track.ShowContinueButton = false;
                track.ShowClearButton = false;
                _downloadCancellationTokenSource?.Dispose();
                _downloadCancellationTokenSource = null;

                // Clear cache entry on successful completion
                var completedCache = DownloadCache.Load();
                completedCache.ClearEntry(track.Id);

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
                    // Don't reset here - the stop handler above already sets Continue/Clear buttons
                    // Just ensure state is correct
                    if (!track.HasStoppedDownload)
                {
                    track.CanDownload = true;
                    track.DownloadButtonText = "Download";
                    track.ShowProgress = false;
                    track.DownloadProgress = 0;
                    }
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

        private async Task DownloadAndConvertTrack(TrackItem track, string outputPath, CancellationToken cancellationToken = default, DownloadCacheEntry? existingCacheEntry = null)
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
                string? ytDlpPath = FileUtilities.FindYtDlp();
                if (string.IsNullOrEmpty(ytDlpPath))
                {
                    string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                    string projectDir = Directory.GetParent(currentDir)?.Parent?.Parent?.FullName ?? currentDir;
                    throw new Exception($"yt-dlp not found.\n\nPlease place yt-dlp.exe in one of these locations:\n1. {currentDir}\n2. {projectDir}\n\nOr add it to your system PATH.");
                }

                // Find ffmpeg
                string? ffmpegPath = FileUtilities.FindFfmpeg();
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
                string tempGuid = existingCacheEntry != null && !string.IsNullOrEmpty(existingCacheEntry.TempFilePattern) 
                    ? Path.GetFileNameWithoutExtension(existingCacheEntry.TempFilePattern).Replace("temp_", "") 
                    : Guid.NewGuid().ToString();
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

                // Create or update cache entry
                var cache = DownloadCache.Load();
                var cacheEntry = existingCacheEntry ?? new DownloadCacheEntry
                {
                    TrackId = track.Id,
                    TrackTitle = track.Title,
                    TrackArtist = track.Artist,
                    OutputPath = outputPath,
                    TempFilePattern = tempPathPattern,
                    DownloadUrl = downloadUrl,
                    Source = _selectedSource,
                    Format = _selectedFormat,
                    Progress = 0,
                    Timestamp = DateTime.Now
                };
                
                // Update cache entry if it exists
                if (existingCacheEntry != null)
                {
                    cacheEntry.TempFilePattern = tempPathPattern;
                    cacheEntry.DownloadUrl = downloadUrl;
                }
                
                cache.SetEntry(track.Id, cacheEntry);

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
                                    
                                    // Update cache with progress
                                    var progressCache = DownloadCache.Load();
                                    var progressEntry = progressCache.GetEntry(track.Id);
                                    if (progressEntry != null)
                                    {
                                        progressEntry.Progress = totalProgress;
                                        progressCache.SetEntry(track.Id, progressEntry);
                                    }
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
                                    double totalProgress = isMP4Local ? progress : (progress * 0.5); // MP4: 100%, MP3: 50% (conversion is other 50%)
                                    track.DownloadProgress = totalProgress;
                                    
                                    // Update cache with progress
                                    var progressCache = DownloadCache.Load();
                                    var progressEntry = progressCache.GetEntry(track.Id);
                                    if (progressEntry != null)
                                    {
                                        progressEntry.Progress = totalProgress;
                                        progressCache.SetEntry(track.Id, progressEntry);
                                    }
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
                    void AddMP4Metadata(string key, string? value)
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
                void AddMetadata(string key, string? value)
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
                                    
                                    // Update cache with progress
                                    var progressCache = DownloadCache.Load();
                                    var progressEntry = progressCache.GetEntry(track.Id);
                                    if (progressEntry != null)
                                    {
                                        progressEntry.Progress = totalProgress;
                                        progressCache.SetEntry(track.Id, progressEntry);
                                    }
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
                
                // For Spotify Download All, show Continue/Clear All buttons instead of resetting
                if (_selectedSource == "Spotify" && _isSpotifyPlaylist)
                {
                    // Save cache for stopped tracks and preserve progress
                    var cache = DownloadCache.Load();
                    foreach (var track in _tracks)
                    {
                        if (track.DownloadButtonText == "Downloaded ✓" || track.DownloadButtonText == "Already Downloaded ✓")
                        {
                            continue;
                        }
                        else if (track.DownloadButtonText == "Downloading..." || track.DownloadButtonText == "Queued...")
                        {
                            // Save cache for stopped tracks with current progress
                            var cacheEntry = cache.GetEntry(track.Id);
                            if (cacheEntry != null)
                            {
                                cacheEntry.Progress = track.DownloadProgress;
                                cache.SetEntry(track.Id, cacheEntry);
                            }
                            // Don't show Continue/Clear on individual buttons when Download All is active
                            track.HasStoppedDownload = false;
                            track.ShowContinueButton = false;
                            track.ShowClearButton = false;
                            track.DownloadButtonText = "Queued...";
                            track.CanDownload = false;
                            // Preserve progress - don't reset it
                            track.ShowProgress = true;
                        }
                    }
                    
                    // Hide Download All button and show Continue/Clear All buttons
                    _isDownloadAllStopped = true;
                    DownloadAllButton.Visibility = System.Windows.Visibility.Collapsed;
                    ContinueAllButton.Visibility = System.Windows.Visibility.Visible;
                    ClearAllButton.Visibility = System.Windows.Visibility.Visible;
                    StatusText.Text = "Download All stopped. Click Continue All to resume or Clear All to start over.";
                }
                else
                {
                    _isDownloadAllStopped = false;
                DownloadAllButton.Content = "Download All";
                DownloadAllButton.Style = (System.Windows.Style)FindResource("PrimaryButtonStyle");
                    ContinueAllButton.Visibility = System.Windows.Visibility.Collapsed;
                    ClearAllButton.Visibility = System.Windows.Visibility.Collapsed;
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
                            track.HasStoppedDownload = false;
                            track.ShowContinueButton = false;
                            track.ShowClearButton = false;
                            track.ShowProgress = false;
                            track.DownloadProgress = 0;
                        }
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
                _isDownloadAllStopped = false;
                DownloadAllButton.Content = "⏹️ Stop";
                DownloadAllButton.Style = (System.Windows.Style)FindResource("StopButtonStyle");
                DownloadAllButton.Visibility = System.Windows.Visibility.Visible;
                ContinueAllButton.Visibility = System.Windows.Visibility.Collapsed;
                ClearAllButton.Visibility = System.Windows.Visibility.Collapsed;

                // Include tracks that can be downloaded OR are queued (stopped tracks to resume)
                var tracksToDownload = _tracks?.Where(t => t != null && 
                    (t.CanDownload || t.DownloadButtonText == "Queued..." || t.DownloadButtonText == "Downloading..."))
                    .ToList() ?? new List<TrackItem>();
                
                // Exclude already downloaded tracks from the download queue
                tracksToDownload = tracksToDownload.Where(t => 
                    t.DownloadButtonText != "Downloaded ✓" && 
                    t.DownloadButtonText != "Already Downloaded ✓").ToList();
                
                // Count already downloaded tracks to adjust the starting count
                int alreadyDownloadedCount = _tracks?.Count(t => 
                    t != null && (t.DownloadButtonText == "Downloaded ✓" || t.DownloadButtonText == "Already Downloaded ✓")) ?? 0;
                
                // Total tracks is the total number of tracks in the playlist
                int totalTracks = _tracks?.Count(t => t != null) ?? 0;
                
                int downloadedCount = alreadyDownloadedCount;

                if (totalTracks == 0)
                {
                    StatusText.Text = "No tracks to download.";
                    _isDownloadingAll = false;
                    _isDownloadAllStopped = false;
                    DownloadAllButton.Content = "Download All";
                    DownloadAllButton.Style = (System.Windows.Style)FindResource("PrimaryButtonStyle");
                    DownloadAllButton.Visibility = System.Windows.Visibility.Visible;
                    ContinueAllButton.Visibility = System.Windows.Visibility.Collapsed;
                    ClearAllButton.Visibility = System.Windows.Visibility.Collapsed;
                    _downloadAllCancellationTokenSource?.Dispose();
                    _downloadAllCancellationTokenSource = null;
                    return;
                }

                // Set up all tracks for Download All (including queued ones that will resume)
                foreach (var track in tracksToDownload)
                {
                    if (track != null)
                    {
                        // If it's already queued (stopped), keep it queued but ensure CanDownload is false
                        if (track.DownloadButtonText != "Queued...")
                        {
                            track.CanDownload = false; // Disable individual download buttons
                            track.DownloadButtonText = "Queued...";
                        }
                        else
                        {
                            // Ensure CanDownload is false for queued tracks too
                            track.CanDownload = false;
                        }
                        track.IsDownloading = false;
                    }
                }
                
                // Also disable download buttons for all tracks (including already downloaded ones)
                if (_tracks != null)
                {
                    foreach (var track in _tracks)
                    {
                        if (track != null && track.DownloadButtonText != "Downloaded ✓" && track.DownloadButtonText != "Already Downloaded ✓")
                        {
                            track.CanDownload = false; // Disable individual download buttons during Download All
                        }
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
                        
                        // Preserve progress from cache if resuming - start from where it ended
                        var resumeCache = DownloadCache.Load();
                        DownloadCacheEntry? resumeCacheEntry = resumeCache.GetEntry(track.Id);
                        if (resumeCacheEntry != null && resumeCacheEntry.Progress > 0)
                        {
                            // Start progress bar from cached percentage
                            track.DownloadProgress = resumeCacheEntry.Progress;
                            track.ShowProgress = true; // Show progress bar
                            _displayedProgressPercent = (int)resumeCacheEntry.Progress;
                            // Ensure progress bar reflects the cached progress immediately
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                track.DownloadProgress = resumeCacheEntry.Progress;
                            }, System.Windows.Threading.DispatcherPriority.Send);
                        }
                        else
                        {
                            _displayedProgressPercent = 0;
                            track.DownloadProgress = 0;
                            track.ShowProgress = true; // Always show progress bar during download
                            resumeCacheEntry = null; // No cache entry, start fresh
                        }

                        string fileNameTitle = string.IsNullOrWhiteSpace(track.Title) ? "Unknown Title" : track.Title;
                        string fileNameArtist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown Artist" : track.Artist;
                        // Use the currently selected format (MP3 or MP4 for YouTube)
                        string extension = ".mp3";
                        if (_selectedSource == "YouTube" && _selectedFormat == "MP4")
                        {
                            extension = ".mp4";
                        }
                        string outputPath = Path.Combine(_downloadPath, $"{FileUtilities.SanitizeFileName(fileNameTitle)} - {FileUtilities.SanitizeFileName(fileNameArtist)}{extension}");

                        if (_downloadAllCancellationTokenSource == null || _downloadAllCancellationTokenSource.Token.IsCancellationRequested)
                        {
                            StatusText.Text = "Download cancelled successfully.";
                            break;
                        }

                        int currentProgress = (int)track.DownloadProgress;
                        StatusText.Text = $"Downloading {downloadedCount + 1}/{totalTracks}: {track.Title} ({currentProgress}%)";

                        // Pass cache entry if resuming to continue from where it left off
                        await DownloadAndConvertTrack(track, outputPath, _downloadAllCancellationTokenSource.Token, resumeCacheEntry);

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
                
                // Only reset if not stopped (if stopped, Continue/Clear All buttons are shown and progress is preserved)
                if (!_isDownloadAllStopped)
                {
                DownloadAllButton.Content = "Download All";
                DownloadAllButton.Style = (System.Windows.Style)FindResource("PrimaryButtonStyle");
                    ContinueAllButton.Visibility = System.Windows.Visibility.Collapsed;
                    ClearAllButton.Visibility = System.Windows.Visibility.Collapsed;

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
                        // Reset to Download (only if Download All is not stopped)
                        track.IsDownloading = false;
                        // Only re-enable if Download All is not stopped
                        if (!_isDownloadAllStopped)
                        {
                            track.CanDownload = true; // Re-enable individual download buttons
                            track.DownloadButtonText = "Download";
                            track.ShowProgress = false;
                            track.DownloadProgress = 0;
                        }
                        // If stopped, keep CanDownload = false and preserve progress/state
                    }
                }
                }
                // If stopped, progress is preserved and Continue/Clear All buttons are shown (handled in stop handler)

                _downloadAllCancellationTokenSource?.Dispose();
                _downloadAllCancellationTokenSource = null;
            }
        }

        private async Task ContinueDownload(TrackItem track)
        {
            var cache = DownloadCache.Load();
            var cacheEntry = cache.GetEntry(track.Id);
            
            if (cacheEntry == null)
            {
                // No cache entry, start fresh
                ClearStoppedDownload(track);
                // Start a fresh download
                string freshExtension = _selectedSource == "YouTube" && _selectedFormat == "MP4" ? ".mp4" : ".mp3";
                string freshOutputPath = Path.Combine(_downloadPath, $"{FileUtilities.SanitizeFileName(track.Title)} - {FileUtilities.SanitizeFileName(track.Artist)}{freshExtension}");
                
                // Check if file already exists
                if (File.Exists(freshOutputPath))
                {
                    track.CanDownload = false;
                    track.DownloadButtonText = "Already Downloaded ✓";
                    track.HasStoppedDownload = false;
                    track.ShowContinueButton = false;
                    track.ShowClearButton = false;
                    StatusText.Text = $"File already exists: {track.Title}";
                    return;
                }
                
                // Start download
                try
                {
                    _downloadCancellationTokenSource = new CancellationTokenSource();
                    track.CanDownload = false;
                    track.IsDownloading = true;
                    track.DownloadButtonText = "⏹️ Stop";
                    track.HasStoppedDownload = false;
                    track.ShowContinueButton = false;
                    track.ShowClearButton = false;
                    _currentDownloadingTrack = track;
                    _displayedProgressPercent = 0;

                    _progressTimer = new System.Windows.Threading.DispatcherTimer();
                    _progressTimer.Interval = TimeSpan.FromMilliseconds(50);
                    _progressTimer.Tick += (s, e) =>
                    {
                        if (_currentDownloadingTrack != null)
                        {
                            double actualProgress = _currentDownloadingTrack.DownloadProgress;
                            int actualPercent = (int)Math.Round(actualProgress);
                            if (_displayedProgressPercent < actualPercent)
                            {
                                int gap = actualPercent - _displayedProgressPercent;
                                int increment = gap > 10 ? 5 : (gap > 5 ? 3 : (gap > 2 ? 2 : 1));
                                _displayedProgressPercent = Math.Min(_displayedProgressPercent + increment, actualPercent);
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
                    await DownloadAndConvertTrack(track, freshOutputPath, _downloadCancellationTokenSource.Token);

                    _progressTimer?.Stop();
                    _progressTimer = null;
                    _currentDownloadingTrack = null;
                    _displayedProgressPercent = 0;

                    track.IsDownloading = false;
                    track.ShowProgress = false;
                    track.DownloadProgress = 0;
                    track.HasStoppedDownload = false;
                    track.ShowContinueButton = false;
                    track.ShowClearButton = false;
                    _downloadCancellationTokenSource?.Dispose();
                    _downloadCancellationTokenSource = null;

                    var completedCache = DownloadCache.Load();
                    completedCache.ClearEntry(track.Id);

                    track.DownloadButtonText = "Downloaded ✓";
                    track.CanDownload = false;
                    StatusText.Text = $"Successfully downloaded: {track.Title}";
                }
                catch (Exception ex)
                {
                    _progressTimer?.Stop();
                    _progressTimer = null;
                    _currentDownloadingTrack = null;
                    _displayedProgressPercent = 0;

                    track.IsDownloading = false;
                    _downloadCancellationTokenSource?.Dispose();
                    _downloadCancellationTokenSource = null;

                    if (ex is OperationCanceledException || ex is TaskCanceledException)
                    {
                        if (!track.HasStoppedDownload)
                        {
                            track.CanDownload = true;
                            track.DownloadButtonText = "Download";
                            track.ShowProgress = false;
                            track.DownloadProgress = 0;
                        }
                        StatusText.Text = "Download cancelled successfully.";
                    }
                    else
                    {
                        track.CanDownload = true;
                        track.DownloadButtonText = "Download";
                        track.ShowProgress = false;
                        track.DownloadProgress = 0;
                        track.HasStoppedDownload = false;
                        track.ShowContinueButton = false;
                        track.ShowClearButton = false;
                        StatusText.Text = $"Download failed: {ex.Message}";

                        var errorDialog = new Views.ErrorDialog("Download Error", $"Failed to download {track.Title}:\n{ex.Message}")
                        {
                            Owner = this
                        };
                        errorDialog.ShowDialog();
                    }
                }
                return;
            }

            // Use saved download path - determine extension based on source and format
            string extension = _selectedSource == "YouTube" && _selectedFormat == "MP4" ? ".mp4" : ".mp3";
            string outputPath = Path.Combine(_downloadPath, $"{FileUtilities.SanitizeFileName(track.Title)} - {FileUtilities.SanitizeFileName(track.Artist)}{extension}");

            // Check if file already exists
            if (File.Exists(outputPath))
            {
                track.CanDownload = false;
                track.DownloadButtonText = "Already Downloaded ✓";
                track.HasStoppedDownload = false;
                track.ShowContinueButton = false;
                track.ShowClearButton = false;
                cache.ClearEntry(track.Id);
                StatusText.Text = $"File already exists: {track.Title}";
                return;
            }

            try
            {
                // Initialize cancellation token source for this download
                _downloadCancellationTokenSource = new CancellationTokenSource();

                track.CanDownload = false;
                track.IsDownloading = true;
                track.DownloadButtonText = "⏹️ Stop";
                track.HasStoppedDownload = false;
                track.ShowContinueButton = false;
                track.ShowClearButton = false;
                _currentDownloadingTrack = track;
                
                // Preserve progress from cache and ensure progress bar is visible
                if (cacheEntry.Progress > 0)
                {
                    track.DownloadProgress = cacheEntry.Progress;
                    track.ShowProgress = true; // Show progress bar with cached progress
                    _displayedProgressPercent = (int)cacheEntry.Progress;
                }
                else
                {
                    track.DownloadProgress = 0;
                    track.ShowProgress = true; // Show progress bar (will start from 0)
                    _displayedProgressPercent = 0;
                }

                // Start progressive percentage timer
                _progressTimer = new System.Windows.Threading.DispatcherTimer();
                _progressTimer.Interval = TimeSpan.FromMilliseconds(50);
                _progressTimer.Tick += (s, e) =>
                {
                    if (_currentDownloadingTrack != null)
                    {
                        double actualProgress = _currentDownloadingTrack.DownloadProgress;
                        int actualPercent = (int)Math.Round(actualProgress);

                        if (_displayedProgressPercent < actualPercent)
                        {
                            int gap = actualPercent - _displayedProgressPercent;
                            int increment = gap > 10 ? 5 : (gap > 5 ? 3 : (gap > 2 ? 2 : 1));
                            _displayedProgressPercent = Math.Min(_displayedProgressPercent + increment, actualPercent);
                            StatusText.Text = $"Downloading: {_currentDownloadingTrack.Title} - {_currentDownloadingTrack.Artist} ({_displayedProgressPercent}%)";
                        }
                        else if (_displayedProgressPercent >= 100)
                        {
                            _progressTimer?.Stop();
                        }
                    }
                };
                _progressTimer.Start();

                StatusText.Text = $"Resuming download: {track.Title} - {track.Artist} ({_displayedProgressPercent}%)";

                // Continue download with cached info
                await DownloadAndConvertTrack(track, outputPath, _downloadCancellationTokenSource.Token, cacheEntry);

                // Stop progress timer
                _progressTimer?.Stop();
                _progressTimer = null;
                _currentDownloadingTrack = null;
                _displayedProgressPercent = 0;

                // Reset download state
                track.IsDownloading = false;
                track.ShowProgress = false;
                track.DownloadProgress = 0;
                track.HasStoppedDownload = false;
                track.ShowContinueButton = false;
                track.ShowClearButton = false;
                _downloadCancellationTokenSource?.Dispose();
                _downloadCancellationTokenSource = null;

                // Clear cache entry on successful completion
                cache.ClearEntry(track.Id);

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

                // Check if it was a cancellation
                if (ex is OperationCanceledException || ex is TaskCanceledException)
                {
                    // Save cache for stopped download
                    var stoppedCache = DownloadCache.Load();
                    var stoppedEntry = stoppedCache.GetEntry(track.Id);
                    if (stoppedEntry != null)
                    {
                        stoppedEntry.Progress = track.DownloadProgress;
                        stoppedCache.SetEntry(track.Id, stoppedEntry);
                    }
                    track.HasStoppedDownload = true;
                    track.ShowContinueButton = true;
                    track.ShowClearButton = true;
                    track.DownloadButtonText = "";
                    track.CanDownload = false;
                    StatusText.Text = "Download stopped. Click Continue to resume or Clear to start over.";
                }
                else
                {
                    track.CanDownload = true;
                    track.DownloadButtonText = "Download";
                    track.ShowProgress = false;
                    track.DownloadProgress = 0;
                    track.HasStoppedDownload = false;
                    track.ShowContinueButton = false;
                    track.ShowClearButton = false;
                    StatusText.Text = $"Download failed: {ex.Message}";

                    var errorDialog = new Views.ErrorDialog("Download Error", $"Failed to download {track.Title}:\n{ex.Message}")
                    {
                        Owner = this
                    };
                    errorDialog.ShowDialog();
                }
            }
        }

        private void ClearStoppedDownload(TrackItem track)
        {
            var cache = DownloadCache.Load();
            var cacheEntry = cache.GetEntry(track.Id);
            
            if (cacheEntry != null)
            {
                // Try to delete temp files
                try
                {
                    if (!string.IsNullOrEmpty(cacheEntry.TempFilePattern))
                    {
                        string? dir = Path.GetDirectoryName(cacheEntry.TempFilePattern);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            string pattern = Path.GetFileName(cacheEntry.TempFilePattern) + ".*";
                            var files = Directory.GetFiles(dir, pattern);
                            foreach (var file in files)
                            {
                                try
                                {
                                    File.Delete(file);
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }

                // Clear cache entry
                cache.ClearEntry(track.Id);
            }

            // Reset track state
            track.HasStoppedDownload = false;
            track.ShowContinueButton = false;
            track.ShowClearButton = false;
            track.DownloadButtonText = "Download";
            track.CanDownload = true;
            track.ShowProgress = false;
            track.DownloadProgress = 0;
            StatusText.Text = "Cleared stopped download. Ready to start fresh.";
        }

        private void ContinueAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isDownloadAllStopped || !_isSpotifyPlaylist || _selectedSource != "Spotify")
                return;

            _isDownloadAllStopped = false;
            ContinueAllButton.Visibility = System.Windows.Visibility.Collapsed;
            ClearAllButton.Visibility = System.Windows.Visibility.Collapsed;
            DownloadAllButton.Visibility = System.Windows.Visibility.Visible;

            // Resume Download All by calling the handler again (fire and forget since it's async void)
            DownloadAllButton_Click(sender, e);
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isDownloadAllStopped || !_isSpotifyPlaylist || _selectedSource != "Spotify")
                return;

            var cache = DownloadCache.Load();
            
            // Clear cache and reset all stopped tracks
            foreach (var track in _tracks)
            {
                if (track.DownloadButtonText == "Downloaded ✓" || track.DownloadButtonText == "Already Downloaded ✓")
                {
                    continue;
                }
                else if (track.DownloadButtonText == "Queued..." || track.ShowProgress)
                {
                    // Clear cache entry
                    var cacheEntry = cache.GetEntry(track.Id);
                    if (cacheEntry != null)
                    {
                        // Try to delete temp files
                        try
                        {
                            if (!string.IsNullOrEmpty(cacheEntry.TempFilePattern))
                            {
                                string? dir = Path.GetDirectoryName(cacheEntry.TempFilePattern);
                                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                                {
                                    string pattern = Path.GetFileName(cacheEntry.TempFilePattern) + ".*";
                                    var files = Directory.GetFiles(dir, pattern);
                                    foreach (var file in files)
                                    {
                                        try
                                        {
                                            File.Delete(file);
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        catch { }

                        cache.ClearEntry(track.Id);
                    }

                    // Reset track completely
                    track.HasStoppedDownload = false;
                    track.ShowContinueButton = false;
                    track.ShowClearButton = false;
                    track.DownloadButtonText = "Download";
                    track.CanDownload = true;
                    track.IsDownloading = false;
                    track.ShowProgress = false; // Ensure progress bar is hidden
                    track.DownloadProgress = 0;
                }
            }

            _isDownloadAllStopped = false;
            _isDownloadingAll = false; // Ensure Download All is not active
            ContinueAllButton.Visibility = System.Windows.Visibility.Collapsed;
            ClearAllButton.Visibility = System.Windows.Visibility.Collapsed;
            DownloadAllButton.Content = "Download All";
            DownloadAllButton.Style = (System.Windows.Style)FindResource("PrimaryButtonStyle");
            DownloadAllButton.Visibility = System.Windows.Visibility.Visible;
            StatusText.Text = "Cleared all stopped downloads. Ready to start fresh.";
        }
    }
}

