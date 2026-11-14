namespace YTShortsManager.Models
{
    public sealed class DownloadResult
    {
        public bool Success { get; init; }
        public string? FilePath { get; init; }
        public string? Error { get; init; }

        /// <summary>false — повторять попытку бессмысленно (например, нет yt-dlp.exe).</summary>
        public bool Retryable { get; init; } = true;

        public static DownloadResult Ok(string? filePath) => new() { Success = true, FilePath = filePath };
        public static DownloadResult Fail(string error, bool retryable = true) =>
            new() { Success = false, Error = error, Retryable = retryable };
    }
}
