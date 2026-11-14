using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using YTShortsManager.Models;
using YTShortsManager.Services;

namespace YTShortsManager
{
    public partial class MainWindow : Window
    {
        private const int MaxDownloadAttempts = 3;

        /// <summary>
        /// Максимум запросов в ожидании. Ограничение защищает от того,
        /// что сторонняя страница/расширение завалит диск и память заданиями.
        /// </summary>
        private const int MaxQueueLength = 30;

        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DoneItemLinger = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan FailedItemLinger = TimeSpan.FromSeconds(8);

        private static readonly Version CurrentVersion =
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        private static readonly Regex ClipboardUrlRegex = new(
            @"(https?:\/\/(?:www\.)?(?:youtube\.com|youtu\.be)\/[^\s]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly DispatcherTimer _clipboardTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ObservableCollection<QueueItem> _queueUi = new();
        private readonly Queue<QueueEntry> _downloadQueue = new();
        private readonly object _queueLock = new();
        private readonly VideoInfoCache _videoInfoCache = new();

        private TaskbarIcon? _notifyIcon;
        private HttpBridgeServer? _bridge;
        private AppConfig _config = new();

        private bool _isProcessingQueue;
        private bool _isExiting;
        private bool _suppressLanguageChange;
        private string _lastClipboardText = "";

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                VersionText.Text = "v" + CurrentVersion.ToString(3);

                _notifyIcon = FindResource("MyNotifyIcon") as TaskbarIcon;
                QueueList.ItemsSource = _queueUi;

                _config = SettingsStore.Load(AppPaths.ConfigFile);

                _suppressLanguageChange = true;
                LangCombo.SelectedIndex = _config.Language == "EN" ? 1 : 0;
                _suppressLanguageChange = false;

                ApplyLanguage(_config.Language);
                PathTextBox.Text = _config.DownloadFolder;

                StartupToggle.IsChecked = StartupManager.IsEnabled();
                StartupToggle.Checked += StartupToggle_Changed;
                StartupToggle.Unchecked += StartupToggle_Changed;

                LoadWindowIcon();

                // Подчищаем куки, оставшиеся после аварийного завершения прошлого запуска.
                YtDlpService.CleanupLeftoverCookies(AppendLog);

                _clipboardTimer.Tick += ClipboardTimer_Tick;
                _clipboardTimer.Start();

                StartBridge();
                CheckDependencies();

                AppendLog("Программа запущена. Версия " + CurrentVersion.ToString(3));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критическая ошибка при запуске: {ex.Message}", "Ошибка запуска");
            }
        }

        // =====================================================================
        // Инициализация
        // =====================================================================
        private void LoadWindowIcon()
        {
            try
            {
                Uri iconUri = new("pack://application:,,,/icon.ico");
                Icon = new BitmapImage(iconUri);

                StreamResourceInfo? streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo != null && _notifyIcon != null)
                    _notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
            }
            catch
            {
                if (_notifyIcon != null) _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
        }

        private void StartBridge()
        {
            _bridge = new HttpBridgeServer(
                request => Task.FromResult(EnqueueDownload(request)),
                request => _videoInfoCache.GetDurationAsync(
                    request.Url,
                    token => YtDlpService.GetDurationAsync(
                        request.Url, request.CookiesData, AppendLog, token),
                    _lifetime.Token),
                AppendLog);

            if (!_bridge.Start())
                ShowBalloon("Локальный сервер недоступен", "Кнопка в браузере не будет работать.", BalloonIcon.Warning);
        }

