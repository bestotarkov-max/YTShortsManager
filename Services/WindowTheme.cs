using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace YTShortsManager.Services
{
    /// <summary>Тёмный системный заголовок окна (Windows 10 1809+ / Windows 11).</summary>
    internal static class WindowTheme
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeLegacy = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void UseDarkTitleBar(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;

                int enabled = 1;

                // На старых сборках Win10 атрибут называется иначе — пробуем оба.
                if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }
            catch
            {
                // Старая Windows — оставляем стандартный заголовок.
            }
        }
    }
}
