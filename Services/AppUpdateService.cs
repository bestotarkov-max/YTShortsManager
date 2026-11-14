using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YTShortsManager.Models;

namespace YTShortsManager.Services
{
    /// <summary>Проверка и установка обновлений программы.</summary>
    public static class AppUpdateService
    {
        public const string VersionUrl =
            "https://raw.githubusercontent.com/bestotarkov-max/YTShortsManager/main/version.json";

        private const string ExeName = "YTShortsManager.exe";

        // Защита от zip-бомбы: даже доверенный архив не должен «взорваться» на диске.
        private const int MaxArchiveEntries = 20_000;
        private const long MaxUncompressedBytes = 2L * 1024 * 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly string[] TrustedHosts =
        {
            "github.com",
            "www.github.com",
            "objects.githubusercontent.com",
            "github-releases.githubusercontent.com",
            "release-assets.githubusercontent.com",
            "raw.githubusercontent.com"
        };

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClient client = new() { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("YTShortsManager-Updater");
            return client;
        }

        /// <returns>null, если обновлений нет.</returns>
        public static async Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
        {
            string json = await Http.GetStringAsync(VersionUrl, cancellationToken).ConfigureAwait(false);
            UpdateInfo? info = JsonSerializer.Deserialize<UpdateInfo>(json, JsonOptions);

            if (info == null || string.IsNullOrWhiteSpace(info.Version))
                throw new InvalidDataException("Некорректный version.json");

            if (!TryParseVersion(info.Version, out Version remoteVersion))
                throw new InvalidDataException($"Не удалось разобрать версию «{info.Version}».");

            if (string.IsNullOrWhiteSpace(info.DownloadUrl))
                throw new InvalidDataException("В version.json не указан download_url.");

            return remoteVersion > currentVersion ? info : null;
        }

        public static async Task ApplyAsync(UpdateInfo info, Action<string> log, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Ссылка на обновление должна быть HTTPS.");
            }

            if (Array.IndexOf(TrustedHosts, uri.Host.ToLowerInvariant()) < 0)
                throw new InvalidOperationException($"Недоверенный источник обновления: {uri.Host}");

            string baseDir = AppPaths.BaseDir;
            string zipPath = Path.Combine(baseDir, "update.zip");
            string tempPath = Path.Combine(baseDir, "update_temp");
            string scriptPath = Path.Combine(baseDir, "update.ps1");

            CleanupFile(zipPath);
            CleanupDirectory(tempPath);

            log("Скачивание новой версии программы (ZIP-архив)...");
            byte[] bytes = await Http.GetByteArrayAsync(info.DownloadUrl, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(zipPath, bytes, cancellationToken).ConfigureAwait(false);

            string expectedHash = (info.Sha256 ?? "").Trim().ToLowerInvariant();
            if (expectedHash.Length != 64 || !IsHex(expectedHash))
            {
                CleanupFile(zipPath);
                throw new InvalidDataException(
                    "В version.json нет корректного SHA-256. Обновление заблокировано: " +
                    "без хэша невозможно убедиться, что архив не подменён.");
            }

            log("Проверка целостности архива (SHA-256)...");
            string actualHash;
            await using (FileStream stream = File.OpenRead(zipPath))
            {
                byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken)
                    .ConfigureAwait(false);
                actualHash = Convert.ToHexString(hash).ToLowerInvariant();
            }

            if (!actualHash.Equals(expectedHash, StringComparison.Ordinal))
            {
                CleanupFile(zipPath);
                throw new InvalidDataException(
                    $"Хэш SHA-256 не совпадает! Архив повреждён или подменён.\nОжидалось: {expectedHash}\nПолучено: {actualHash}");
            }

            log("✅ Хэш совпадает.");

            log("Распаковка архива...");
            try
            {
                ExtractArchiveSafely(zipPath, tempPath);
            }
            catch
            {
                CleanupDirectory(tempPath);
                throw;
            }

