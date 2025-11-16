using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MediaConverterToMP3.Models;
using MediaConverterToMP3.Views.MainWindowOperations.Utilities;
using Newtonsoft.Json;

namespace MediaConverterToMP3.Views
{
    public partial class MainWindow
    {
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
                    Year = FileUtilities.ExtractYearFromReleaseDate(trackResponse.Album?.ReleaseDate, trackResponse.Album?.ReleaseDatePrecision),
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
                                Year = FileUtilities.ExtractYearFromReleaseDate(item.Track.Album?.ReleaseDate, item.Track.Album?.ReleaseDatePrecision),
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
                        Year = FileUtilities.ExtractYearFromReleaseDate(track.Album?.ReleaseDate, track.Album?.ReleaseDatePrecision),
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
                }
            }
            else
            {
                StatusText.Text = "No results found. Try different search terms.";
            }
        }
    }
}

