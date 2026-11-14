using System;
using System.IO;

namespace YTShortsManager.Services
{
    /// <summary>Файловый лог с простой ротацией (не растёт бесконечно).</summary>
    internal static class FileLog
    {
        private const long MaxBytes = 2 * 1024 * 1024;
        private static readonly object Gate = new();

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    AppPaths.EnsureDataDir();

                    FileInfo file = new(AppPaths.LogFile);
                    if (file.Exists && file.Length > MaxBytes) file.Delete();

                    File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // Лог не должен ронять программу.
            }
        }
    }
}