        private void CheckDependencies()
        {
            List<string> missing = new();
            if (!File.Exists(AppPaths.YtDlp)) missing.Add("yt-dlp.exe");
            if (!File.Exists(AppPaths.Ffmpeg)) missing.Add("ffmpeg.exe");
            if (!File.Exists(AppPaths.Deno)) missing.Add("deno.exe");

            if (missing.Count == 0)
            {
                AppendLog("✅ Все зависимости на месте.");
                return;
            }

            AppendLog("⚠️ ВНИМАНИЕ: отсутствуют файлы: " + string.Join(", ", missing));
            MessageBox.Show(
                "В папке с программой не хватает важных файлов:\n\n" + string.Join("\n", missing) +
                "\n\nБез них скачивание видео может работать неправильно.",
                "Отсутствуют зависимости",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // =====================================================================
        // Логирование и UI-поток
        // =====================================================================
        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.Invoke(action);
        }

        private void PostToUi(Action action)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

            try
            {
                if (Dispatcher.CheckAccess()) action();
                else Dispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // Диспетчер уже остановлен — просто выходим.
            }
        }

        private void AppendLog(string message)
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            PostToUi(() => AppendLogCore(entry));
            FileLog.Write(entry);
        }

        private void AppendLogCore(string entry)
        {
            if (LogTextBox == null) return;

            LogTextBox.AppendText(entry + Environment.NewLine);

            // Не даём текстовому полю расти до бесконечности.
            const int maxLength = 200_000;
            if (LogTextBox.Text.Length > maxLength)
            {
                LogTextBox.Text = LogTextBox.Text[^100_000..];
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
            }

            LogTextBox.ScrollToEnd();
        }

        private void ShowBalloon(string title, string message, BalloonIcon icon)
        {
            PostToUi(() => _notifyIcon?.ShowBalloonTip(title, message, icon));
        }

        // =====================================================================
        // Локализация
        // =====================================================================
        private void ApplyLanguage(string language)
        {
            if (DownloadButton == null) return;

            _config.Language = AppStrings.Normalize(language);
            AppStrings.SetLanguage(_config.Language);
            IReadOnlyDictionary<string, string> text = AppStrings.Get(_config.Language);

            DownloadButton.Content = text["DownloadBtn"];
            PasteButton.Content = text["PasteBtn"];
            FolderLabel.Text = text["FolderLabel"];
            BrowseButton.Content = text["BrowseBtn"];
            HistoryButton.Content = text["HistoryBtn"];
            ClearCacheButton.Content = text["ClearCacheBtn"];
            UpdateButton.Content = text["UpdateYtdlpBtn"];
            AppUpdateButton.Content = text["UpdateAppBtn"];
            ClipboardToggle.Content = text["ClipboardToggle"];
            StartupToggle.Content = text["StartupToggle"];
            LangLabel.Text = text["LangLabel"];

            HeroKickerText.Text = text["HeroKicker"];
            SubtitleText.Text = text["Subtitle"];
            LinkLabel.Text = text["LinkLabel"];
            QueueLabel.Text = text["QueueLabel"];
            QueueEmptyText.Text = text["QueueEmpty"];
            ProgressLabel.Text = text["ProgressLabel"];
            LogLabel.Text = text["LogLabel"];

            foreach (QueueItem entry in _queueUi)
                entry.RefreshStatus();
        }

        private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressLanguageChange || LangCombo.SelectedItem is not ComboBoxItem item) return;

            ApplyLanguage(item.Content?.ToString() ?? AppStrings.Default);
            SettingsStore.Save(AppPaths.ConfigFile, _config);
        }

        // =====================================================================
        // Очередь загрузок
        // =====================================================================
        /// <returns>false, если очередь переполнена и запрос отклонён.</returns>
        private bool EnqueueDownload(DownloadRequest request)
        {
            QueueItem item = new()
            {
                Url = request.Url,
                Title = request.Url,
                Status = QueueStatus.Queued
            };

            // Коллекция ObservableCollection должна меняться только в UI-потоке.
            RunOnUi(() => _queueUi.Add(item));

            string outputFolder = _config.DownloadFolder;
            int queueLength = 0;
            bool overflow = false;
            bool startWorker = false;

            lock (_queueLock)
            {
                if (_downloadQueue.Count >= MaxQueueLength)
                {
                    overflow = true;
                }
                else
                {
                    _downloadQueue.Enqueue(new QueueEntry(request, item, outputFolder));
                    queueLength = _downloadQueue.Count;

                    if (!_isProcessingQueue)
                    {
                        _isProcessingQueue = true;
                        startWorker = true;
                    }
                }
            }

            if (overflow)
            {
                PostToUi(() => _queueUi.Remove(item));
                AppendLog($"⛔ Очередь переполнена ({MaxQueueLength}). Запрос отклонён: {request.Url}");
                return false;
            }

            AppendLog($"➕ Добавлено в очередь. Всего: {queueLength}");

            if (startWorker)
                _ = Task.Run(ProcessQueueAsync);

            return true;
        }

        private async Task ProcessQueueAsync()
        {
            while (true)
            {
                QueueEntry? entry;

                lock (_queueLock)
                {
                    if (_downloadQueue.Count == 0)
                    {
                        _isProcessingQueue = false;
                        entry = null;
                    }
                    else
                    {
                        entry = _downloadQueue.Dequeue();
                    }
                }

                if (entry == null)
                {
                    PostToUi(() =>
                    {
                        DownloadProgress.Value = 0;
                        ProgressText.Text = AppStrings.Get(_config.Language)["Ready"];
                    });
                    break;
                }

                try
                {
                    await ProcessEntryAsync(entry).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppendLog($"❌ Непредвиденная ошибка: {ex.Message}");
                }
            }
        }

        private async Task ProcessEntryAsync(QueueEntry entry)
        {
            QueueItem item = entry.Item;
            DownloadRequest request = entry.Request;

            PostToUi(() =>
            {
                item.Status = QueueStatus.Downloading;
                DownloadProgress.Value = 0;
                ProgressText.Text = "0%";
            });

            AppendLog($"▶ Начинаю скачивание: {request.Url}");

            DownloadResult? result = null;

            for (int attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
            {
                try
                {
                    result = await YtDlpService.DownloadAsync(
                            request,
                            entry.OutputFolder,
                            AppendLog,
                            new ActionProgress(ReportProgress),
                            _lifetime.Token)
                        .ConfigureAwait(false);

                    if (result.Success) break;
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    result = DownloadResult.Fail(ex.Message);
                }

                if (attempt >= MaxDownloadAttempts) break;
                if (result is { Retryable: false }) break;

                AppendLog($"⚠️ Попытка {attempt}/{MaxDownloadAttempts} не удалась: {result?.Error}. Повтор через {RetryDelay.TotalSeconds:0} сек...");

                try
                {
                    await Task.Delay(RetryDelay, _lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            if (result is { Success: true })
            {
                string? filePath = result.FilePath;

                PostToUi(() =>
                {
                    item.Status = QueueStatus.Done;
                    DownloadProgress.Value = 100;
                    ProgressText.Text = "100%";

                    if (!string.IsNullOrEmpty(filePath))
                        HistoryStore.Add(Path.GetFileName(filePath), filePath, request.Url);
                });

                AppendLog(string.IsNullOrEmpty(filePath)
                    ? "✅ Загрузка завершена."
                    : "✅ Загрузка завершена: " + filePath);

                string balloonText = string.IsNullOrEmpty(filePath)
                    ? "Видео скачано"
                    : Path.GetFileName(filePath);
                ShowBalloon("Загрузка завершена", balloonText, BalloonIcon.Info);
                ScheduleRemoval(item, DoneItemLinger);
                return;
            }

            string error = result?.Error ?? "неизвестная ошибка";

            PostToUi(() => item.Status = QueueStatus.Failed);
            AppendLog($"❌ Не удалось скачать {request.Url}: {error}");
            ShowBalloon("Ошибка загрузки", error, BalloonIcon.Error);
            ScheduleRemoval(item, FailedItemLinger);
        }

        private void ScheduleRemoval(QueueItem item, TimeSpan delay)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, _lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Программа закрывается — не важно.
                }

                PostToUi(() => _queueUi.Remove(item));
            });
        }

        private void ReportProgress(double percent)
        {
            double value = Math.Clamp(percent, 0, 100);
            PostToUi(() =>
            {
                DownloadProgress.Value = value;
                ProgressText.Text = value.ToString("F1", CultureInfo.InvariantCulture) + "%";
            });
        }

        /// <summary>
        /// Читает путь из поля ввода, проверяет и сохраняет в конфиг.
        /// Вызывается только из UI-потока (обращение к TextBox из фонового потока недопустимо).
        /// </summary>
        private string ApplyTypedFolder()
        {
            string typed = PathTextBox.Text?.Trim() ?? "";

            if (!string.IsNullOrEmpty(typed) && Path.IsPathRooted(typed) &&
                !string.Equals(typed, _config.DownloadFolder, StringComparison.OrdinalIgnoreCase))
            {
                _config.DownloadFolder = typed;
                PathTextBox.Text = typed;
                SettingsStore.Save(AppPaths.ConfigFile, _config);
            }

            return _config.DownloadFolder;
        }

        // =====================================================================
        // Обработчики кнопок
        // =====================================================================
        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
                UrlTextBox.Text = Clipboard.GetText();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new();
            if (dialog.ShowDialog() == true)
            {
                _config.DownloadFolder = dialog.FolderName;
                PathTextBox.Text = _config.DownloadFolder;
                SettingsStore.Save(AppPaths.ConfigFile, _config);
            }
        }

        private void PathTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyTypedFolder();

        private void DownloadButton_Click(object sender, RoutedEventArgs e) => StartDownloadFromUi();

        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            StartDownloadFromUi();
        }

        private void StartDownloadFromUi()
        {
            string url = UrlTextBox.Text.Trim();

            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Вставьте ссылку!", "Ссылка");
                return;
            }

            if (!VideoUtils.IsValidYouTubeUrl(url))
            {
                MessageBox.Show("Поддерживаются только ссылки YouTube!", "Ссылка");
                return;
            }

            UrlTextBox.Clear();
            ApplyTypedFolder();

            if (!EnqueueDownload(new DownloadRequest { Url = url }))
            {
                MessageBox.Show(
                    $"Очередь переполнена (максимум {MaxQueueLength}).\nДождитесь окончания текущих загрузок.",
                    "Очередь");
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessingQueue)
            {
                MessageBox.Show("Дождитесь окончания текущих загрузок.", "Обновление yt-dlp");
                return;
            }

            UpdateButton.IsEnabled = false;
            AppendLog("Скачивание новой версии yt-dlp...");

            try
            {
                string path = await YtDlpService.UpdateYtDlpAsync(AppendLog, _lifetime.Token);
                AppendLog("Файл: " + path);
                MessageBox.Show("yt-dlp обновлён!", "Успех");
            }
            catch (OperationCanceledException)
            {
                // Программа закрывается.
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Ошибка обновления yt-dlp: {ex.Message}");
                MessageBox.Show($"Не удалось обновить yt-dlp:\n\n{ex.Message}", "Ошибка");
            }
            finally
            {
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = AppStrings.Get(_config.Language)["UpdateYtdlpBtn"];
            }
        }

        private async void AppUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            AppUpdateButton.IsEnabled = false;

            try
            {
                AppendLog("Проверка обновлений программы...");
                UpdateInfo? update = await AppUpdateService.CheckAsync(CurrentVersion, _lifetime.Token);

                if (update == null)
                {
                    MessageBox.Show("У вас установлена последняя версия!", "Обновление");
                    return;
                }

                MessageBoxResult answer = MessageBox.Show(
                    $"Доступна новая версия {update.Version}!\n\n{update.Changelog}\n\nСкачать и обновить?",
                    "Обновление",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (answer != MessageBoxResult.Yes) return;

                await AppUpdateService.ApplyAsync(update, AppendLog, _lifetime.Token);
                ExitApplication();
            }
            catch (OperationCanceledException)
            {
                // Программа закрывается.
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка обновления программы: {ex.Message}");
                MessageBox.Show($"Не удалось обновить программу:\n\n{ex.Message}", "Ошибка обновления");
            }
            finally
            {
                AppUpdateButton.IsEnabled = true;
            }
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            // Применяем путь, введённый в поле, чтобы проверка папки была актуальной.
            ApplyTypedFolder();

            List<HistoryItem> history = HistoryStore.Load();

            if (history.Count == 0)
            {
                MessageBox.Show("История пуста.", "История");
                return;
            }

            HistoryWindow window = new(_config.Language)
            {
                Owner = this,
                AllowedFolder = _config.DownloadFolder
            };
            window.HistoryList.ItemsSource = history;
            window.ShowDialog();
        }

        private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult answer = MessageBox.Show(
                "Очистить историю скачиваний и удалить временные файлы (.part, куки)?",
                "Очистка",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;

            try
            {
                HistoryStore.Clear();
                YtDlpService.CleanupLeftoverCookies(AppendLog);

                foreach (string pattern in new[] { "cookies_temp_*.txt", "*.part" })
                {
                    foreach (string file in Directory.GetFiles(AppPaths.BaseDir, pattern))
                        TryDeleteFile(file);

                    if (Directory.Exists(_config.DownloadFolder))
                    {
                        foreach (string file in Directory.GetFiles(_config.DownloadFolder, pattern))
                            TryDeleteFile(file);
                    }
                }

                if (Directory.Exists(AppPaths.DataDir))
                {
                    foreach (string file in Directory.GetFiles(AppPaths.DataDir, "*.part"))
                        TryDeleteFile(file);
                }

                TryDeleteDirectory(Path.Combine(AppPaths.BaseDir, "update_temp"));
                TryDeleteFile(Path.Combine(AppPaths.BaseDir, "update.zip"));

                AppendLog("🧹 Кэш и история очищены.");
                MessageBox.Show("Очистка завершена!", "Успех");
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка очистки: {ex.Message}");
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
                // ignore
            }
        }

        private void StartupToggle_Changed(object sender, RoutedEventArgs e)
        {
            bool enable = StartupToggle.IsChecked == true;
            string? error = StartupManager.Set(enable);

            if (error != null)
            {
                AppendLog($"Ошибка автозапуска: {error}");
                return;
            }

            AppendLog(enable ? "✅ Автозапуск включён." : "❌ Автозапуск выключен.");
        }

        // =====================================================================
        // Буфер обмена
        // =====================================================================
        private void ClipboardTimer_Tick(object? sender, EventArgs e)
        {
            if (ClipboardToggle.IsChecked != true) return;

            try
            {
                if (!Clipboard.ContainsText()) return;

                string text = Clipboard.GetText().Trim();
                Match match = ClipboardUrlRegex.Match(text);

                if (!match.Success) return;

                string url = match.Value;

                if (url == _lastClipboardText || !string.IsNullOrEmpty(UrlTextBox.Text)) return;

                _lastClipboardText = url;
                UrlTextBox.Text = url;
                AppendLog("📋 Ссылка автоматически извлечена из буфера обмена.");
            }
            catch
            {
                // Буфер обмена может быть занят другим приложением.
            }
        }

        // =====================================================================
        // Окно
        // =====================================================================
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) Maximize_Click(sender, e);
            else DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => ExitApplication();

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (MaximizeButton != null)
                MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

            if (WindowState == WindowState.Minimized) Hide();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SettingsStore.Save(AppPaths.ConfigFile, _config);
            ExitApplication();
        }

        private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) => ShowWindow();

        private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            if (_isExiting) return;
            _isExiting = true;

            _lifetime.Cancel();
            YtDlpService.KillRunningProcesses();
            _clipboardTimer.Stop();
            _bridge?.Dispose();

            try
            {
                _notifyIcon?.Dispose();
            }
            catch
            {
                // ignore
            }

            Application.Current.Shutdown();
        }
    }
}
