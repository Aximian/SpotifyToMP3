using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MediaConverterToMP3.Models;
using MediaConverterToMP3.Views.MainWindowOperations.Utilities;
using Newtonsoft.Json;

namespace MediaConverterToMP3.Views
{
    public partial class MainWindow
    {
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
                string? ytDlpPath = FileUtilities.FindYtDlp();
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
                string? ytDlpPath = FileUtilities.FindYtDlp();
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
    }
}

