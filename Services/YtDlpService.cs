using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YTShortsManager.Models;

namespace YTShortsManager.Services
{
    /// <summary>
    /// Обёртка над yt-dlp.exe: скачивание, получение длительности, безопасное удаление куки.
    /// </summary>
    public static class YtDlpService
    {
        public static readonly HashSet<string> AllowedQualities =
            new(StringComparer.OrdinalIgnoreCase) { "best", "2160", "1440", "1080", "720", "480", "360" };

        public static readonly HashSet<string> AllowedActions =
            new(StringComparer.OrdinalIgnoreCase) { "full", "trim" };

        private static readonly Regex ProgressRegex =
            new(@"\[download\]\s+(\d+(?:[.,]\d+)?)%", RegexOptions.Compiled);

        private static readonly Regex MergedFileRegex =
            new(@"\[Merger\]\s+Merging formats into ""(.+)""", RegexOptions.Compiled);

        private const string YtDlpDownloadUrl =
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        private static readonly List<Process> RunningProcesses = new();
        private static readonly object ProcessesGate = new();

        // =====================================================================
        // Скачивание
        // =====================================================================
        public static async Task<DownloadResult> DownloadAsync(
            DownloadRequest request,
            string outputFolder,
            Action<string> log,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            Normalize(request);

            if (!File.Exists(AppPaths.YtDlp))
                return DownloadResult.Fail("yt-dlp.exe не найден рядом с программой.", retryable: false);

            if (!VideoUtils.IsValidYouTubeUrl(request.Url))
                return DownloadResult.Fail("Недопустимый URL (разрешён только YouTube).", retryable: false);

            bool hasFfmpeg = File.Exists(AppPaths.Ffmpeg);
            bool hasDeno = File.Exists(AppPaths.Deno);

            if ((request.Action == "trim" || !request.AudioOnly) && !hasFfmpeg)
                log("⚠️ ffmpeg.exe не найден: склейка дорожек и обрезка могут не работать.");

            Directory.CreateDirectory(outputFolder);

            DateTime startedUtc = DateTime.UtcNow;
            string? cookieFile = null;
            Process? process = null;

            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = AppPaths.YtDlp,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                // Более предсказуемый вывод.
                psi.ArgumentList.Add("--no-color");
                psi.ArgumentList.Add("--newline");
                // --print включает "тихий" режим, поэтому прогресс нужно включить явно.
                psi.ArgumentList.Add("--progress");
                psi.ArgumentList.Add("--no-simulate");

                if (hasDeno)
                {
                    psi.ArgumentList.Add("--js-runtimes");
                    psi.ArgumentList.Add($"deno:{AppPaths.Deno}");
                }

                if (!request.Playlist) psi.ArgumentList.Add("--no-playlist");

                if (request.AudioOnly)
                {
                    psi.ArgumentList.Add("-x");
                    psi.ArgumentList.Add("--audio-format");
                    psi.ArgumentList.Add("mp3");
                    psi.ArgumentList.Add("--audio-quality");
                    psi.ArgumentList.Add("0");
                }
                else
                {
                    psi.ArgumentList.Add("-f");
                    psi.ArgumentList.Add(request.Quality == "best"
                        ? "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best"
                        : $"bestvideo[height<={request.Quality}][ext=mp4]+bestaudio[ext=m4a]/best[height<={request.Quality}][ext=mp4]/best");
                }

                if (request.Subs)
                {
                    psi.ArgumentList.Add("--write-subs");
                    psi.ArgumentList.Add("--write-auto-subs");
                    psi.ArgumentList.Add("--sub-langs");
                    psi.ArgumentList.Add("ru,en");
                    psi.ArgumentList.Add("--convert-subs");
                    psi.ArgumentList.Add("srt");
                }

                if (request.Thumb)
                {
                    psi.ArgumentList.Add("--write-thumbnail");
                    psi.ArgumentList.Add("--convert-thumbnails");
                    psi.ArgumentList.Add("jpg");
                }

                if (request.Sponsor)
                {
                    psi.ArgumentList.Add("--sponsorblock-remove");
                    psi.ArgumentList.Add("sponsor,intro,outro,selfpromo,interaction");
                }

                if (!string.IsNullOrWhiteSpace(request.CookiesData))
                {
                    cookieFile = AppPaths.NewTempCookieFile();
                    if (await WriteCookiesFileAsync(cookieFile, request.CookiesData, cancellationToken))
                    {
                        psi.ArgumentList.Add("--cookies");
                        psi.ArgumentList.Add(cookieFile);
                    }
                    else
                    {
                        // Битый или пустой список куки ломал бы загрузку целиком — лучше без них.
                        SafelyDeleteCookieFile(cookieFile);
                        cookieFile = null;
                        log("⚠️ Куки YouTube пусты или повреждены — продолжаю без них.");
                    }
                }

                if (request.Action == "trim" && hasFfmpeg)
                {
                    string end = request.End > 0 ? request.End.ToString(CultureInfo.InvariantCulture) : "inf";
                    psi.ArgumentList.Add("--download-sections");
                    psi.ArgumentList.Add($"*{request.Start}-{end}");
                }

                string outputTemplate = request.Playlist
                    ? "%(playlist_index)s - %(title)s.%(ext)s"
                    : "%(title)s.%(ext)s";
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add(Path.Combine(outputFolder, outputTemplate));

                if (hasFfmpeg)
                {
                    psi.ArgumentList.Add("--ffmpeg-location");
                    psi.ArgumentList.Add(AppPaths.Ffmpeg);
                }

                psi.ArgumentList.Add("--merge-output-format");
                psi.ArgumentList.Add("mp4");

                // Надёжный способ узнать итоговый путь файла (в т.ч. без склейки).
                psi.ArgumentList.Add("--print");
                psi.ArgumentList.Add("after_move:filepath");

                psi.ArgumentList.Add(request.Url);

                string? lastFile = null;
                List<string> errorLines = new();
                object stateLock = new();

                process = new Process { StartInfo = psi };

                process.OutputDataReceived += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    log(e.Data);

                    Match match = ProgressRegex.Match(e.Data);
                    if (match.Success)
                    {
                        string raw = match.Groups[1].Value.Replace(',', '.');
                        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
                            progress?.Report(percent);
                    }

                    Match merged = MergedFileRegex.Match(e.Data);
                    if (merged.Success)
                    {
                        lock (stateLock) lastFile = merged.Groups[1].Value;
                        return;
                    }

                    // Строка, напечатанная через --print after_move:filepath.
                    string line = e.Data.Trim();
                    if (line.Length > 3 && !line.StartsWith("[") && Path.IsPathRooted(line) && File.Exists(line))
                    {
                        lock (stateLock) lastFile = line;
                    }
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data) || IsFfmpegNoise(e.Data)) return;

