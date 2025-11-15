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
        public TimeSpan Duration { get; set; }
        public string? ImageUrl { get; set; }

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

