using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using YTShortsManager.Models;
using YTShortsManager.Services;

namespace YTShortsManager
{
    public partial class HistoryWindow : Window
    {
        /// <summary>Папка, внутри которой разрешено открывать файлы. Задаётся вызывающей стороной.</summary>
        public string? AllowedFolder { get; set; }

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".webm", ".mov", ".avi",
            ".mp3", ".m4a", ".opus", ".ogg", ".wav", ".flac",
            ".srt", ".vtt", ".ass",
            ".jpg", ".jpeg", ".png", ".webp"
        };

        public HistoryWindow() : this(AppStrings.Default)
        {
        }

        public HistoryWindow(string language)
        {
            InitializeComponent();
            ApplyLanguage(language);
            SourceInitialized += (_, _) => WindowTheme.UseDarkTitleBar(this);
        }

        private IReadOnlyDictionary<string, string> _text = AppStrings.Get(AppStrings.Default);

        private void ApplyLanguage(string language)
        {
            _text = AppStrings.Get(language);

            Title = _text["HistoryTitle"];
            HistoryTitleText.Text = _text["HistoryTitle"];
            HistoryHintText.Text = _text["HistoryHint"];
            FilesLabelRun.Text = _text["FilesLabel"] + " ";
        }

        private void Item_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is HistoryItem item)
            {
                string? path = ResolveSafePath(item.Path);

                if (path == null)
                {
                    MessageBox.Show(
                        _text["HistoryBlocked"],
                        _text["ErrorTitle"]);
                    return;
                }

                if (!File.Exists(path))
                {
                    MessageBox.Show($"{_text["HistoryNotFound"]}\n{path}", _text["ErrorTitle"]);
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"{_text["HistoryOpenFailed"]}\n{ex.Message}", _text["ErrorTitle"]);
                }
            }
        }

        /// <summary>
        /// Возвращает полный путь, только если он локальный, имеет разрешённое расширение
        /// и лежит внутри <see cref="AllowedFolder"/>. Иначе — null.
        /// </summary>
        private string? ResolveSafePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!Path.IsPathRooted(path)) return null;
            if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
                path.StartsWith("//", StringComparison.Ordinal)) return null;

            string extension = Path.GetExtension(path);
            if (!AllowedExtensions.Contains(extension)) return null;

            try
            {
                string fullPath = Path.GetFullPath(path);

                if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)) return null;

                if (!string.IsNullOrWhiteSpace(AllowedFolder))
                {
                    string root = Path.GetFullPath(AllowedFolder)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return null;
                }

                return fullPath;
            }
            catch
            {
                return null;
            }
        }
    }
}
