using System;
using System.Text.RegularExpressions;

namespace MediaConverterToMP3.Views.MainWindowOperations.Utilities
{
    public static class UrlParser
    {
        public static bool IsUrl(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            return input.StartsWith("http://") || input.StartsWith("https://") ||
                   input.StartsWith("www.") || input.Contains("://");
        }

        public static bool IsSpotifyUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("spotify.com") || url.StartsWith("spotify:");
        }

        public static bool IsYouTubeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("youtube.com") || url.Contains("youtu.be");
        }

        public static bool IsYouTubePlaylistUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("youtube.com/playlist") || url.Contains("list=");
        }

        public static string? ExtractPlaylistId(string url)
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
                    var match = Regex.Match(url, @"playlist/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }

            return null;
        }

        public static string? ExtractTrackId(string url)
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
                    var match = Regex.Match(url, @"track/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }

            return null;
        }

        public static string? ExtractYouTubeVideoId(string url)
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
                var watchMatch = Regex.Match(url, @"[?&]v=([a-zA-Z0-9_-]{11})");
                if (watchMatch.Success)
                {
                    return watchMatch.Groups[1].Value;
                }

                // Short youtu.be URL
                var shortMatch = Regex.Match(url, @"youtu\.be/([a-zA-Z0-9_-]{11})");
                if (shortMatch.Success)
                {
                    return shortMatch.Groups[1].Value;
                }

                // Embed URL
                var embedMatch = Regex.Match(url, @"youtube\.com/embed/([a-zA-Z0-9_-]{11})");
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
    }
}

