using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using YTShortsManager.Models;

namespace YTShortsManager.Services
{
    public static class HistoryStore
    {
        private const int MaxItems = 50;
        private const int MaxTitleLength = 300;

        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

        /// <summary>
        /// Обложка — только настоящий адрес YouTube. Иначе подменённый history.json
        /// мог бы подсунуть file:// или UNC-путь и вызвать сетевой запрос (утечка NTLM).
        /// </summary>
        private static readonly Regex ThumbnailRegex = new(
            @"^https://img\.youtube\.com/vi/[\w-]{11}/[A-Za-z0-9_]+\.jpg$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<HistoryItem> Load()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(AppPaths.HistoryFile)) return new List<HistoryItem>();

                    List<HistoryItem>? items =
                        JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(AppPaths.HistoryFile));

                    return Sanitize(items);
                }
                catch
                {
                    return new List<HistoryItem>();
                }
            }
        }

        public static void Add(string title, string path, string videoUrl)
        {
            if (!IsSafePath(path)) return;

            lock (Gate)
            {
                try
                {
                    List<HistoryItem> history = Load();
                    string videoId = VideoUtils.ExtractVideoId(videoUrl);
                    string thumbnailUrl = string.IsNullOrEmpty(videoId)
                        ? ""
                        : $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";

                    history.Insert(0, new HistoryItem
                    {
                        Title = TrimTitle(title),
                        Path = path,
                        Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                        ThumbnailUrl = ThumbnailRegex.IsMatch(thumbnailUrl) ? thumbnailUrl : ""
                    });

                    while (history.Count > MaxItems) history.RemoveAt(history.Count - 1);

                    File.WriteAllText(AppPaths.HistoryFile, JsonSerializer.Serialize(history, Pretty));
                }
                catch
                {
                    // История — не критичная функция.
                }
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                try
                {
                    if (File.Exists(AppPaths.HistoryFile)) File.Delete(AppPaths.HistoryFile);
                }
                catch
                {
                    // ignore
                }
            }
        }

        /// <summary>Приводит прочитанный с диска список к безопасному виду.</summary>
        private static List<HistoryItem> Sanitize(IEnumerable<HistoryItem>? items)
        {
            List<HistoryItem> result = new();
            if (items == null) return result;

            foreach (HistoryItem? item in items)
            {
                if (item == null) continue;
                if (!IsSafePath(item.Path)) continue;

                item.Title = TrimTitle(item.Title);
                item.Time = TrimTitle(item.Time);

                string thumbnail = item.ThumbnailUrl ?? "";
                item.ThumbnailUrl = ThumbnailRegex.IsMatch(thumbnail) ? thumbnail : "";

                result.Add(item);
                if (result.Count >= MaxItems) break;
            }

            return result;
        }

        /// <summary>
        /// Разрешаем только локальные абсолютные пути. UNC (\\server\share) отклоняем:
        /// Windows при обращении к такому пути отправляет NTLM-хэш.
        /// </summary>
        private static bool IsSafePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.Length > 32_000) return false;
            if (!Path.IsPathRooted(path)) return false;
            if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
                path.StartsWith("//", StringComparison.Ordinal)) return false;

            try
            {
                string full = Path.GetFullPath(path);
                return !full.StartsWith(@"\\", StringComparison.Ordinal) &&
                       !full.StartsWith("//", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string TrimTitle(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string text = value.Trim();
            return text.Length > MaxTitleLength ? text[..MaxTitleLength] : text;
        }
    }
}
