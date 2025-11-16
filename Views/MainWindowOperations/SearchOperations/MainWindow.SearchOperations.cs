using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediaConverterToMP3.Models;
using MediaConverterToMP3.Views.MainWindowOperations.Utilities;
using Newtonsoft.Json;

namespace MediaConverterToMP3.Views
{
    public partial class MainWindow
    {
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // Prevent searching while Download All is active
            if (_isDownloadingAll)
            {
                StatusText.Text = "Please stop Download All before searching.";
                return;
            }

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
                
                // Don't reset Download All stopped state here - it will be restored after loading tracks if cache entries exist
                // Only reset if it's not a playlist search
                _isDownloadingAll = false;

                if (_selectedSource == "Spotify")
                {
                    // Check if input looks like a URL but is not Spotify
                    if (UrlParser.IsUrl(searchQuery) && !UrlParser.IsSpotifyUrl(searchQuery))
                    {
                        ShowErrorDialog("Invalid URL",
                            $"The URL you entered is not a valid Spotify URL.\n\nPlease enter:\n• A Spotify track URL (e.g., https://open.spotify.com/track/...)\n• A Spotify playlist URL (e.g., https://open.spotify.com/playlist/...)\n• Or a song name to search");
                        return;
                    }

                    // Check if input is a Spotify playlist URL
                    string? playlistId = UrlParser.ExtractPlaylistId(searchQuery);
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
                        string? trackId = UrlParser.ExtractTrackId(searchQuery);
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
                    if (UrlParser.IsUrl(searchQuery) && !UrlParser.IsYouTubeUrl(searchQuery))
                    {
                        ShowErrorDialog("Invalid URL",
                            $"The URL you entered is not a valid YouTube URL.\n\nPlease enter:\n• A YouTube video URL (e.g., https://www.youtube.com/watch?v=...)\n• Or a song name to search");
                        return;
                    }

                    // Check if input is a YouTube URL
                    string? videoId = UrlParser.ExtractYouTubeVideoId(searchQuery);
                    if (!string.IsNullOrEmpty(videoId))
                    {
                        // Load YouTube video
                        await LoadYouTubeVideo(videoId);
                    }
                    else if (UrlParser.IsYouTubePlaylistUrl(searchQuery))
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

        // Note: LoadTrack, LoadPlaylistTracks, SearchTracks are in SpotifyOperations
        // Note: LoadYouTubeVideo, LoadYouTubeSearch are in YouTubeOperations
    }
}

