using System;
using System.IO;
using Newtonsoft.Json;

namespace MediaConverterToMP3.Models
{
    public class AppSettings
    {
        public string DownloadPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "MediaDownloads");

        public string? SpotifyClientId { get; set; }
        public string? SpotifyClientSecret { get; set; }

        private static string GetSettingsPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string settingsDir = Path.Combine(appDataPath, "MediaConverterToMP3");
            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }
            return Path.Combine(settingsDir, "settings.json");
        }

        public static AppSettings Load()
        {
            string settingsPath = GetSettingsPath();
            if (File.Exists(settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(settingsPath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
                catch
                {
                    return new AppSettings();
                }
            }
            return new AppSettings();
        }

        public void Save()
        {
            string settingsPath = GetSettingsPath();
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save settings: {ex.Message}", ex);
            }
        }
    }
}

