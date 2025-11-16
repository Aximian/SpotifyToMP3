using System;
using System.ComponentModel;
using Newtonsoft.Json;

namespace SpotifyToMP3.Models
{
    public class TrackItem : INotifyPropertyChanged
    {
        private string _downloadButtonText = "Download";
        private bool _canDownload = true;
        private double _downloadProgress = 0;
        private bool _showProgress = false;
        private string _progressText = "";

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string AlbumArtist { get; set; } = "";
        public string Year { get; set; } = "";
        public string Genre { get; set; } = "";
        public string TrackNumber { get; set; } = "";
        private TimeSpan _duration;
        public TimeSpan Duration 
        { 
            get => _duration;
            set
            {
                _duration = value;
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(FormattedDuration));
                OnPropertyChanged(nameof(EstimatedSize));
            }
        }
        public string? ImageUrl { get; set; }
        
        public string FormattedDuration
        {
            get
            {
                try
                {
                    if (_duration.TotalSeconds <= 0)
                        return "";
                    
                    if (_duration.TotalHours >= 1)
                        return $"{(int)_duration.TotalHours}:{_duration.Minutes:D2}:{_duration.Seconds:D2}";
                    else
                        return $"{_duration.Minutes}:{_duration.Seconds:D2}";
                }
                catch
                {
                    return "";
                }
            }
        }
        
        public string EstimatedSize
        {
            get
            {
                try
                {
                    if (_duration.TotalSeconds <= 0)
                        return "";
                    
                    // Estimate MP3 size: 320kbps = 40KB per second
                    double totalSeconds = Math.Abs(_duration.TotalSeconds);
                    double sizeInMB = (320.0 / 8.0) * totalSeconds / 1024.0;
                    
                    if (sizeInMB < 1)
                        return $"{(sizeInMB * 1024):F0} KB";
                    else
                        return $"{sizeInMB:F1} MB";
                }
                catch
                {
                    return "";
                }
            }
        }

        public string DownloadButtonText
        {
            get => _downloadButtonText;
            set
            {
                _downloadButtonText = value;
                OnPropertyChanged(nameof(DownloadButtonText));
            }
        }

        public bool CanDownload
        {
            get => _canDownload;
            set
            {
                _canDownload = value;
                OnPropertyChanged(nameof(CanDownload));
            }
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                _downloadProgress = Math.Max(0, Math.Min(100, value));
                OnPropertyChanged(nameof(DownloadProgress));
                OnPropertyChanged(nameof(ProgressWidth));
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public bool ShowProgress
        {
            get => _showProgress;
            set
            {
                _showProgress = value;
                OnPropertyChanged(nameof(ShowProgress));
            }
        }

        public string ProgressText
        {
            get
            {
                if (_downloadProgress > 0)
                    return $"Downloading... {_downloadProgress:F0}%";
                return _progressText;
            }
            set
            {
                _progressText = value;
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public double ProgressWidth
        {
            get => Math.Max(0, Math.Min(200, _downloadProgress * 2));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

