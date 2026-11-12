using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YTShortsManager
{
    public class QueueItem : INotifyPropertyChanged
    {
        private string _status = "В очереди";
        private string _title = "Загрузка информации...";

        public string Url { get; set; } = "";
        
        public string Title 
        { 
            get => _title; 
            set { _title = value; OnPropertyChanged(); } 
        }
        
        public string Status 
        { 
            get => _status; 
            set { _status = value; OnPropertyChanged(); } 
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}