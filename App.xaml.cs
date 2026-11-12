using System;
using System.Windows;

namespace YTShortsManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            this.DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show("Критическая ошибка: " + args.Exception.Message);
            };
        }
    }
}