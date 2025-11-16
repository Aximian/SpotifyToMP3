using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MediaConverterToMP3.Models
{
    public class DownloadCacheEntry
    {
        public string TrackId { get; set; } = "";
        public string TrackTitle { get; set; } = "";
        public string TrackArtist { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string TempFilePath { get; set; } = "";
        public string TempFilePattern { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public double Progress { get; set; } = 0;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Source { get; set; } = ""; // "Spotify" or "YouTube"
        public string Format { get; set; } = ""; // "MP3" or "MP4"
    }

    public class DownloadCache
    {
        public Dictionary<string, DownloadCacheEntry> Entries { get; set; } = new Dictionary<string, DownloadCacheEntry>();

        private static string GetCachePath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string cacheDir = Path.Combine(appDataPath, "MediaConverterToMP3");
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
            return Path.Combine(cacheDir, "download_cache.json");
        }

        public static DownloadCache Load()
        {
            string cachePath = GetCachePath();
            if (File.Exists(cachePath))
            {
                try
                {
                    string json = File.ReadAllText(cachePath);
                    var cache = JsonConvert.DeserializeObject<DownloadCache>(json);
                    if (cache != null && cache.Entries != null)
                    {
                        // Clean up entries where temp files no longer exist
                        var keysToRemove = new List<string>();
                        foreach (var entry in cache.Entries)
                        {
                            if (!string.IsNullOrEmpty(entry.Value.TempFilePath) && !File.Exists(entry.Value.TempFilePath))
                            {
                                // Check if any file with the pattern exists
                                if (!string.IsNullOrEmpty(entry.Value.TempFilePattern))
                                {
                                    string? dir = Path.GetDirectoryName(entry.Value.TempFilePattern);
                                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                                    {
                                        string pattern = Path.GetFileName(entry.Value.TempFilePattern) + ".*";
                                        var files = Directory.GetFiles(dir, pattern);
                                        if (files.Length == 0)
                                        {
                                            keysToRemove.Add(entry.Key);
                                        }
                                    }
                                    else
                                    {
                                        keysToRemove.Add(entry.Key);
                                    }
                                }
                                else
                                {
                                    keysToRemove.Add(entry.Key);
                                }
                            }
                        }
                        foreach (var key in keysToRemove)
                        {
                            cache.Entries.Remove(key);
                        }
                        if (keysToRemove.Count > 0)
                        {
                            cache.Save();
                        }
                    }
                    return cache ?? new DownloadCache();
                }
                catch
                {
                    return new DownloadCache();
                }
            }
            return new DownloadCache();
        }

        public void Save()
        {
            string cachePath = GetCachePath();
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(cachePath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }

        public void ClearEntry(string trackId)
        {
            if (Entries.ContainsKey(trackId))
            {
                Entries.Remove(trackId);
                Save();
            }
        }

        public void ClearAll()
        {
            Entries.Clear();
            Save();
        }

        public DownloadCacheEntry? GetEntry(string trackId)
        {
            return Entries.ContainsKey(trackId) ? Entries[trackId] : null;
        }

        public void SetEntry(string trackId, DownloadCacheEntry entry)
        {
            Entries[trackId] = entry;
            Save();
        }
    }
}

