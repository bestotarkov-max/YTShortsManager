using System;
using System.IO;
using System.Text.Json;
using YTShortsManager.Models;

namespace YTShortsManager.Services
{
    public static class SettingsStore
    {
        private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

        public static AppConfig Load(string path)
        {
            AppConfig config = new();
            try
            {
                if (File.Exists(path))
                    config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
            }
            catch
            {
                config = new AppConfig();
            }

            // Валидация: битый язык больше не роняет запуск.
            config.Language = AppStrings.Normalize(config.Language);

            if (string.IsNullOrWhiteSpace(config.DownloadFolder) || !Path.IsPathRooted(config.DownloadFolder))
                config.DownloadFolder = AppPaths.DefaultDownloadFolder;

            return config;
        }

        public static void Save(string path, AppConfig config)
        {
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(config, Pretty));
            }
            catch
            {
                // Настройки не критичны для работы.
            }
        }
    }
}
