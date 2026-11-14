using System;
using System.Collections.Generic;

namespace YTShortsManager.Services
{
    public static class AppStrings
    {
        public const string Default = "RU";

        private static readonly Dictionary<string, Dictionary<string, string>> All =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["RU"] = new()
                {
                    ["DownloadBtn"] = "СКАЧАТЬ ВИДЕО",
                    ["PasteBtn"] = "Вставить",
                    ["FolderLabel"] = "ПАПКА СОХРАНЕНИЯ",
                    ["BrowseBtn"] = "Обзор",
                    ["HistoryBtn"] = "📜 История",
                    ["ClearCacheBtn"] = "🧹 Очистить кэш",
                    ["UpdateYtdlpBtn"] = "⬆️ Обновить yt-dlp",
                    ["UpdateAppBtn"] = "📦 Обновить программу",
                    ["ClipboardToggle"] = "Авто-вставка",
                    ["StartupToggle"] = "Запускать с Windows",
                    ["LangLabel"] = "Язык / Language:",
                    ["HeroKicker"] = "СКАЧИВАЙ · ОБРЕЗАЙ · ГОТОВО",
                    ["Subtitle"] = "Shorts, видео и музыка — в один клик",
                    ["LinkLabel"] = "ССЫЛКА НА ВИДЕО",
                    ["QueueLabel"] = "ОЧЕРЕДЬ ЗАГРУЗОК",
                    ["QueueEmpty"] = "Очередь пуста — добавьте ссылку слева",
                    ["ProgressLabel"] = "ПРОГРЕСС",
                    ["LogLabel"] = "ЖУРНАЛ",
                    ["StatusQueued"] = "В очереди",
                    ["StatusDownloading"] = "Скачивается...",
                    ["StatusDone"] = "Готово",
                    ["StatusFailed"] = "Ошибка",
                    ["HistoryTitle"] = "История скачиваний",
                    ["HistoryHint"] = "Нажмите на карточку, чтобы открыть файл",
                    ["FilesLabel"] = "Файлов:",
                    ["ErrorTitle"] = "Ошибка",
                    ["HistoryBlocked"] = "Файл находится вне папки загрузок или имеет недопустимый тип.\nОткрытие заблокировано в целях безопасности.",
                    ["HistoryNotFound"] = "Файл не найден по пути:",
                    ["HistoryOpenFailed"] = "Не удалось открыть файл:",
                    ["Ready"] = "Готово"
                },
                ["EN"] = new()
                {
                    ["DownloadBtn"] = "DOWNLOAD VIDEO",
                    ["PasteBtn"] = "Paste",
                    ["FolderLabel"] = "SAVE FOLDER",
                    ["BrowseBtn"] = "Browse",
                    ["HistoryBtn"] = "📜 History",
                    ["ClearCacheBtn"] = "🧹 Clear Cache",
                    ["UpdateYtdlpBtn"] = "⬆️ Update yt-dlp",
                    ["UpdateAppBtn"] = "📦 Update App",
                    ["ClipboardToggle"] = "Auto-paste",
                    ["StartupToggle"] = "Run on Windows Startup",
                    ["LangLabel"] = "Language / Язык:",
                    ["HeroKicker"] = "DOWNLOAD · TRIM · DONE",
                    ["Subtitle"] = "Shorts, videos and music in one click",
                    ["LinkLabel"] = "VIDEO LINK",
                    ["QueueLabel"] = "DOWNLOAD QUEUE",
                    ["QueueEmpty"] = "Queue is empty — add a link on the left",
                    ["ProgressLabel"] = "PROGRESS",
                    ["LogLabel"] = "LOG",
                    ["StatusQueued"] = "Queued",
                    ["StatusDownloading"] = "Downloading...",
                    ["StatusDone"] = "Done",
                    ["StatusFailed"] = "Error",
                    ["HistoryTitle"] = "Download history",
                    ["HistoryHint"] = "Click a card to open the file",
                    ["FilesLabel"] = "Files:",
                    ["ErrorTitle"] = "Error",
                    ["HistoryBlocked"] = "The file is outside the download folder or has an unsupported type.\nOpening is blocked for security reasons.",
                    ["HistoryNotFound"] = "File not found at path:",
                    ["HistoryOpenFailed"] = "Could not open the file:",
                    ["Ready"] = "Done"
                }
            };

        /// <summary>Приводит любой ввод к поддерживаемому языку (защита от мусора в config.json).</summary>
        public static string Normalize(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return Default;
            string candidate = lang.Trim().ToUpperInvariant();
            return All.ContainsKey(candidate) ? candidate : Default;
        }

        public static IReadOnlyDictionary<string, string> Get(string? lang) => All[Normalize(lang)];

        /// <summary>Текущий язык интерфейса — нужен моделям, которые не знают о настройках.</summary>
        public static string CurrentLanguage { get; private set; } = Default;

        public static void SetLanguage(string? lang) => CurrentLanguage = Normalize(lang);

        /// <summary>Локализованная подпись статуса очереди по его внутреннему токену.</summary>
        public static string QueueStatusText(string status) =>
            Get(CurrentLanguage).TryGetValue("Status" + status, out string? value) ? value : status;
    }
}
