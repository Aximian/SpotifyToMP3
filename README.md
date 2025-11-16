# Media Converter to MP3

A C# WPF application that searches and downloads music from both Spotify and YouTube, converting them to MP3 or MP4 files using Spotify's API and yt-dlp. Includes built-in support for adding downloaded MP3 files directly to Spotify Local Files.

## Quick Start

### For End Users (Pre-built Release)
1. **[Download the latest release](https://github.com/Aximian/MediaConverterToMP3/releases/latest)** and extract it
2. Run `MediaConverterToMP3.exe` from the extracted folder
3. Click Settings and enter your Spotify credentials (required for Spotify searches, see below)
4. Select your source (Spotify or YouTube) using the selector in the top left
5. Start searching and downloading

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

### Source Selection
- Use the source selector in the top left corner to switch between **Spotify** (green) and **YouTube** (blue/red)
- Spotify requires credentials (see below), YouTube works without any setup
- When YouTube is selected, you can choose between **MP3** or **MP4** format using the format selector

### Searching

1. **For Spotify:**
   - Launch the application and wait for "Connected to Spotify" message
   - Enter a song name, artist, or album (plain text)
   - Or paste a Spotify playlist/track URL
   - Press Enter or click Search

2. **For YouTube:**
   - Select YouTube from the source selector
   - Choose your preferred format: **MP3** (audio) or **MP4** (video) using the format selector
   - Enter a search query (song name, artist, etc.)
   - Or paste a YouTube video URL
   - Press Enter or click Search
   - Results show up to 30 matching videos

### Downloading

- Click **Download** next to any track to download it individually
- Use **Download All** button to download all tracks in the current list
- Use the **filter box** (with filter icon) to search through loaded results
- Files are saved with proper naming (Title - Artist.mp3 or .mp4) to your chosen directory
- Already downloaded files are marked with "Already Downloaded ✓"
- The app automatically detects existing files and shows appropriate status

### Adding to Spotify Local Files

- After downloading MP3 files, you can add them directly to Spotify Local Files
- Click **Add to Spotify Local** next to any downloaded MP3 track
- Configure your Spotify Local Files path in Settings (usually in Spotify's settings)
- **Note:** MP4 files cannot be added to Spotify (only MP3 supported)
- **Note:** The "Add to Spotify Local" button is automatically hidden for:
  - Spotify source tracks (they're already from Spotify)
  - MP4 format tracks (MP4 cannot be added to Spotify)

#### macOS
**Note:** WPF is Windows-only. For macOS support, the UI needs to be ported to a cross-platform framework like Avalonia UI or .NET MAUI.

Currently, macOS builds are not available. To add macOS support:
1. Port the UI to Avalonia UI (recommended - similar to WPF)
2. Or use .NET MAUI for cross-platform support
3. Update build scripts to include macOS runtime identifier: `osx-x64` or `osx-arm64`

## Features

- 🎵 **Dual Source Support**: Search and download from both Spotify and YouTube
- 🎬 **Format Selection**: Choose MP3 (audio) or MP4 (video) format for YouTube downloads
- 🔍 **Smart Search**: Text search or direct URL support for both platforms
- 🎨 **Modern UI**: Clean, intuitive interface with source and format selectors
- 🔽 **Filter Results**: Filter loaded tracks by title, artist, or album
- ⚡ **Fast Downloads**: Optimized YouTube search and image loading
- 📥 **Bulk Operations**: Download all tracks or add all MP3s to Spotify at once
- 📁 **Organized Output**: Files saved with proper naming (Title - Artist.mp3/mp4)
- 🎧 **Spotify Integration**: Add downloaded MP3 files directly to Spotify Local Files
- ⚙️ **Customizable**: Choose your download directory, Spotify Local Files path, and manage settings
- 🔄 **Smart Detection**: Automatically detects existing files and updates UI accordingly
- 👁️ **Intelligent UI**: Buttons automatically show/hide based on source, format, and file status

## Troubleshooting

**"Failed to connect to Spotify"**
- Verify your Client ID and Client Secret are correct
- Check your internet connection
- Make sure you've entered credentials in Settings

**"yt-dlp not found" or "FFmpeg not found"**
- For standalone builds: Make sure the .exe files are in the same folder as MediaConverterToMP3.exe
- For development: Place them in the `bin` folder
- These tools are required for YouTube downloads

**YouTube search is slow or fails**
- Check your internet connection
- Ensure yt-dlp.exe is present and up to date
- Try a different search query

**Download fails**
- Check your internet connection
- Some tracks may not be available
- For YouTube: Ensure yt-dlp and ffmpeg are working correctly

**"Add to Spotify Local" button not showing**
- Make sure you've configured the Spotify Local Files path in Settings
- The button only appears for YouTube MP3 tracks (not MP4 or Spotify source tracks)
- Ensure the track has been downloaded as MP3 first

**MP4 downloads not working**
- Ensure yt-dlp is up to date (MP4 support requires recent version)
- Check that FFmpeg is properly installed and accessible
- Some videos may have download restrictions

## Important Notes

- This tool is for educational purposes only
- Downloading copyrighted material may violate terms of service
- Ensure you have proper rights to download content
- Respect copyright laws and artist rights

## License

MIT License - see [Docs/LICENSE](Docs/LICENSE) for details

## Contributing

Contributions welcome. See [Docs/CONTRIBUTING.md](Docs/CONTRIBUTING.md) for guidelines.
