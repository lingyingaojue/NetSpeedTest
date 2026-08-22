using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using NetSpeedTest.Services;

namespace NetSpeedTest.Helpers;

public static class WindowHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// 应用深色标题栏 + 圆角（无背景模糊）。需在窗口 HWND 创建后（SourceInitialized）调用。
    /// </summary>
    public static void ApplyWindowChrome(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int dark = ThemeService.Current == ThemeMode.Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            int corner = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }
        catch { }
    }

    public static void ClampToScreen(Window window)
    {
        var area = SystemParameters.WorkArea;
        if (window.Width > area.Width * 0.95) window.Width = area.Width * 0.92;
        if (window.Height > area.Height * 0.95) window.Height = area.Height * 0.92;
        window.Left = (area.Width - window.Width) / 2 + area.Left;
        window.Top = (area.Height - window.Height) / 2 + area.Top;
    }
}
