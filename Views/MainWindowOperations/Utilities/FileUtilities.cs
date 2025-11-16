using System;
using System.IO;

namespace MediaConverterToMP3.Views.MainWindowOperations.Utilities
{
    public static class FileUtilities
    {
        public static string? FindFfmpeg()
        {
            // Check current directory (where app is running from)
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possibleNames = { "ffmpeg.exe", "ffmpeg" };

            foreach (var name in possibleNames)
            {
                string path = Path.Combine(currentDir, name);
                if (File.Exists(path))
                    return path;
            }

            // Check bin folder - try multiple approaches
            try
            {
                // Approach 1: 2 levels up (bin/Debug/net8.0-windows -> bin)
                string? binDir1 = Directory.GetParent(currentDir)?.Parent?.FullName;
                if (!string.IsNullOrEmpty(binDir1))
                {
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(binDir1, name);
                        if (File.Exists(path))
                            return path;
                    }
                }

                // Approach 2: Look for a folder named "bin" in parent directories
                DirectoryInfo? current = new DirectoryInfo(currentDir);
                for (int i = 0; i < 5 && current != null; i++)
                {
                    current = current.Parent;
                    if (current != null && current.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var name in possibleNames)
                        {
                            string path = Path.Combine(current.FullName, name);
                            if (File.Exists(path))
                                return path;
                        }
                        break;
                    }
                }
            }
            catch { }

            // Check project root directory (3 levels up from bin/Debug/net8.0-windows)
            try
            {
                string? projectDir = Directory.GetParent(currentDir)?.Parent?.Parent?.FullName;
                if (!string.IsNullOrEmpty(projectDir))
                {
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(projectDir, name);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }
            catch { }

            // Check PATH
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(dir, name);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }

            return null;
        }

        public static string? FindYtDlp()
        {
            // Check current directory (where app is running from)
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possibleNames = { "yt-dlp.exe", "yt-dlp" };

            foreach (var name in possibleNames)
            {
                string path = Path.Combine(currentDir, name);
                if (File.Exists(path))
                    return path;
            }

            // Check bin folder - try multiple approaches
            try
            {
                // Approach 1: 2 levels up (bin/Debug/net8.0-windows -> bin)
                string? binDir1 = Directory.GetParent(currentDir)?.Parent?.FullName;
                if (!string.IsNullOrEmpty(binDir1))
                {
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(binDir1, name);
                        if (File.Exists(path))
                            return path;
                    }
                }

                // Approach 2: Look for a folder named "bin" in parent directories
                DirectoryInfo? current = new DirectoryInfo(currentDir);
                for (int i = 0; i < 5 && current != null; i++)
                {
                    current = current.Parent;
                    if (current != null && current.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var name in possibleNames)
                        {
                            string path = Path.Combine(current.FullName, name);
                            if (File.Exists(path))
                                return path;
                        }
                        break;
                    }
                }
            }
            catch { }

            // Check project root directory (3 levels up from bin/Debug/net8.0-windows)
            try
            {
                string? projectDir = Directory.GetParent(currentDir)?.Parent?.Parent?.FullName;
                if (!string.IsNullOrEmpty(projectDir))
                {
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(projectDir, name);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }
            catch { }

            // Check PATH
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    foreach (var name in possibleNames)
                    {
                        string path = Path.Combine(dir, name);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }

            return null;
        }

        public static string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        public static string ExtractYearFromReleaseDate(string? releaseDate, string? precision)
        {
            if (string.IsNullOrWhiteSpace(releaseDate))
                return "";

            // Spotify release_date can be in formats: "2023", "2023-06", "2023-06-15"
            // precision can be "year", "month", "day"
            if (precision == "year" || releaseDate.Length == 4)
            {
                return releaseDate.Substring(0, 4);
            }
            else if (releaseDate.Length >= 4)
            {
                return releaseDate.Substring(0, 4);
            }
            return "";
        }
    }
}

