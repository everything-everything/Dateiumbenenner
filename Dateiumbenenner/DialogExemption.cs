using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Dateiumbenenner
{
    /// <summary>
    /// Hält ein Fenster (z. B. die Belegvorschau) bedienbar, während ein anderer Dialog modal geöffnet ist:
    /// WPF sperrt bei ShowDialog alle Fenster des Programms – das Fenster wird sofort wieder freigegeben.
    /// Gehört der Dialog zu diesem Fenster selbst, bleibt es gesperrt.
    /// </summary>
    public static class DialogExemption
    {
        private const int WM_ENABLE = 0x000A;
        private const uint GW_ENABLEDPOPUP = 6;

        [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr hWnd, bool enable);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        public static void Attach(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero) Hook(window, handle);
            else window.SourceInitialized += (_, _) => Hook(window, new WindowInteropHelper(window).Handle);
        }

        private static void Hook(Window window, IntPtr handle)
        {
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_ENABLE && wParam == IntPtr.Zero)
                    window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Reenable(hwnd)));
                return IntPtr.Zero;
            });
        }

        private static void Reenable(IntPtr hwnd)
        {
            if (!IsWindow(hwnd) || IsWindowEnabled(hwnd)) return;
            var popup = GetWindow(hwnd, GW_ENABLEDPOPUP);
            if (popup != IntPtr.Zero && popup != hwnd) return;
            EnableWindow(hwnd, true);
        }
    }
}
