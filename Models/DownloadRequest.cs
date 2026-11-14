using System.Text.Json.Serialization;

namespace YTShortsManager.Models
{
    /// <summary>
    /// Запрос на скачивание. Приходит из расширения (JSON) или создаётся в окне программы.
    /// </summary>
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
}