                    log(e.Data);

                    if (e.Data.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (stateLock)
                        {
                            errorLines.Add(e.Data.Trim());
                            if (errorLines.Count > 5) errorLines.RemoveAt(0);
                        }
                    }
                };

                if (!process.Start())
                    return DownloadResult.Fail("Не удалось запустить yt-dlp.");

                RegisterProcess(process);

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                    // Гарантирует, что асинхронные обработчики вывода дочитали до конца.
                    process.WaitForExit();
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    throw;
                }

                if (process.ExitCode != 0)
                {
                    string detail;
                    lock (stateLock) detail = string.Join(" | ", errorLines);
                    log($"[yt-dlp] Процесс завершился с кодом {process.ExitCode}");
                    return DownloadResult.Fail(string.IsNullOrWhiteSpace(detail)
                        ? $"yt-dlp завершился с кодом {process.ExitCode}"
                        : detail);
                }

                string? filePath;
                lock (stateLock) filePath = lastFile;

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    filePath = FindNewestFile(outputFolder, startedUtc);

                return DownloadResult.Ok(filePath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return DownloadResult.Fail(ex.Message);
            }
            finally
            {
                if (process != null)
                {
                    UnregisterProcess(process);
                    process.Dispose();
                }

                if (cookieFile != null) SafelyDeleteCookieFile(cookieFile);
            }
        }

        // =====================================================================
        // Файл куки
        // =====================================================================

        private const string NetscapeHeader = "# Netscape HTTP Cookie File";

        /// <summary>
        /// yt-dlp понимает куки только в формате Netscape и только без BOM.
        /// Encoding.UTF8 при записи добавляет BOM (EF BB BF) — из-за него первая строка
        /// перестаёт считаться заголовком, и загрузка падает с
        /// "does not look like a Netscape format cookies file".
        /// Поэтому чистим содержимое и пишем UTF-8 без BOM.
        /// </summary>
        /// <returns>true, если в файле есть хотя бы одна строка с куки.</returns>
        private static async Task<bool> WriteCookiesFileAsync(
            string path,
            string cookiesData,
            CancellationToken cancellationToken)
        {
            (string content, bool hasCookies) = NormalizeCookiesFile(cookiesData);
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken);
            return hasCookies;
        }

        private static (string Content, bool HasCookies) NormalizeCookiesFile(string raw)
        {
            StringBuilder result = new();
            result.Append(NetscapeHeader).Append('\n');

            HashSet<string> seen = new(StringComparer.Ordinal);
            bool hasCookies = false;

            // BOM может прийти и в самом тексте (например, из буфера обмена).
            string cleaned = raw.Replace("\uFEFF", "").Replace("\r\n", "\n").Replace('\r', '\n');

            foreach (string rawLine in cleaned.Split('\n'))
            {
                string line = rawLine.TrimEnd(' ', '\t');
                if (line.Length == 0) continue;

                bool httpOnly = line.StartsWith("#HttpOnly_", StringComparison.Ordinal);

                // Заголовок и прочие комментарии Netscape нам не нужны — пишем свой.
                if (line[0] == '#' && !httpOnly) continue;

                // В Netscape-строке ровно 7 полей через табуляцию: domain, includeSub,
                // path, secure, expiry, name, value. Иначе yt-dlp отвергает весь файл.
                string[] fields = line.Split('\t');
                if (fields.Length < 7) continue;

                string domain = httpOnly ? fields[0]["#HttpOnly_".Length..] : fields[0];
                if (domain.Length == 0 || domain.Contains(' ')) continue;

                if (!seen.Add(line)) continue;

                result.Append(line).Append('\n');
                hasCookies = true;
            }

            return (hasCookies ? result.ToString() : "", hasCookies);
        }

        // =====================================================================
        // Длительность видео
        // =====================================================================
        public static async Task<int> GetDurationAsync(
            string url,
            string cookiesData,
            Action<string>? log,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(AppPaths.YtDlp) || !VideoUtils.IsValidYouTubeUrl(url)) return 0;

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
            CancellationToken token = timeoutCts.Token;

            string? cookieFile = null;
            Process? process = null;
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = AppPaths.YtDlp,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                psi.ArgumentList.Add("--no-playlist");
                psi.ArgumentList.Add("--no-warnings");
                psi.ArgumentList.Add("--no-color");
                psi.ArgumentList.Add("--skip-download");
                psi.ArgumentList.Add("--print");
                psi.ArgumentList.Add("%(duration)s");

                if (File.Exists(AppPaths.Deno))
                {
                    psi.ArgumentList.Add("--js-runtimes");
                    psi.ArgumentList.Add($"deno:{AppPaths.Deno}");
                }

                if (!string.IsNullOrWhiteSpace(cookiesData))
                {
                    cookieFile = AppPaths.NewTempCookieFile();
                    if (await WriteCookiesFileAsync(cookieFile, cookiesData, token))
                    {
                        psi.ArgumentList.Add("--cookies");
                        psi.ArgumentList.Add(cookieFile);
                    }
                    else
                    {
                        SafelyDeleteCookieFile(cookieFile);
                        cookieFile = null;
                    }
                }

                psi.ArgumentList.Add(url);

                process = new Process { StartInfo = psi };
                if (!process.Start()) return 0;
                RegisterProcess(process);

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(token);
                Task<string> stderrTask = process.StandardError.ReadToEndAsync(token);

                try
                {
                    await process.WaitForExitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);

                    // Дочитываем потоки, чтобы не оставлять "висящих" задач.
                    try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* ignore */ }

                    throw;
                }

                string stdout = await stdoutTask;
                string stderr = await stderrTask;

                string? value = stdout
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .FirstOrDefault(s => s.Length > 0);

                if (value != null &&
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
                    seconds > 0)
                {
                    return (int)Math.Round(seconds);
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                    log?.Invoke("[info] " + stderr.Trim());

                return 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (Exception ex)
            {
                log?.Invoke("[info] Не удалось получить длительность: " + ex.Message);
                return 0;
            }
            finally
            {
                if (process != null)
                {
                    UnregisterProcess(process);
                    process.Dispose();
                }

                if (cookieFile != null) SafelyDeleteCookieFile(cookieFile);
            }
        }

        // =====================================================================
        // Обновление yt-dlp
        // =====================================================================
        public static async Task<string> UpdateYtDlpAsync(Action<string> log, CancellationToken cancellationToken)
        {
            string newExe = Path.Combine(AppPaths.BaseDir, "yt-dlp-new.exe");
            string backup = AppPaths.YtDlp + ".bak";

            using HttpClient client = new() { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("YTShortsManager");

            byte[] bytes = await client.GetByteArrayAsync(YtDlpDownloadUrl, cancellationToken);
            if (bytes.Length < 1_000_000)
                throw new InvalidDataException("Скачанный файл yt-dlp повреждён (слишком маленький).");

            await File.WriteAllBytesAsync(newExe, bytes, cancellationToken);

            try
            {
                if (File.Exists(backup)) File.Delete(backup);

                if (File.Exists(AppPaths.YtDlp))
                    File.Move(AppPaths.YtDlp, backup);

                try
                {
                    File.Move(newExe, AppPaths.YtDlp);
                }
                catch
                {
                    // Откатываемся на рабочую версию.
                    if (File.Exists(backup)) File.Move(backup, AppPaths.YtDlp);
                    throw;
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(backup)) File.Delete(backup);
                    if (File.Exists(newExe)) File.Delete(newExe);
                }
                catch
                {
                    // ignore
                }
            }

            log("✅ yt-dlp успешно обновлён.");
            return AppPaths.YtDlp;
        }

        // =====================================================================
        // Вспомогательное
        // =====================================================================
        private static void Normalize(DownloadRequest request)
        {
            request.Url = (request.Url ?? "").Trim();
            request.CookiesData ??= "";
            if (!AllowedActions.Contains(request.Action)) request.Action = "full";
            if (!AllowedQualities.Contains(request.Quality)) request.Quality = "best";
            if (request.Start < 0) request.Start = 0;
            if (request.End < 0) request.End = 0;

            // "trim" с концом раньше начала — бессмыслен, считаем обычным скачиванием.
            if (request.Action == "trim" && request.End > 0 && request.End <= request.Start)
            {
                request.Action = "full";
                request.Start = 0;
                request.End = 0;
            }
        }

        private static bool IsFfmpegNoise(string line) =>
            line.Contains("frame=") || line.Contains("Stream mapping:") ||
            line.Contains("Metadata:") || line.Contains("Duration:") ||
            line.StartsWith("  Stream #") || line.StartsWith("Input #") ||
            line.StartsWith("Output #") || line.Contains("Press [q]") ||
            line.Contains("encoder         :");

        private static string? FindNewestFile(string folder, DateTime sinceUtc)
        {
            try
            {
                return new DirectoryInfo(folder)
                    .EnumerateFiles()
                    .Where(f => f.LastWriteTimeUtc >= sinceUtc.AddSeconds(-2))
                    .Where(f => !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Процесс уже завершился.
            }
        }

        private static void RegisterProcess(Process process)
        {
            lock (ProcessesGate) RunningProcesses.Add(process);
        }

        private static void UnregisterProcess(Process process)
        {
            lock (ProcessesGate) RunningProcesses.Remove(process);
        }

        /// <summary>
        /// Завершает все запущенные yt-dlp. Вызывается при выходе из программы,
        /// чтобы не оставлять «осиротевшие» процессы, качающие видео в фоне.
        /// </summary>
        public static void KillRunningProcesses()
        {
            Process[] snapshot;
            lock (ProcessesGate) snapshot = RunningProcesses.ToArray();

            foreach (Process process in snapshot) TryKill(process);
        }

        /// <summary>
        /// Удаляет cookie-файлы, оставшиеся после аварийного завершения (падение, выключение ПК).
        /// Заодно чистит «старые» файлы, которые прошлые версии писали рядом с программой.
        /// </summary>
        public static void CleanupLeftoverCookies(Action<string>? log = null)
        {
            try
            {
                if (Directory.Exists(AppPaths.DataDir))
                {
                    foreach (string file in Directory.EnumerateFiles(AppPaths.DataDir, "cookies_*.txt"))
                        SafelyDeleteCookieFile(file);
                }

                // Наследие старых версий: куки лежали в папке программы.
                foreach (string pattern in new[] { "cookies.txt", "cookies_temp*.txt" })
                {
                    foreach (string file in Directory.EnumerateFiles(AppPaths.BaseDir, pattern))
                        SafelyDeleteCookieFile(file);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("⚠️ Не удалось очистить старые файлы куки: " + ex.Message);
            }
        }

        /// <summary>Затирает и удаляет файл с куки.</summary>
        public static void SafelyDeleteCookieFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;

                try
                {
                    long length = new FileInfo(path).Length;
                    if (length is > 0 and < 10_000_000)
                    {
                        using FileStream fs = new(path, FileMode.Open, FileAccess.Write);
                        fs.Write(new byte[length], 0, (int)length);
                        fs.Flush(true);
                    }
                }
                catch
                {
                    // Если файл занят — просто удалим ниже.
                }

                File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }
}
