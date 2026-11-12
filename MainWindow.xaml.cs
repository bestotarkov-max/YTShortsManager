using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Hardcodet.Wpf.TaskbarNotification;

namespace YTShortsManager
{
    public class DownloadRequest
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("action")] public string Action { get; set; } = "full";
        [JsonPropertyName("start")] public int Start { get; set; } = 0;
        [JsonPropertyName("end")] public int End { get; set; } = 0;
        [JsonPropertyName("quality")] public string Quality { get; set; } = "best";
        [JsonPropertyName("audio_only")] public bool AudioOnly { get; set; } = false;
        [JsonPropertyName("subs")] public bool Subs { get; set; } = false;
        [JsonPropertyName("thumb")] public bool Thumb { get; set; } = false;
        [JsonPropertyName("sponsor")] public bool Sponsor { get; set; } = false;
        [JsonPropertyName("playlist")] public bool Playlist { get; set; } = false;
        [JsonPropertyName("cookies_data")] public string CookiesData { get; set; } = "";
    }

    public class AppConfig 
    { 
        public string DownloadFolder { get; set; } = ""; 
        public string Language { get; set; } = "RU"; 
    }
    
    public class HistoryItem
    {
        public string Title { get; set; } = "";
        public string Path { get; set; } = "";
        public string Time { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
    }

    public class UpdateInfo
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
        [JsonPropertyName("changelog")] public string Changelog { get; set; } = "";
    }

    public partial class MainWindow : Window
    {
        private TaskbarIcon? notifyIcon;
        private string baseDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        
        private HttpListener? httpListener; // Вынесено для корректного закрытия

        private string ytdlpPath = "";
        private string ffmpegPath = "";
        private string denoPath = "";
        private string configPath = "";
        private string historyPath = "";
        private string logPath = "";
        private string downloadFolder = "";

        private DispatcherTimer? clipboardTimer;
        private string lastClipboardText = "";

        private Queue<DownloadRequest> downloadQueue = new Queue<DownloadRequest>();
        private ObservableCollection<QueueItem> queueUI = new ObservableCollection<QueueItem>();
        private bool isProcessingQueue = false;
        private readonly object queueLock = new object();

        private string CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        private const string UpdateCheckUrl = "https://raw.githubusercontent.com/bestotarkov-max/YTShortsManager/main/version.json";

        private Dictionary<string, Dictionary<string, string>> Locales = new Dictionary<string, Dictionary<string, string>>
        {
            { "RU", new Dictionary<string, string> {
                { "DownloadBtn", "СКАЧАТЬ ВИДЕО" },
                { "PasteBtn", "Вставить" },
                { "FolderLabel", "Папка:" },
                { "BrowseBtn", "Обзор" },
                { "HistoryBtn", "📜 История" },
                { "ClearCacheBtn", "🧹 Очистить кэш" },
                { "UpdateYtdlpBtn", "⬆️ Обновить yt-dlp" },
                { "UpdateAppBtn", "📦 Обновить программу" },
                { "ClipboardToggle", "Авто-вставка" },
                { "StartupToggle", "Запускать с Windows" },
                { "ProgressText", "0%" },
                { "LangLabel", "Язык / Language:" }
            }},
            { "EN", new Dictionary<string, string> {
                { "DownloadBtn", "DOWNLOAD VIDEO" },
                { "PasteBtn", "Paste" },
                { "FolderLabel", "Folder:" },
                { "BrowseBtn", "Browse" },
                { "HistoryBtn", "📜 History" },
                { "ClearCacheBtn", "🧹 Clear Cache" },
                { "UpdateYtdlpBtn", "⬆️ Update yt-dlp" },
                { "UpdateAppBtn", "📦 Update App" },
                { "ClipboardToggle", "Auto-paste" },
                { "StartupToggle", "Run on Windows Startup" },
                { "ProgressText", "0%" },
                { "LangLabel", "Language / Язык:" }
            }}
        };
        private string currentLang = "RU";

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                
                notifyIcon = FindResource("MyNotifyIcon") as TaskbarIcon;

                ytdlpPath = Path.Combine(baseDir, "yt-dlp.exe");
                ffmpegPath = Path.Combine(baseDir, "ffmpeg.exe");
                denoPath = Path.Combine(baseDir, "deno.exe");
                configPath = Path.Combine(baseDir, "config.json");
                historyPath = Path.Combine(baseDir, "history.json");
                logPath = Path.Combine(baseDir, "app.log");

                if (QueueList != null) QueueList.ItemsSource = queueUI; 

                LoadConfig();
                PathTextBox.Text = downloadFolder;
                
                LangCombo.SelectedIndex = currentLang == "EN" ? 1 : 0;
                ApplyLanguage(currentLang);

                StartupToggle.IsChecked = IsStartupEnabled();
                StartupToggle.Checked += (s, e) => SetStartup(true);
                StartupToggle.Unchecked += (s, e) => SetStartup(false);

                try
                {
                    if (notifyIcon != null)
                    {
                        Uri iconUri = new Uri("pack://application:,,,/icon.ico");
                        this.Icon = new BitmapImage(iconUri);
                        var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
                        if (streamInfo != null) notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                    }
                }
                catch { if(notifyIcon != null) notifyIcon.Icon = System.Drawing.SystemIcons.Application; }
                
                StartSmartClipboard();
                StartHttpServer();
                CheckDependencies();
                AppendLog("Программа запущена. Версия " + CurrentVersion);
            }
            catch (Exception ex) { MessageBox.Show($"Критическая ошибка при запуске: {ex.Message}"); }
        }

        private void ApplyLanguage(string lang)
        {
            if (DownloadButton == null) return; 

            currentLang = lang;
            var t = Locales[lang];

            DownloadButton.Content = t["DownloadBtn"];
            PasteButton.Content = t["PasteBtn"];
            FolderLabel.Text = t["FolderLabel"];
            BrowseButton.Content = t["BrowseBtn"];
            HistoryButton.Content = t["HistoryBtn"];
            ClearCacheButton.Content = t["ClearCacheBtn"];
            UpdateButton.Content = t["UpdateYtdlpBtn"];
            AppUpdateButton.Content = t["UpdateAppBtn"];
            ClipboardToggle.Content = t["ClipboardToggle"];
            StartupToggle.Content = t["StartupToggle"];
            LangLabel.Text = t["LangLabel"];
        }

        private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LangCombo.SelectedItem is ComboBoxItem selectedItem)
            {
                string lang = selectedItem.Content.ToString() ?? "RU";
                ApplyLanguage(lang);
                SaveConfig(); 
            }
        }

        private void AppendLog(string message)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            
            Dispatcher.Invoke(() => {
                if (LogTextBox != null)
                {
                    LogTextBox.AppendText(logEntry + "\n");
                    LogTextBox.ScrollToEnd();
                }
            });

            try { File.AppendAllText(logPath, logEntry + Environment.NewLine); } catch { }
        }

        private void CheckDependencies()
        {
            List<string> missingFiles = new List<string>();

            if (!File.Exists(ytdlpPath)) missingFiles.Add("yt-dlp.exe");
            if (!File.Exists(ffmpegPath)) missingFiles.Add("ffmpeg.exe");
            if (!File.Exists(denoPath)) missingFiles.Add("deno.exe");

            if (missingFiles.Count > 0)
            {
                string msg = "ВНИМАНИЕ! В папке с программой не хватает важных файлов:\n\n" + 
                             string.Join("\n", missingFiles) + 
                             "\n\nБез них скачивание видео может работать неправильно.";
                
                MessageBox.Show(msg, "Отсутствуют зависимости", MessageBoxButton.OK, MessageBoxImage.Warning);
                AppendLog("⚠️ ВНИМАНИЕ: Обнаружены отсутствующие зависимости!");
            }
            else
            {
                AppendLog("✅ Все зависимости на месте.");
            }
        }

        private async void AppUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            AppUpdateButton.IsEnabled = false;
            AppendLog("Проверка обновлений программы...");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "YTShortsManager");
                    
                    string json = await client.GetStringAsync(UpdateCheckUrl);
                    var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json);

                    if (updateInfo != null && new Version(updateInfo.Version) > new Version(CurrentVersion))
                    {
                        var result = MessageBox.Show($"Доступна новая версия {updateInfo.Version}!\n\n{updateInfo.Changelog}\n\nСкачать и обновить?", "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            await DownloadAndUpdate(updateInfo.DownloadUrl);
                        }
                    }
                    else
                    {
                        MessageBox.Show("У вас установлена последняя версия!", "Обновление");
                    }
                }
            }
            catch (Exception ex) { AppendLog($"Ошибка проверки обновлений: {ex.Message}"); }
            finally { AppUpdateButton.IsEnabled = true; }
        }

        private async Task DownloadAndUpdate(string downloadUrl)
        {
            string zipPath = Path.Combine(baseDir, "update.zip");
            string extractPath = Path.Combine(baseDir, "update_temp");
            AppendLog("Скачивание новой версии программы (ZIP-архив)...");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    byte[] bytes = await client.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(zipPath, bytes);
                }

                AppendLog("Распаковка архива...");
                
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                string batPath = Path.Combine(baseDir, "update.bat");
                string currentExe = Environment.ProcessPath ?? Path.Combine(baseDir, "YTShortsManager.exe");
                
                string batContent = $@"
