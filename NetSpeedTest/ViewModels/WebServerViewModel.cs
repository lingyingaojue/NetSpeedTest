using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace NetSpeedTest.ViewModels;

/// <summary>
/// Web 服务器独立分类页 ViewModel。
/// </summary>
public partial class WebServerViewModel : ObservableObject
{
    private readonly WebServerService _webServer;
    private bool _syncingEnabled;
    private bool _syncingLanAccess;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private bool _allowLanAccess;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _lanStatusText = "";

    [ObservableProperty]
    private string _copyResultText = "";

    [ObservableProperty]
    private ObservableCollection<AdapterAccessBinding> _lanBindings = new();

    public const int Port = 8080;

    public string Url => "http://127.0.0.1:8080";

    public string WwwRootPath => Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");

    public IReadOnlyList<string> ApiEndpoints { get; } = new[]
    {
        "GET  /api/status",
        "GET  /api/adapters",
        "POST /api/adapters/select",
        "GET  /api/profiles",
        "POST /api/profiles",
        "GET  /api/history",
        "DELETE /api/history",
        "GET  /api/settings",
        "POST /api/settings",
        "POST /api/test/start",
        "POST /api/test/stop",
        "GET  /api/server"
    };

    public WebServerViewModel(WebServerService webServer)
    {
        _webServer = webServer;
        _enabled = webServer.Enabled;
        _allowLanAccess = webServer.AllowLanAccess;
        RefreshBindings();
        RefreshStatus();
        RefreshLanStatus();
        _webServer.StateChanged += OnServerStateChanged;
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_syncingEnabled) return;
        _webServer.SetEnabled(value);
        _webServer.SaveEnabled(value);
        RefreshStatus();
        RefreshLanStatus();
    }

    partial void OnAllowLanAccessChanged(bool value)
    {
        if (_syncingLanAccess) return;
        _webServer.SetAllowLanAccess(value);
        RefreshLanStatus();
    }

    private void OnServerStateChanged()
    {
        _syncingEnabled = true;
        _syncingLanAccess = true;
        Enabled = _webServer.Enabled;
        AllowLanAccess = _webServer.AllowLanAccess;
        _syncingEnabled = false;
        _syncingLanAccess = false;
        RefreshBindings();
        RefreshStatus();
        RefreshLanStatus();
    }

    private void OnLanguageChanged()
    {
        RefreshStatus();
        RefreshLanStatus();
    }

    private void RefreshBindings()
    {
        LanBindings.Clear();
        foreach (var item in _webServer.Bindings)
            LanBindings.Add(item);
    }

    private void RefreshStatus()
    {
        if (_webServer.Enabled)
        {
            StatusText = $"{LocalizationService.Get("WebServer_Running")} · {Url}";
        }
        else if (!string.IsNullOrWhiteSpace(_webServer.LastError))
        {
            StatusText = $"{LocalizationService.Get("WebServer_StartFailed")}: {_webServer.LastError}";
        }
        else
        {
            StatusText = LocalizationService.Get("WebServer_Stopped");
        }
    }

    private void RefreshLanStatus()
    {
        if (!AllowLanAccess)
        {
            LanStatusText = LocalizationService.Get("WebServer_LanOff");
        }
        else if (!Enabled)
        {
            LanStatusText = LocalizationService.Get("WebServer_LanWaitStart");
        }
        else if (_webServer.LanReady)
        {
            LanStatusText = $"{LocalizationService.Get("WebServer_LanOn")} · {LanBindings.Count} {LocalizationService.Get("WebServer_LanSegments")}";
        }
        else if (!string.IsNullOrWhiteSpace(_webServer.LanError))
        {
            LanStatusText = $"{LocalizationService.Get("WebServer_LanFailed")}: {_webServer.LanError}";
        }
        else
        {
            LanStatusText = LocalizationService.Get("WebServer_LanFailed");
        }
    }

    [RelayCommand]
    private void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CopyResultText = $"{LocalizationService.Get("WebServer_OpenFailed")}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyUrl()
    {
        try
        {
            Clipboard.SetText(Url);
            CopyResultText = LocalizationService.Get("WebServer_Copied");
        }
        catch (Exception ex)
        {
            CopyResultText = $"{LocalizationService.Get("WebServer_CopyFailed")}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyLanAddresses()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(LocalizationService.Get("WebServer_LanAddresses"));
            foreach (var item in LanBindings)
                sb.AppendLine($"{item.AdapterName} · {item.DisplayText} · {item.Subnet} → {item.Url}");
            Clipboard.SetText(sb.ToString().TrimEnd());
            CopyResultText = LocalizationService.Get("WebServer_Copied");
        }
        catch (Exception ex)
        {
            CopyResultText = $"{LocalizationService.Get("WebServer_CopyFailed")}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenWwwRoot()
    {
        try
        {
            var dir = Path.GetDirectoryName(WwwRootPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                CopyResultText = LocalizationService.Get("WebServer_DirMissing");
                return;
            }

            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CopyResultText = $"{LocalizationService.Get("WebServer_OpenFailed")}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }

    public event Action? CloseRequested;
}