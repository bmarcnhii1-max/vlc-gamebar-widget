using System;
using System.IO;
using System.Text.Json;

namespace VlcControl
{
    internal sealed class AppSettings
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8080;
        public string Password { get; set; } = "gamebar";
        public bool Topmost { get; set; } = true;
        public bool LaunchVlcOnStart { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VlcControl",
            "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        if (string.IsNullOrWhiteSpace(loaded.Host)) loaded.Host = "127.0.0.1";
                        if (loaded.Port < 1 || loaded.Port > 65535) loaded.Port = 8080;
                        if (loaded.Password == null) loaded.Password = "gamebar";
                        return loaded;
                    }
                }
            }
            catch
            {
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }
    }
}
