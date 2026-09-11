using System.Net.NetworkInformation;
using System.Threading;
using NetSpeedTest.Models;

namespace NetSpeedTest.Services;

/// <summary>
/// 监听系统网卡/网络地址变化，去抖后通知界面刷新。
/// </summary>
public sealed class NetworkMonitorService : IDisposable
{
    private const int DebounceMs = 800;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly NetworkInfoService _networkInfoService;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Timer _debounceTimer;
    private readonly Timer _pollTimer;

    private string _lastFingerprint = "";
    private bool _disposed;

    /// <summary>
    /// 检测到网卡集合、IP、网关或状态发生变化时触发。可能在后台线程触发。
    /// </summary>
    public event Action? NetworkChanged;

    public NetworkMonitorService(NetworkInfoService networkInfoService)
    {
        _networkInfoService = networkInfoService;

        _lastFingerprint = BuildFingerprint(_networkInfoService.GetPhysicalAdapters());

        _debounceTimer = new Timer(_ => _ = RefreshAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _pollTimer = new Timer(_ => _ = RefreshAsync(), null, PollInterval, PollInterval);

        NetworkChange.NetworkAddressChanged += OnNetworkChange;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChange;
    }

    private void OnNetworkChange(object? sender, EventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (_disposed) return;
        try { _debounceTimer.Change(DebounceMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    private async Task RefreshAsync()
    {
        if (_disposed) return;

        try
        {
            await _refreshGate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed) return;

            _networkInfoService.InvalidateCache();
            var adapters = _networkInfoService.GetPhysicalAdapters();
            var fingerprint = BuildFingerprint(adapters);

            var changed = false;
            lock (_sync)
            {
                if (!string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    _lastFingerprint = fingerprint;
                    changed = true;
                }
            }

            if (changed)
            {
                Logger.Log($"[NET] network changed, adapters={adapters.Count}");
                NetworkChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[NET] refresh failed: {ex.Message}");
        }
        finally
        {
            try { _refreshGate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private static string BuildFingerprint(IEnumerable<NetworkAdapterInfo> adapters)
    {
        return string.Join("|", adapters
            .OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(a => string.Join(":", a.Id, a.IPAddress, a.Gateway, a.StatusText, a.IsOperational, a.LinkSpeedBps, a.TypeName)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        NetworkChange.NetworkAddressChanged -= OnNetworkChange;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChange;

        _debounceTimer.Dispose();
        _pollTimer.Dispose();
        _refreshGate.Dispose();
    }
}