            string? sourceRoot = FindUpdateSource(tempPath);
            if (sourceRoot == null)
            {
                CleanupDirectory(tempPath);
                CleanupFile(zipPath);
                throw new InvalidDataException(
                    $"В архиве обновления не найден {ExeName}. Обновление отменено, текущая версия не тронута.");
            }

            if (!string.Equals(sourceRoot, tempPath, StringComparison.OrdinalIgnoreCase))
                log($"ℹ️ Архив содержит вложенную папку «{Path.GetFileName(sourceRoot)}» — берём файлы из неё.");

            string exePath = Environment.ProcessPath ?? Path.Combine(baseDir, ExeName);

            File.WriteAllText(scriptPath,
                BuildUpdaterScript(baseDir, exePath, zipPath, tempPath, sourceRoot, Environment.ProcessId),
                new UTF8Encoding(true));

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" -ParentPid {Environment.ProcessId}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        /// <summary>
        /// Находит папку с файлами обновления. Архив, сжатый «папкой» (обычный ZIP из Проводника),
        /// содержит лишний верхний уровень — иначе новая версия оказалась бы внутри старой.
        /// </summary>
        private static string? FindUpdateSource(string extractedRoot)
        {
            if (File.Exists(Path.Combine(extractedRoot, ExeName)))
                return extractedRoot;

            try
            {
                // Ровно одна вложенная папка — типичный случай «ZIP с папкой внутри».
                string[] directories = Directory.GetDirectories(extractedRoot);
                if (directories.Length == 1 && File.Exists(Path.Combine(directories[0], ExeName)))
                    return directories[0];

                // На всякий случай — самый неглубокий найденный exe.
                string? best = null;
                foreach (string candidate in Directory.EnumerateFiles(extractedRoot, ExeName, SearchOption.AllDirectories))
                {
                    if (best == null || candidate.Length < best.Length) best = candidate;
                }

                return best == null ? null : Path.GetDirectoryName(best);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Проверяет, что архив не выходит за пределы папки и не является zip-бомбой.</summary>
        private static void ExtractArchiveSafely(string zipPath, string destination)
        {
            Directory.CreateDirectory(destination);

            string root = Path.GetFullPath(destination)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            using ZipArchive archive = ZipFile.OpenRead(zipPath);

            if (archive.Entries.Count > MaxArchiveEntries)
                throw new InvalidDataException(
                    $"В архиве обновления слишком много файлов ({archive.Entries.Count}).");

            long totalBytes = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                totalBytes += entry.Length;
                if (totalBytes > MaxUncompressedBytes)
                    throw new InvalidDataException("Архив обновления слишком большой после распаковки.");

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Некорректный путь в архиве: {entry.FullName}", ex);
                }

                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Небезопасный путь в архиве: {entry.FullName}");
            }

            archive.ExtractToDirectory(destination, overwriteFiles: true);
        }

        private static bool IsHex(string value)
        {
            foreach (char symbol in value)
            {
                if (!Uri.IsHexDigit(symbol)) return false;
            }

            return true;
        }

