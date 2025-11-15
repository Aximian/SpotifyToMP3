using Newtonsoft.Json;

namespace SpotifyToMP3.Models
{
    public class TokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonProperty("token_type")]
        public string TokenType { get; set; } = "";

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }

    public class SpotifySearchResponse
    {
        [JsonProperty("tracks")]
        public TracksResponse Tracks { get; set; } = null!;
    }

    public class TracksResponse
    {
        [JsonProperty("items")]
        public TrackResponse[] Items { get; set; } = null!;
    }

    public class TrackResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("artists")]
        public ArtistResponse[] Artists { get; set; } = null!;

        [JsonProperty("album")]
        public AlbumResponse? Album { get; set; }

        [JsonProperty("duration_ms")]
        public long DurationMs { get; set; }
    }

    public class ArtistResponse
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";
    }

    public class AlbumResponse
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("images")]
        public ImageResponse[]? Images { get; set; }

        [JsonProperty("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonProperty("release_date_precision")]
        public string? ReleaseDatePrecision { get; set; }

        [JsonProperty("artists")]
        public ArtistResponse[]? Artists { get; set; }
    }

    public class ImageResponse
    {
        [JsonProperty("url")]
        public string Url { get; set; } = "";

        [JsonProperty("height")]
        public int Height { get; set; }

        [JsonProperty("width")]
        public int Width { get; set; }
    }

    public class PlaylistTracksResponse
    {
        [JsonProperty("items")]
        public PlaylistTrackItem[] Items { get; set; } = null!;

        [JsonProperty("next")]
        public string? Next { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }

    public class PlaylistTrackItem
    {
        [JsonProperty("track")]
        public TrackResponse? Track { get; set; }
    }
}

