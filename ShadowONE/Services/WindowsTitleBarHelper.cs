using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ShadowONE.Services
{
    internal static class WindowsTitleBarHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void SetDarkTitleBar(Window window)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            void TryApply()
            {
                var handle = GetWindowHandle(window);
                if (handle == IntPtr.Zero)
                    return;

                int value = 1;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref value, sizeof(int));
            }

            TryApply();

            Dispatcher.UIThread.Post(TryApply, DispatcherPriority.Loaded);
            window.Opened += OnOpened;

            void OnOpened(object? sender, EventArgs e)
            {
                window.Opened -= OnOpened;
                TryApply();
            }
        }

        private static IntPtr GetWindowHandle(Window window)
        {
            var platformHandle = window.TryGetPlatformHandle();
            return platformHandle?.Handle ?? IntPtr.Zero;
        }
    }
}
