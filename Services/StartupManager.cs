using System;
using System.IO;
using Microsoft.Win32;

namespace YTShortsManager.Services
{
    internal static class StartupManager
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "YTShortsManager";

        public static bool IsEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <returns>null при успехе, иначе текст ошибки.</returns>
        public static string? Set(bool enable)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return "Не удалось открыть раздел автозапуска.";

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                        return "Не удалось определить путь к программе.";

                    key.SetValue(ValueName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }

                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
