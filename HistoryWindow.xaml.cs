using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace YTShortsManager
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();
        }

        private void Item_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is HistoryItem item)
            {
                if (File.Exists(item.Path))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Не удалось открыть файл:\n{ex.Message}", "Ошибка");
                    }
                }
                else
                {
                    MessageBox.Show($"Файл не найден по пути:\n{item.Path}", "Ошибка");
                }
            }
        }
    }
}