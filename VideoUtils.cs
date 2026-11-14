using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace YTShortsManager
{
    public static class VideoUtils
    {
        private const int MaxUrlLength = 2048;

        private static readonly Regex VideoIdRegex = new(
            @"(?:youtu\.be/|youtube\.com/(?:embed/|v/|watch\?v=|watch\?.+&v=|shorts/|live/))([\w-]{11})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "youtube.com",
            "www.youtube.com",
            "m.youtube.com",
            "music.youtube.com",
            "youtu.be",
            "youtube-nocookie.com",
            "www.youtube-nocookie.com"
        };

        public static bool IsValidYouTubeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)) return false;

            // Только HTTPS: по http возможна подмена ответа в открытых сетях.
            if (uri.Scheme != Uri.UriSchemeHttps) return false;

            return AllowedHosts.Contains(uri.Host);
        }

        public static string ExtractVideoId(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            Match match = VideoIdRegex.Match(url);
            return match.Success ? match.Groups[1].Value : "";
        }
    }
}
