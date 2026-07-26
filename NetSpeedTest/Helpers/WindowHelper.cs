using System.Windows;

namespace NetSpeedTest.Helpers;

public static class WindowHelper
{
    public static void ClampToScreen(Window window)
    {
        var area = SystemParameters.WorkArea;
        if (window.Width > area.Width * 0.95) window.Width = area.Width * 0.92;
        if (window.Height > area.Height * 0.95) window.Height = area.Height * 0.92;
        window.Left = (area.Width - window.Width) / 2 + area.Left;
        window.Top = (area.Height - window.Height) / 2 + area.Top;
    }
}
