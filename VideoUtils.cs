using System.Text.RegularExpressions;

namespace YTShortsManager
{
    public static class VideoUtils
    {
        public static string ExtractVideoId(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            var regex = new Regex(@"(?:youtu\.be\/|youtube\.com\/(?:embed\/|v\/|watch\?v=|watch\?.+&v=|shorts\/))([\w-]{11})");
            var match = regex.Match(url);
            return match.Success ? match.Groups[1].Value : "";
        }
    }
}