# Spotify to MP3 Converter

A C# WPF application that searches Spotify tracks and downloads them as MP3 files using Spotify's API and yt-dlp.

## Quick Start

### For End Users (Pre-built Release)
1. Download the latest release and extract it
2. Run `SpotifyToMP3.exe` from the extracted folder
3. Click Settings and enter your Spotify credentials (see below)
4. Start searching and downloading

## Getting Spotify Credentials

1. Go to https://developer.spotify.com/dashboard
2. Log in with your Spotify account
3. Click "Create app"
4. Fill in:
   - App name: Any name
   - Website: `https://localhost`
   - Redirect URI: `https://localhost:8888/callback`
   - Check "Web API"
5. Click "Save"
6. Copy your Client ID and Client Secret (click "View client secret")
7. Paste them into the Settings window

**Keep your credentials private - never share your Client Secret.**

## How to Use

1. Launch the application and wait for "Connected to Spotify" message
2. Search for tracks:
   - Enter a song name, artist, or album (plain text)
   - Or paste a Spotify playlist/track URL
   - Press Enter or click Search
3. Download tracks:
   - Click Download next to any track
   - Use "Download All" for playlists
   - Files are saved as 320kbps MP3 to your chosen directory

## Building from Source

### Prerequisites
- .NET 8.0 SDK
- yt-dlp.exe, ffmpeg.exe, ffprobe.exe (place in `bin` folder)

### Build Steps

1. Clone the repository
2. Restore dependencies: `dotnet restore`
3. Build: `dotnet build`
4. Run: `dotnet run`

### Building Standalone Executable

1. Place `yt-dlp.exe`, `ffmpeg.exe`, and `ffprobe.exe` in the `bin` folder
2. Build:
   ```
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   ```
3. Copy external tools to publish folder:
   ```
   copy bin\yt-dlp.exe bin\Release\net8.0-windows\win-x64\publish\
   copy bin\ffmpeg.exe bin\Release\net8.0-windows\win-x64\publish\
   copy bin\ffprobe.exe bin\Release\net8.0-windows\win-x64\publish\
   ```

Executable location: `bin\Release\net8.0-windows\win-x64\publish\`

## Troubleshooting

**"Failed to connect to Spotify"**
- Verify your Client ID and Client Secret are correct
- Check your internet connection

**"yt-dlp not found" or "FFmpeg not found"**
- For standalone builds: Make sure the .exe files are in the same folder as SpotifyToMP3.exe
- For development: Place them in the `bin` folder

**Download fails**
- Check your internet connection
- Some tracks may not be available on YouTube

## Important Notes

- This tool is for educational purposes only
- Downloading copyrighted material may violate terms of service
- Ensure you have proper rights to download content
- Respect copyright laws and artist rights

## License

MIT License - see [Docs/LICENSE](Docs/LICENSE) for details

## Contributing

Contributions welcome. See [Docs/CONTRIBUTING.md](Docs/CONTRIBUTING.md) for guidelines.