        private static string BuildUpdaterScript(string baseDir, string exePath, string zipPath,
            string tempPath, string sourceRoot, int parentPid)
        {
            static string Q(string value) => "'" + value.Replace("'", "''") + "'";

            return $@"
param([int]$ParentPid)

$ErrorActionPreference = 'Stop'
$exe = {Q(exePath)}
$src = {Q(sourceRoot)}
$dst = {Q(baseDir)}
$zip = {Q(zipPath)}
$temp = {Q(tempPath)}
$backup = $exe + '.old'
$logDir = Join-Path $env:LOCALAPPDATA 'YTShortsManager'
$log = Join-Path $logDir 'update.log'

function Write-Log([string]$message) {{
    try {{ Add-Content -LiteralPath $log -Value ('[' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '] ' + $message) -Encoding UTF8 }} catch {{ }}
}}

function Restore-OldVersion {{
    if (Test-Path -LiteralPath $backup) {{
        Remove-Item -LiteralPath $exe -Force -ErrorAction SilentlyContinue
        try {{ Rename-Item -LiteralPath $backup -NewName 'YTShortsManager.exe' -ErrorAction Stop }} catch {{ }}
        try {{ Start-Process -FilePath $exe -WorkingDirectory $dst -ErrorAction Stop }} catch {{ }}
    }}
}}

function Fail([string]$message) {{
    Write-Log ('ОШИБКА: ' + $message)
    Restore-OldVersion
    try {{
        $shell = New-Object -ComObject WScript.Shell
        $null = $shell.Popup(""Не удалось установить обновление.`n`n$message`n`nПодробности: $log"", 0, 'Ошибка обновления', 16)
    }} catch {{ }}
    exit 1
}}

try {{ New-Item -ItemType Directory -Path $logDir -Force | Out-Null }} catch {{ }}
Write-Log ('Начало установки. PID родителя: ' + $ParentPid)

# 1. Дожидаемся выхода текущей версии, чтобы не обновлять файлы под работающим процессом.
$waited = 0
if ($ParentPid -gt 0) {{
    while (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue) {{
        Start-Sleep -Milliseconds 500
        $waited += 500
        if ($waited -ge 120000) {{ Fail 'Текущая версия программы не завершилась за 2 минуты.' }}
    }}
}}

# 2. Отводим старый exe в сторону (переименование работает даже для запущенного файла).
if (Test-Path -LiteralPath $backup) {{ Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue }}

$renamed = $false
for ($i = 0; $i -lt 60; $i++) {{
    if (-not (Test-Path -LiteralPath $exe)) {{ $renamed = $true; break }}
    try {{
        Rename-Item -LiteralPath $exe -NewName 'YTShortsManager.exe.old' -ErrorAction Stop
        $renamed = $true
        break
    }} catch {{
        Start-Sleep -Milliseconds 500
    }}
}}
if (-not $renamed) {{ Fail 'Файл программы занят другим процессом (антивирус или вторая копия).' }}

# 3. Копируем новые файлы с повторами — диск и антивирус иногда блокируют запись.
$copied = $false
$lastError = ''
for ($i = 0; $i -lt 10; $i++) {{
    try {{
        Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force -ErrorAction Stop
        $copied = $true
        break
    }} catch {{
        $lastError = $_.Exception.Message
        Start-Sleep -Milliseconds 700
    }}
}}
if (-not $copied) {{ Fail ('Не удалось скопировать файлы обновления: ' + $lastError) }}
if (-not (Test-Path -LiteralPath $exe)) {{ Fail 'В архиве обновления не оказалось YTShortsManager.exe.' }}

# 4. Запускаем новую версию и убеждаемся, что она не упала сразу.
$started = $null
$lastError = ''
for ($i = 0; $i -lt 10; $i++) {{
    try {{
        $started = Start-Process -FilePath $exe -WorkingDirectory $dst -PassThru -ErrorAction Stop
        break
    }} catch {{
        $lastError = $_.Exception.Message
        Start-Sleep -Milliseconds 700
    }}
}}
if ($null -eq $started) {{ Fail ('Не удалось запустить новую версию: ' + $lastError) }}

Start-Sleep -Seconds 3
if ($started.HasExited) {{
    Fail ('Новая версия завершилась сразу после запуска, код ' + $started.ExitCode + '.')
}}

Write-Log 'Обновление установлено успешно.'

# 5. Убираем временные файлы и старую версию.
Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";
        }

        public static bool TryParseVersion(string? text, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(text)) return false;

            string clean = text.Trim().TrimStart('v', 'V');
            int dash = clean.IndexOf('-');
            if (dash >= 0) clean = clean[..dash];

            return Version.TryParse(clean, out version!);
        }

        private static void CleanupFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }

        private static void CleanupDirectory(string path)
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
    }
}
