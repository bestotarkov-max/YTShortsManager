using System.Text.Json.Serialization;

namespace YTShortsManager.Models
{
    public class UpdateInfo
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("changelog")] public string Changelog { get; set; } = "";
    }
}
