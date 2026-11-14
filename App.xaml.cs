using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using YTShortsManager.Services;

namespace YTShortsManager
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "YTShortsManager_SingleInstance_Mutex_IZYXxx";

            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "Программа уже запущена и работает в системном трее (возле часов)!",
                    "Внимание",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Shutdown();
                return;
            }

            base.OnStartup(e);

            // Ошибки UI больше не убивают программу молча — пишем в лог и продолжаем работу.
            DispatcherUnhandledException += (_, args) =>
            {
                FileLog.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI-ОШИБКА: {args.Exception}");
                MessageBox.Show("Ошибка: " + args.Exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                FileLog.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ФАТАЛЬНАЯ ОШИБКА: {args.ExceptionObject}");

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                FileLog.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ФОНОВАЯ ОШИБКА: {args.Exception}");
                args.SetObserved();
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // Мьютекс мог быть уже освобождён.
            }

            _mutex?.Dispose();
            _mutex = null;

            base.OnExit(e);
        }
    }
}
