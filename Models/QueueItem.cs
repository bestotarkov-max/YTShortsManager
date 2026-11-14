using System.ComponentModel;
using System.Runtime.CompilerServices;
using YTShortsManager.Services;

namespace YTShortsManager.Models
{
    public static class QueueStatus
    {
        public const string Queued = "Queued";
        public const string Downloading = "Downloading";
        public const string Done = "Done";
        public const string Failed = "Failed";
    }

    public class QueueItem : INotifyPropertyChanged
    {
        private string _status = QueueStatus.Queued;

        public string Url { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>Внутренний токен статуса — не зависит от языка интерфейса.</summary>
        public string Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        /// <summary>Подпись статуса для UI на текущем языке.</summary>
        public string StatusText => AppStrings.QueueStatusText(_status);

        /// <summary>Пересчитывает подпись после смены языка.</summary>
        public void RefreshStatus() => OnPropertyChanged(nameof(StatusText));

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
