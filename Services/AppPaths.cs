using System;
using System.IO;

namespace YTShortsManager.Services
{
    /// <summary>Все пути программы в одном месте.</summary>
    internal static class AppPaths
    {
        public static string BaseDir { get; } =
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// Служебные и секретные файлы — в LOCALAPPDATA.
        /// Папку программы пользователи часто держат на Рабочем столе/в OneDrive,
        /// поэтому куки и логи туда писать нельзя (синхронизация = утечка).
        /// </summary>
        public static string DataDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YTShortsManager");

        public static string YtDlp => Path.Combine(BaseDir, "yt-dlp.exe");
        public static string Ffmpeg => Path.Combine(BaseDir, "ffmpeg.exe");
        public static string Deno => Path.Combine(BaseDir, "deno.exe");

        public static string ConfigFile => Path.Combine(BaseDir, "config.json");
        public static string HistoryFile => Path.Combine(BaseDir, "history.json");
        public static string LogFile => Path.Combine(DataDir, "app.log");

        public static string DefaultDownloadFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "YT_Shorts");

        public static void EnsureDataDir() => Directory.CreateDirectory(DataDir);

        /// <summary>Уникальный файл куки на одну операцию — исключает гонку между потоками.</summary>
        public static string NewTempCookieFile()
        {
            EnsureDataDir();
            return Path.Combine(DataDir, "cookies_" + Guid.NewGuid().ToString("N") + ".txt");
        }
    }
}
