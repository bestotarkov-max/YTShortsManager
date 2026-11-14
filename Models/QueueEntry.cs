namespace YTShortsManager.Models
{
    /// <summary>
    /// Связка "запрос + его строка в UI". Раньше строка искалась по тексту статуса,
    /// что ломалось при любой смене статуса.
    /// </summary>
    internal sealed class QueueEntry
    {
        public QueueEntry(DownloadRequest request, QueueItem item, string outputFolder)
        {
            Request = request;
            Item = item;
            OutputFolder = outputFolder;
        }

        public DownloadRequest Request { get; }
        public QueueItem Item { get; }
        public string OutputFolder { get; }
    }
}
