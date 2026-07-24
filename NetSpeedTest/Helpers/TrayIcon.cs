using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace NetSpeedTest.Helpers;

internal static class TrayIcon
{
    private static HwndSource? _source;
    private static uint _wmTaskbarRestart;
    private static uint _wmCallback;
    private static Action? _onShow;
    private static ContextMenu? _contextMenu;
    private static System.Drawing.Icon? _icon;
    private static readonly Guid _iconGuid = Guid.NewGuid();
    private static Func<bool>? _isTesting;
    private static Action<string, string>? _onNotify;
    private static List<(string tag, MenuItem item)>? _menuItems;

    private const int NIM_ADD = 0;
    private const int NIM_MODIFY = 1;
    private const int NIM_DELETE = 2;
    private const int NIF_MESSAGE = 1;
    private const int NIF_ICON = 2;
    private const int NIF_TIP = 4;
    private const int NIF_INFO = 16;
    private const int NIF_GUID = 32;
    private const int NIF_SHOWTIP = 128;
    private const int NIIF_INFO = 1;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    public static void Init(Window window, Action onShow,
        List<(string label, Action action)> menuItems,
        Func<bool>? isTesting = null,
        Action<string, string>? onNotify = null)
    {
        _onShow = onShow;
        _isTesting = isTesting;
        _onNotify = onNotify;

        _wmTaskbarRestart = RegisterWindowMessage("TaskbarCreated");
        _wmCallback = RegisterWindowMessage("NetSpeedTestTrayCallback");

        _icon = LoadIcon();

        var styles = Application.Current.Resources;
        _contextMenu = new ContextMenu
        {
            Background = (System.Windows.Media.Brush?)styles["CardBrush"],
            Foreground = (System.Windows.Media.Brush?)styles["TextPrimaryBrush"]
        };
        _contextMenu.Opened += (_, _) => RefreshMenuState();

        _menuItems = new();
        foreach (var (label, action) in menuItems)
        {
            if (label == "-")
            {
                _contextMenu.Items.Add(new Separator());
            }
            else
            {
                var item = new MenuItem { Header = label };
                item.Click += (_, _) => action();
                _contextMenu.Items.Add(item);
                _menuItems.Add((label, item));
            }
        }

        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var helper = new WindowInteropHelper(window);
                var hwnd = helper.EnsureHandle();
                _source = HwndSource.FromHwnd(hwnd);
                _source.AddHook(WndProc);
                AddIcon();
            }
            catch { }
        };

        window.Closed += (_, _) => Dispose();
    }

    private static void RefreshMenuState()
    {
        if (_menuItems == null) return;
        var testing = _isTesting?.Invoke() ?? false;
        foreach (var (tag, item) in _menuItems)
        {
            if (tag is "开始下载测速" or "开始上传测速" or "开始双向测速")
                item.IsEnabled = !testing;
            else if (tag == "取消测速")
                item.IsEnabled = testing;
        }
    }

    public static void ShowBalloon(string title, string text)
    {
        if (_source == null) return;
        var data = new NOTIFYICONDATA();
        data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
        data.hWnd = _source.Handle;
        data.guidItem = _iconGuid;
        data.uFlags = NIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private static void AddIcon()
    {
        if (_source == null || _icon == null) return;

        var data = new NOTIFYICONDATA();
        data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID | NIF_SHOWTIP;
        data.hWnd = _source.Handle;
        data.uCallbackMessage = _wmCallback;
        data.hIcon = _icon.Handle;
        data.guidItem = _iconGuid;
        data.szTip = "NetSpeedTest";

        if (!Shell_NotifyIcon(NIM_ADD, ref data))
        {
            data.cbSize = 952;
            Shell_NotifyIcon(NIM_ADD, ref data);
        }
    }

    private static System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var walk = Application.ResourceAssembly ?? typeof(TrayIcon).Assembly;
            foreach (var name in walk.GetManifestResourceNames())
            {
                if (name.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase))
                {
                    using var s = walk.GetManifestResourceStream(name);
                    if (s != null) return new System.Drawing.Icon(s);
                }
            }
        }
        catch { }

        try { return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { }

        return null;
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _wmCallback)
        {
            if ((int)lParam == WM_LBUTTONDBLCLK)
                _onShow?.Invoke();
            else if ((int)lParam == WM_RBUTTONUP)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _contextMenu!.IsOpen = true;
                });
            }
            handled = true;
        }
        else if (msg == _wmTaskbarRestart)
        {
            AddIcon();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void Dispose()
    {
        if (_source != null)
        {
            var data = new NOTIFYICONDATA();
            data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
            data.hWnd = _source.Handle;
            data.guidItem = _iconGuid;
            Shell_NotifyIcon(NIM_DELETE, ref data);
        }
        _icon?.Dispose();
        _icon = null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}