@echo off
timeout /t 3 /nobreak >nul

:wait_unlock
del ""{currentExe}"" /F /Q >nul 2>&1
if exist ""{currentExe}"" (
    timeout /t 1 /nobreak >nul
    goto wait_unlock
)

xcopy ""{extractPath}\*"" ""{baseDir}"" /E /I /Y /Q
start """" ""{currentExe}""
rmdir /s /q ""{extractPath}""
del ""{zipPath}""
del ""%~f0""
";
                File.WriteAllText(batPath, batContent);

                Process.Start(new ProcessStartInfo { FileName = batPath, UseShellExecute = true, CreateNoWindow = true });
                
                Application.Current.Shutdown();
                Environment.Exit(0); 
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка обновления: {ex.Message}");
                MessageBox.Show("Не удалось скачать или распаковать обновление. Проверьте логи.");
            }
        }

        private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Вы уверены, что хотите очистить историю скачиваний и удалить временные файлы (.part)?", "Очистка", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (File.Exists(historyPath)) File.Delete(historyPath);

                if (Directory.Exists(downloadFolder))
                {
                    var partFiles = Directory.GetFiles(downloadFolder, "*.part", SearchOption.TopDirectoryOnly);
                    foreach (var file in partFiles)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }

                AppendLog("🧹 Кэш и история успешно очищены.");
                MessageBox.Show("Очистка завершена!", "Успех");
            }
            catch (Exception ex) { AppendLog($"Ошибка очистки: {ex.Message}"); }
        }

        private void EnqueueDownload(DownloadRequest req)
        {
            var uiItem = new QueueItem { Url = req.Url, Title = "Загрузка данных...", Status = "В очереди" };
            
            Dispatcher.Invoke(() => queueUI.Add(uiItem));

            lock (queueLock)
            {
                downloadQueue.Enqueue(req);
                AppendLog($"➕ Добавлено в очередь. Всего: {downloadQueue.Count}");
            }

            if (!isProcessingQueue)
            {
                isProcessingQueue = true;
                Task.Run(() => ProcessQueueAsync());
            }
        }

        private async Task ProcessQueueAsync()
        {
            while (true)
            {
                DownloadRequest? req = null;
                QueueItem? currentUiItem = null;

                lock (queueLock)
                {
                    if (downloadQueue.Count == 0)
                    {
                        isProcessingQueue = false;
                        Dispatcher.Invoke(() => { DownloadProgress.Value = 0; ProgressText.Text = "Готово"; });
                        break;
                    }
                    req = downloadQueue.Dequeue();
                }

                Dispatcher.Invoke(() => {
                    currentUiItem = queueUI.FirstOrDefault(x => x.Status == "В очереди");
                    if (currentUiItem != null)
                    {
                        currentUiItem.Status = "Скачивается...";
                        currentUiItem.Title = "Ссылка: " + req.Url; 
                    }
                    DownloadProgress.Value = 0; 
                    ProgressText.Text = "0%"; 
                    AppendLog($"▶ Начинаю скачивание: {req.Url}"); 
                });

                if (req != null)
                {
                    await Task.Run(() => RunYtDlpWithRetry(req));
                }

                Dispatcher.Invoke(() => {
                    if (currentUiItem != null) queueUI.Remove(currentUiItem);
                });
            }
        }

        private void RunYtDlpWithRetry(DownloadRequest req)
        {
            int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    RunYtDlp(req);
                    break; 
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        AppendLog($"❌ Все {maxRetries} попытки провалились для {req.Url}. Пропускаю.");
                        break;
                    }
                    AppendLog($"⚠️ Ошибка: {ex.Message}. Повторная попытка {attempt}/{maxRetries} через 5 сек...");
                    Thread.Sleep(5000);
                }
            }
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key?.GetValue("YTShortsManager") != null;
                }
            }
            catch { return false; }
        }

        private void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    
                    if (enable)
                    {
                        string? exePath = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exePath)) key.SetValue("YTShortsManager", $"\"{exePath}\"");
                        AppendLog("✅ Автозапуск включен.");
                    }
                    else
                    {
                        key.DeleteValue("YTShortsManager", false);
                        AppendLog("❌ Автозапуск выключен.");
                    }
                }
            }
            catch (Exception ex) { AppendLog($"Ошибка автозапуска: {ex.Message}"); }
        }

        private void StartSmartClipboard()
        {
            clipboardTimer = new DispatcherTimer();
            clipboardTimer.Interval = TimeSpan.FromSeconds(1.5);
            clipboardTimer.Tick += ClipboardTimer_Tick;
            clipboardTimer.Start();
        }

        private void ClipboardTimer_Tick(object? sender, EventArgs e)
        {
            if (ClipboardToggle.IsChecked != true) return;

            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText().Trim();
                    
                    var urlMatch = Regex.Match(text, @"(https?:\/\/(?:www\.)?(?:youtube\.com|youtu\.be)\/[^\s]+)");
                    
                    if (urlMatch.Success)
                    {
                        string cleanUrl = urlMatch.Value; 

                        if (cleanUrl != lastClipboardText && string.IsNullOrEmpty(UrlTextBox.Text))
                        {
                            lastClipboardText = cleanUrl;
                            UrlTextBox.Text = cleanUrl;
                            AppendLog("📋 Ссылка автоматически извлечена из буфера обмена.");
                        }
                    }
                }
            }
            catch { }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) Maximize_Click(sender, e);
            else this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized) this.WindowState = WindowState.Normal;
            else this.WindowState = WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e) { this.Close(); }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (!string.IsNullOrEmpty(cfg?.DownloadFolder)) downloadFolder = cfg.DownloadFolder;
                    if (!string.IsNullOrEmpty(cfg?.Language)) currentLang = cfg.Language;
                }
            } catch { }
            if (string.IsNullOrEmpty(downloadFolder))
                downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "YT_Shorts");
        }

        private void SaveConfig()
        {
            try { File.WriteAllText(configPath, JsonSerializer.Serialize(new AppConfig { DownloadFolder = downloadFolder, Language = currentLang })); } catch { }
        }

        private void Window_StateChanged(object sender, EventArgs e) 
        { 
            if (WindowState == WindowState.Minimized) this.Hide(); 
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) 
        { 
            try { httpListener?.Stop(); httpListener?.Close(); } catch { }
            notifyIcon?.Dispose(); 
            Application.Current.Shutdown(); 
        }

        private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) { this.Show(); this.WindowState = WindowState.Normal; }
        
        private void Exit_Click(object sender, RoutedEventArgs e) 
        { 
            try { httpListener?.Stop(); httpListener?.Close(); } catch { }
            notifyIcon?.Dispose(); 
            Application.Current.Shutdown(); 
        }
        
        private void PasteButton_Click(object sender, RoutedEventArgs e) { if (Clipboard.ContainsText()) UrlTextBox.Text = Clipboard.GetText(); }
        
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) { downloadFolder = dialog.FolderName; PathTextBox.Text = downloadFolder; SaveConfig(); }
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) { MessageBox.Show("Вставьте ссылку!"); return; }

            UrlTextBox.Clear();
            var req = new DownloadRequest { Url = url };
            EnqueueDownload(req);
        }

        private void AddToHistory(string title, string path, string videoUrl)
        {
            try
            {
                List<HistoryItem> history = new List<HistoryItem>();
                if (File.Exists(historyPath))
                {
                    var json = File.ReadAllText(historyPath);
                    history = JsonSerializer.Deserialize<List<HistoryItem>>(json) ?? new List<HistoryItem>();
                }

                string videoId = VideoUtils.ExtractVideoId(videoUrl);
                string thumbUrl = !string.IsNullOrEmpty(videoId) ? $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg" : "";

                history.Insert(0, new HistoryItem { 
                    Title = title, Path = path, Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ThumbnailUrl = thumbUrl
                });
                
                if (history.Count > 50) history.RemoveAt(history.Count - 1);
                File.WriteAllText(historyPath, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
            } catch { }
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(historyPath)) { MessageBox.Show("История пуста."); return; }
            
            try
            {
                List<HistoryItem>? history = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(historyPath));
                HistoryWindow historyWindow = new HistoryWindow();
                historyWindow.Owner = this; 
                if (history != null) historyWindow.HistoryList.ItemsSource = history; 
                historyWindow.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка чтения истории: " + ex.Message); }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateButton.IsEnabled = false;
            UpdateButton.Content = "Обновление...";
            AppendLog("Скачивание новой версии yt-dlp...");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string newExe = Path.Combine(baseDir, "yt-dlp-new.exe");
                    byte[] fileBytes = await client.GetByteArrayAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
                    await File.WriteAllBytesAsync(newExe, fileBytes);
                    
                    if (File.Exists(ytdlpPath)) File.Delete(ytdlpPath);
                    File.Move(newExe, ytdlpPath);
                    
                    AppendLog("✅ yt-dlp успешно обновлен!");
                    MessageBox.Show("yt-dlp обновлен!", "Успех");
                }
            }
            catch (Exception ex) { AppendLog($"❌ Ошибка обновления: {ex.Message}"); }
            
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = Locales[currentLang]["UpdateYtdlpBtn"];
        }

        private string GetVideoDuration(string url, string cookiesData)
        {
            if (!File.Exists(ytdlpPath)) return "0";
            try
            {
                string args = $"--skip-download --print \"%(duration)s\" \"{url}\"";
                
                if (!string.IsNullOrEmpty(cookiesData))
                {
                    string tempCookieFile = Path.Combine(baseDir, "cookies_temp.txt");
                    File.WriteAllText(tempCookieFile, cookiesData);
                    args += $" --cookies \"{tempCookieFile}\"";
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ytdlpPath, Arguments = args,
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
                };
                
                using (Process? p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd().Trim(); 
                        p.WaitForExit();
                        if (int.TryParse(output, out int res)) return output;
                    }
                }
            } catch { }
            return "0";
        }

        private void RunYtDlp(DownloadRequest req)
        {
            if (!File.Exists(ytdlpPath)) { throw new FileNotFoundException("yt-dlp.exe не найден!"); }
            
            bool hasFfmpeg = File.Exists(ffmpegPath);
            bool hasDeno = File.Exists(denoPath);

            Directory.CreateDirectory(downloadFolder);
            string args = "";

            if (hasDeno) args += $"--js-runtimes deno:\"{denoPath}\" ";
            if (!req.Playlist) args += "--no-playlist ";
            
            if (req.AudioOnly) args += "-x --audio-format mp3 --audio-quality 0 ";
            else if (req.Quality == "best") args += "-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\" ";
            else args += $"-f \"bestvideo[height<={req.Quality}][ext=mp4]+bestaudio[ext=m4a]/best[height<={req.Quality}][ext=mp4]/best\" ";

            if (req.Subs) args += "--write-subs --write-auto-subs --sub-langs \"ru,en\" --convert-subs srt ";
            if (req.Thumb) args += "--write-thumbnail --convert-thumbnails jpg ";
            if (req.Sponsor) args += "--sponsorblock-remove sponsor,intro,outro,selfpromo,interaction ";

            if (!string.IsNullOrEmpty(req.CookiesData))
            {
                string cookieFile = Path.Combine(baseDir, "cookies.txt");
                File.WriteAllText(cookieFile, req.CookiesData);
                args += $"--cookies \"{cookieFile}\" ";
            }

            if (req.Action == "trim" && req.End > 0) args += $"--download-sections \"*{req.Start}-{req.End}\" ";

            string outtmpl = req.Playlist ? "%(playlist_index)s - %(title)s.%(ext)s" : "%(title)s.%(ext)s";
            args += $"-o \"{Path.Combine(downloadFolder, outtmpl)}\" ";
            
            if (hasFfmpeg) args += $"--ffmpeg-location \"{ffmpegPath}\" ";
            
            args += $"--merge-output-format mp4 \"{req.Url}\"";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ytdlpPath, Arguments = args,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };

            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            string lastFilePath = "";
            Regex progressRegex = new Regex(@"\[download\]\s+(\d+\.?\d*)%");
            Regex mergerRegex = new Regex(@"\[Merger\] Merging formats into ""(.+)""");
            bool hasError = false;

            using (Process? process = new Process { StartInfo = psi })
            {
                if (process == null) throw new Exception("Не удалось запустить yt-dlp");

                process.OutputDataReceived += (s, ev) => {
                    if (string.IsNullOrEmpty(ev.Data)) return;
                    Dispatcher.Invoke(() => AppendLog(ev.Data)); 

                    var match = progressRegex.Match(ev.Data);
                    if (match.Success && double.TryParse(match.Groups[1].Value, out double percent))
                    {
                        Dispatcher.Invoke(() => { DownloadProgress.Value = percent; ProgressText.Text = $"{percent:F1}%"; });
                    }

                    var mMatch = mergerRegex.Match(ev.Data);
                    if (mMatch.Success) lastFilePath = mMatch.Groups[1].Value;
                };
                
                process.ErrorDataReceived += (s, ev) => {
                    if (!string.IsNullOrEmpty(ev.Data)) 
                    {
                        if (ev.Data.Contains("frame=") || 
                            ev.Data.Contains("Stream mapping:") || 
                            ev.Data.Contains("Metadata:") || 
                            ev.Data.Contains("Duration:") || 
                            ev.Data.StartsWith("  Stream #") || 
                            ev.Data.StartsWith("Input #") || 
                            ev.Data.StartsWith("Output #") ||
                            ev.Data.Contains("Press [q]") ||
                            ev.Data.Contains("encoder         :")) 
                        {
                            return; 
                        }

                        Dispatcher.Invoke(() => AppendLog(ev.Data));
                        if (ev.Data.Contains("ERROR:")) hasError = true; 
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0) 
                {
                    hasError = true;
                    Dispatcher.Invoke(() => AppendLog($"[yt-dlp] Процесс завершился с кодом {process.ExitCode}"));
                }
            }

            if (hasError) throw new Exception("Сбой yt-dlp (см. логи).");

            Dispatcher.Invoke(() => {
                AppendLog("✅ Процесс завершен!");
                DownloadProgress.Value = 100;
                ProgressText.Text = "100%";
                
                if (!string.IsNullOrEmpty(lastFilePath))
                    AddToHistory(Path.GetFileName(lastFilePath), lastFilePath, req.Url);

                notifyIcon?.ShowBalloonTip("Загрузка завершена", "Видео успешно скачано!", BalloonIcon.Info);
            });
        }

        private async void StartHttpServer()
        {
            await Task.Run(() =>
            {
                httpListener = new HttpListener();
                httpListener.Prefixes.Add("http://127.0.0.1:5000/");
                try
                {
                    httpListener.Start();
                    Dispatcher.Invoke(() => AppendLog("Сервер для расширения запущен на порту 5000."));

                    while (true)
                    {
                        HttpListenerContext context = httpListener.GetContext();
                        HttpListenerRequest request = context.Request;
                        
                        string origin = request.Headers["Origin"];
                        if (string.IsNullOrEmpty(origin) || 
                            (!origin.StartsWith("chrome-extension://") && !origin.StartsWith("moz-extension://")))
                        {
                            context.Response.StatusCode = 403;
                            context.Response.Close();
                            continue;
                        }

                        string responseJson = "{\"status\": \"success\"}";
                        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                        context.Response.ContentType = "application/json";

                        if (request.HttpMethod == "POST")
                        {
                            using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
                            {
                                string body = reader.ReadToEnd();
                                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                                if (request.Url.AbsolutePath == "/download")
                                {
                                    try {
                                        var req = JsonSerializer.Deserialize<DownloadRequest>(body, options);
                                        if (req != null)
                                        {
                                            Dispatcher.Invoke(() => AppendLog($"Запрос из браузера: {req.Url}"));
                                            EnqueueDownload(req);
                                        }
                                    } catch (Exception ex) { Dispatcher.Invoke(() => AppendLog($"Ошибка парсинга: {ex.Message}")); }
                                }
                                else if (request.Url.AbsolutePath == "/get_info")
                                {
                                    try
                                    {
                                        var req = JsonSerializer.Deserialize<DownloadRequest>(body, options);
                                        if (req != null)
                                        {
                                            string duration = GetVideoDuration(req.Url, req.CookiesData); 
                                            responseJson = $"{{\"status\": \"success\", \"duration\": {duration}}}";
                                        }
                                    }
                                    catch { responseJson = "{\"status\": \"error\"}"; }
                                }
                            }
                        }

                        byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.Close();
                    }
                }
                catch (Exception ex) 
                { 
                    if (ex is HttpListenerException && ex.Message.Contains("aborted")) return;
                    Dispatcher.Invoke(() => AppendLog("Ошибка сервера: " + ex.Message)); 
                }
            });
        }
    }
}