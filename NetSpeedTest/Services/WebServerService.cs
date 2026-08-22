using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NetSpeedTest.Models;
using NetSpeedTest.ViewModels;

namespace NetSpeedTest.Services;

/// <summary>
/// 内置 Web 服务器：为 Web 界面提供 REST API 和静态文件服务。
/// 支持网卡网段映射：仅允许与本机任一网卡同网段的局域网设备访问。
/// </summary>
public class WebServerService
{
    public const int Port = 8080;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const int MaxRequestBodyChars = 256 * 1024;
    private static readonly SemaphoreSlim RequestGate = new(64, 64);

    private readonly IServiceProvider _serviceProvider;
    private readonly object _gate = new();
    private readonly List<HttpListener> _listeners = new();
    private CancellationTokenSource? _cts;
    private bool _enabled;
    private bool _allowLanAccess = true;
    private bool _lanReady;
    private bool _aclReady;
    private bool _firewallReady;
    private string _lanError = "";
    private List<AdapterAccessBinding> _bindings = new();
    private readonly object _bindingsGate = new();
    private DateTime _bindingsBuiltAtUtc;

    public bool Enabled => _enabled;

    /// <summary>
    /// 是否允许与本机网卡同网段的局域网设备访问。
    /// </summary>
    public bool AllowLanAccess => _allowLanAccess;

    public bool LanReady => _lanReady;

    public bool AclReady => _aclReady;

    public bool FirewallReady => _firewallReady;

    public string LanError => _lanError;

    public IReadOnlyList<AdapterAccessBinding> Bindings => _bindings;

    /// <summary>
    /// 最近一次启动失败信息；成功启动或停止后清空。
    /// </summary>
    public string LastError { get; private set; } = "";

    public event Action? StateChanged;

    public WebServerService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _bindings = BuildAdapterBindings();
        _bindingsBuiltAtUtc = DateTime.UtcNow;
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled) Start(); else Stop();
    }

    public void SetAllowLanAccess(bool allow)
    {
        if (_allowLanAccess == allow) return;
        _allowLanAccess = allow;
        SaveSettings();
        if (_enabled)
        {
            Stop();
            Start();
        }
        else
        {
            StateChanged?.Invoke();
        }
        SaveSettings();
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_listeners.Count > 0) return;
            try
            {
                // Web 文件已嵌入 exe；若 exe 同目录存在 wwwroot，则作为自定义覆盖
                _bindings = BuildAdapterBindings();
                _bindingsBuiltAtUtc = DateTime.UtcNow;
                _aclReady = false;
                _firewallReady = false;
                _lanReady = false;
                _lanError = "";

                var loopback = StartListener("http://127.0.0.1:8080/", "http://localhost:8080/");
                if (loopback == null) throw new InvalidOperationException("无法监听 127.0.0.1:8080");
                _listeners.Add(loopback);

                if (_allowLanAccess)
                    StartLanListeners();

                _enabled = true;
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;
                foreach (var listener in _listeners.ToArray())
                {
                    var current = listener;
                    _ = Task.Run(() => ListenLoopAsync(current, ct));
                }
                LastError = "";
                Logger.Log(_lanReady
                    ? $"Web server started on http://127.0.0.1:{Port} with LAN subnet mapping"
                    : $"Web server started loopback-only on http://127.0.0.1:{Port}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Web server start failed: {ex.Message}");
                CleanupListeners();
                _enabled = false;
                _lanReady = false;
                LastError = ex.Message;
            }
        }
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        lock (_gate)
        {
            try { _cts?.Cancel(); } catch { }
            CleanupListeners();
            _enabled = false;
            _lanReady = false;
            LastError = "";
            Logger.Log("Web server stopped");
        }
        StateChanged?.Invoke();
    }

    public void ApplySavedState()
    {
        var (enabled, allowLan) = LoadSettings();
        _allowLanAccess = allowLan;
        if (enabled) Start();
    }

    public void SaveEnabled(bool enabled)
    {
        // 保存实际生效状态：Start 失败时不会把 Enabled=true 持久化
        _ = enabled;
        SaveSettings(_enabled);
    }

    private void CleanupListeners()
    {
        foreach (var l in _listeners)
        {
            try { l.Stop(); } catch { }
            try { l.Close(); } catch { }
        }
        _listeners.Clear();
        _cts = null;
    }

    private void StartLanListeners()
    {
        if (_bindings.Count == 0)
        {
            _lanError = LocalizationService.Get("WebServer_NoBindings");
            return;
        }

        try
        {
            EnsureUrlAcl();
        }
        catch (Exception ex)
        {
            _lanError = ex.Message;
            Logger.Log($"URL ACL setup failed: {ex.Message}");
        }

        try
        {
            EnsureFirewallRule();
            _firewallReady = true;
        }
        catch (Exception ex)
        {
            _firewallReady = false;
            if (string.IsNullOrWhiteSpace(_lanError)) _lanError = ex.Message;
            Logger.Log($"Firewall rule setup failed: {ex.Message}");
        }

        // 优先通配符监听：网卡/网段变化后无需重设 ACL
        var wildcard = StartListener("http://+:8080/");
        if (wildcard != null)
        {
            _listeners.Add(wildcard);
            _lanReady = true;
            _aclReady = true;
            _lanError = "";
            return;
        }

        // 回退：逐网卡监听其本机 IP
        var lanCount = 0;
        foreach (var binding in _bindings)
        {
            var listener = StartListener($"http://{binding.IPAddress}:8080/");
            if (listener == null) continue;
            _listeners.Add(listener);
            lanCount++;
        }
        _lanReady = lanCount > 0;
        _aclReady = _lanReady;
        if (!_lanReady && string.IsNullOrWhiteSpace(_lanError))
            _lanError = LocalizationService.Get("WebServer_LanNeedAdmin");
    }

    private HttpListener? StartListener(params string[] prefixes)
    {
        var listener = new HttpListener();
        try
        {
            foreach (var prefix in prefixes) listener.Prefixes.Add(prefix);
            listener.Start();
            return listener;
        }
        catch (Exception ex)
        {
            Logger.Log($"Listener start failed for {string.Join(", ", prefixes)}: {ex.Message}");
            try { listener.Close(); } catch { }
            return null;
        }
    }

    private void EnsureUrlAcl()
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        RunNetsh($"http add urlacl url=http://+:8080/ user=\"{user}\"");
    }

    private void EnsureFirewallRule()
    {
        const string ruleName = "NetSpeedTest Web Server 8080";
        try
        {
            RunNetsh($"advfirewall firewall show rule name=\"{ruleName}\"");
            return;
        }
        catch { }

        RunNetsh($"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={Port} profile=any");
    }

    private static void RunNetsh(string arguments)
    {
        var psi = new ProcessStartInfo("netsh.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi);
        if (process == null) throw new InvalidOperationException("无法启动 netsh");
        process.WaitForExit(10000);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
    }

    private (bool Enabled, bool AllowLanAccess) LoadSettings()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            var path = Path.Combine(dir, "web.json");
            if (!File.Exists(path)) return (false, true);

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var enabled = doc.RootElement.TryGetProperty("Enabled", out var e) && e.GetBoolean();
            var allow = !doc.RootElement.TryGetProperty("AllowLanAccess", out var a) || a.GetBoolean();
            return (enabled, allow);
        }
        catch (Exception ex)
        {
            Logger.Log($"Web state load failed: {ex.Message}");
            return (false, true);
        }
    }

    private void SaveSettings(bool? enabled = null)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "web.json");
            var target = enabled ?? _enabled;
            File.WriteAllText(path, JsonSerializer.Serialize(new { Enabled = target, AllowLanAccess = _allowLanAccess }));
        }
        catch (Exception ex) { Logger.Log($"Web state save failed: {ex.Message}"); }
    }

    private List<AdapterAccessBinding> BuildAdapterBindings()
    {
        var result = new List<AdapterAccessBinding>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(unicast.Address)) continue;
                    var mask = unicast.IPv4Mask;
                    if (mask == null) continue;

                    var ipBytes = unicast.Address.GetAddressBytes();
                    var maskBytes = mask.GetAddressBytes();
                    if (ipBytes.Length != 4 || maskBytes.Length != 4) continue;
                    // 跳过 APIPA 链路本地地址（169.254.x.x），不是有效局域网段
                    if (ipBytes[0] == 169 && ipBytes[1] == 254) continue;

                    var networkBytes = new byte[4];
                    var prefixLength = 0;
                    for (var i = 0; i < 4; i++)
                    {
                        networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
                        prefixLength += CountBits(maskBytes[i]);
                    }

                    var subnet = new IPAddress(networkBytes);
                    result.Add(new AdapterAccessBinding
                    {
                        AdapterName = nic.Name,
                        Description = nic.Description,
                        IPAddress = unicast.Address.ToString(),
                        SubnetMask = mask.ToString(),
                        PrefixLength = prefixLength,
                        Subnet = $"{subnet}/{prefixLength}",
                        Url = $"http://{unicast.Address}:{Port}/"
                    });
                }
            }
        }
        catch (Exception ex) { Logger.Log($"Build adapter bindings failed: {ex.Message}"); }

        return result
            .OrderByDescending(b => b.Subnet.StartsWith("192.168.") || b.Subnet.StartsWith("10."))
            .ThenByDescending(b => b.Subnet.StartsWith("172."))
            .ThenBy(b => b.AdapterName)
            .ToList();
    }

    private static int CountBits(byte value)
    {
        var count = 0;
        for (var i = 0; i < 8; i++)
            if ((value & (1 << i)) != 0) count++;
        return count;
    }


    private void RefreshBindingsIfStale()
    {
        lock (_bindingsGate)
        {
            if ((DateTime.UtcNow - _bindingsBuiltAtUtc).TotalSeconds < 5) return;
            _bindings = BuildAdapterBindings();
            _bindingsBuiltAtUtc = DateTime.UtcNow;
        }
    }

    private bool IsRemoteAllowed(IPEndPoint? endpoint)
    {
        if (endpoint == null) return false;
        var remote = endpoint.Address;
        if (IPAddress.IsLoopback(remote)) return true;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (remote.AddressFamily != AddressFamily.InterNetwork) return false;
        if (!_allowLanAccess) return false;
        RefreshBindingsIfStale();

        var remoteBytes = remote.GetAddressBytes();
        foreach (var binding in _bindings)
        {
            if (!IPAddress.TryParse(binding.IPAddress, out var local)) continue;
            if (!IPAddress.TryParse(binding.SubnetMask, out var mask)) continue;
            var localBytes = local.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();
            if (localBytes.Length != 4 || maskBytes.Length != 4) continue;

            var same = true;
            for (var i = 0; i < 4; i++)
            {
                if ((remoteBytes[i] & maskBytes[i]) == (localBytes[i] & maskBytes[i])) continue;
                same = false;
                break;
            }
            if (same) return true;
        }
        return false;
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; }

            _ = Task.Run(async () =>
            {
                await RequestGate.WaitAsync(ct);
                try { await HandleContextAsync(ctx); }
                finally { RequestGate.Release(); }
            }, ct);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx)
    {
        try
        {
            if (!IsRemoteAllowed(ctx.Request.RemoteEndPoint))
            {
                await WriteJsonAsync(ctx, 403, new { error = "Forbidden", message = "Remote IP is not in a local subnet" });
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod;

            if (method == "GET" && (path == "/" || path == "/index.html" || path.StartsWith("/assets/")))
            {
                await ServeStaticAsync(ctx, path);
                return;
            }

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleApiAsync(ctx, method, path);
                return;
            }

            await WriteJsonAsync(ctx, 404, new { error = "Not Found" });
        }
        catch (Exception ex)
        {
            Logger.Log($"Web request failed: {ex.Message}");
            try { await WriteJsonAsync(ctx, 500, new { error = ex.Message }); } catch { }
        }
    }

    private async Task HandleApiAsync(HttpListenerContext ctx, string method, string path)
    {
        switch (path.ToLowerInvariant())
        {
            case "/api/status":
                await WriteJsonAsync(ctx, 200, GetStatus());
                return;
            case "/api/adapters":
                await WriteJsonAsync(ctx, 200, GetAdapters());
                return;
            case "/api/adapters/select":
                if (method == "POST") { await HandleAdaptersSelectAsync(ctx); return; }
                break;
            case "/api/profiles":
                if (method == "GET") { await WriteJsonAsync(ctx, 200, GetProfiles()); return; }
                if (method == "POST") { await HandleProfilesPostAsync(ctx); return; }
                break;
            case "/api/history":
                if (method == "GET") { await HandleHistoryAsync(ctx); return; }
                if (method == "DELETE") { await HandleHistoryDeleteAsync(ctx); return; }
                break;
            case "/api/settings":
                if (method == "GET") { await WriteJsonAsync(ctx, 200, GetSettings()); return; }
                if (method == "POST") { await HandleSettingsPostAsync(ctx); return; }
                break;
            case "/api/test/start":
                if (method == "POST") { await HandleTestStartAsync(ctx); return; }
                break;
            case "/api/test/stop":
                if (method == "POST") { await HandleTestStopAsync(ctx); return; }
                break;
            case "/api/server":
                if (method == "GET") { await WriteJsonAsync(ctx, 200, GetServerInfo()); return; }
                break;
        }

        await WriteJsonAsync(ctx, 404, new { error = "Not Found" });
    }


    private object GetServerInfo()
    {
        RefreshBindingsIfStale();
        return new
        {
            enabled = Enabled,
            port = Port,
            url = "http://127.0.0.1:8080",
            lanAccess = AllowLanAccess,
            lanReady = LanReady,
            aclReady = AclReady,
            firewallReady = FirewallReady,
            lanError = LanError,
            bindings = _bindings.Select(b => new
            {
                adapterName = b.AdapterName,
                description = b.Description,
                ip = b.IPAddress,
                subnetMask = b.SubnetMask,
                prefixLength = b.PrefixLength,
                subnet = b.Subnet,
                url = b.Url
            })
        };
    }
    private MainViewModel GetMainViewModel() => _serviceProvider.GetRequiredService<MainViewModel>();

    private async Task HandleAdaptersSelectAsync(HttpListenerContext ctx)
    {
        try
        {
            var body = await ReadBodyAsync(ctx.Request);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var ids = ReadStringList(root, "adapterIds");

            var vm = GetMainViewModel();
            var validIds = vm.Adapters.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selected = ids.Where(validIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (selected.Count == 0)
            {
                await WriteJsonAsync(ctx, 400, new { error = "At least one valid adapter id is required" });
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in vm.AdapterSelectionItems)
                    item.IsSelected = selected.Contains(item.Adapter.Id);
            });

            await WriteJsonAsync(ctx, 200, new { ok = true, selectedAdapterIds = selected });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(ctx, 400, new { error = ex.Message });
        }
    }

    private object GetStatus()
    {
        var vm = GetMainViewModel();
        return new
        {
            running = vm.IsTesting,
            status = vm.StatusText,

            elapsedSeconds = vm.ElapsedSeconds,
            downloadMbps = vm.DownloadMbps,
            uploadMbps = vm.UploadMbps,
            totalMbps = vm.TotalRateMbps,
            averageMbps = vm.AverageMbps,
            averageDownloadMbps = vm.AverageDownloadMbps,
            averageUploadMbps = vm.AverageUploadMbps,
            averageTotalMbps = vm.AverageTotalMbps,
            latencyMs = vm.LatencyMs,
            wanLatencyMs = vm.WanLatencyMs,
            jitterMs = vm.JitterMs,
            packetLossPercent = vm.PacketLossPercent,
            packetLossSent = vm.PacketLossSent,
            packetLossReceived = vm.PacketLossReceived,
            totalBytes = vm.TotalBytes,
            activeThreads = vm.ActiveThreadCount,
            selectedAdapters = vm.AdapterSelectionItems.Where(x => x.IsSelected).Select(x => x.Adapter.Id).ToList(),
            currentProfile = vm.SelectedProfile?.Name,
            recentResult = vm.HasRecentResult ? new
            {
                downloadMbps = vm.RecentDownloadMbps,
                uploadMbps = vm.RecentUploadMbps,
                latencyMs = vm.RecentLatencyMs
            } : null
        };
    }

    private object GetAdapters()
    {
        var vm = GetMainViewModel();
        return vm.Adapters.Select(a => new
        {
            id = a.Id,
            name = a.Name,
            description = a.Description,
            ip = a.IPAddress,
            gateway = a.Gateway,
            mac = a.MacAddress,
            type = a.TypeName,
            status = a.StatusText,
            linkSpeedBps = a.LinkSpeedBps,
            selected = vm.AdapterSelectionItems.FirstOrDefault(x => x.Adapter.Id == a.Id)?.IsSelected ?? false
        });
    }

    private object GetProfiles()
    {
        var service = _serviceProvider.GetRequiredService<ProfileService>();
        return service.GetAllProfiles();
    }

    private async Task HandleProfilesPostAsync(HttpListenerContext ctx)
    {
        try
        {
            var body = await ReadBodyAsync(ctx.Request);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var service = _serviceProvider.GetRequiredService<ProfileService>();
            var profile = new SpeedTestProfile
            {
                Id = root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : Guid.NewGuid().ToString("N"),
                Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                DownloadUrls = ReadStringList(root, "downloadUrls"),
                UploadUrls = ReadStringList(root, "uploadUrls"),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            if (root.TryGetProperty("delete", out var del) && del.ValueKind == JsonValueKind.True)
            {
                service.DeleteProfile(profile.Id);

                await WriteJsonAsync(ctx, 200, new { ok = true });
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new InvalidDataException("Profile name is required");
            foreach (var u in profile.DownloadUrls.Concat(profile.UploadUrls))
            {
                if (!await IsPublicHttpUrlAsync(u))
                    throw new InvalidDataException("Only public http/https URLs are allowed");
            }

            service.SaveProfile(profile);
            var vm = GetMainViewModel();
            Application.Current.Dispatcher.Invoke(vm.RefreshProfilesForWeb);
            await WriteJsonAsync(ctx, 200, new { ok = true, id = profile.Id });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(ctx, 400, new { error = ex.Message });
        }
    }

    private static async Task<bool> IsPublicHttpUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        if (IPAddress.TryParse(uri.Host, out var literal)) return !IsPrivateIp(literal);
        try
        {
            using var dnsCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var addrs = await Dns.GetHostAddressesAsync(uri.Host, dnsCts.Token);
            foreach (var addr in addrs)
                if (IsPrivateIp(addr)) return false;
        }
        catch { return false; }
        return true;
    }

    private static bool IsPrivateIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        if (b.Length == 4)
        {
            return (b[0] == 10)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 127);
        }
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
        return ip.IsIPv4MappedToIPv6 && IsPrivateIp(ip.MapToIPv4());
    }
    private static List<string> ReadStringList(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array) return new();
        return arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private async Task HandleHistoryAsync(HttpListenerContext ctx)
    {
        var page = int.TryParse(ctx.Request.QueryString["page"], out var p) ? Math.Max(1, p) : 1;
        var pageSize = int.TryParse(ctx.Request.QueryString["pageSize"], out var ps) ? Math.Clamp(ps, 1, 500) : 50;
        var service = _serviceProvider.GetRequiredService<DataService>();
        await WriteJsonAsync(ctx, 200, new
        {
            total = service.GetRecordCount(),
            page,
            pageSize,
            records = service.GetRecords(page, pageSize)
        });
    }

    private async Task HandleHistoryDeleteAsync(HttpListenerContext ctx)
    {
        var id = int.TryParse(ctx.Request.QueryString["id"], out var v) ? v : -1;
        var service = _serviceProvider.GetRequiredService<DataService>();
        if (id > 0)
        {
            service.DeleteRecord(id);
        }
        else if (string.Equals(ctx.Request.QueryString["all"], "true", StringComparison.OrdinalIgnoreCase))
        {
            service.ClearAllRecords();
        }
        else
        {
            await WriteJsonAsync(ctx, 400, new { error = "A valid id or all=true is required" });
            return;
        }
        await WriteJsonAsync(ctx, 200, new { ok = true });
    }

    private object GetSettings()
    {
        var options = _serviceProvider.GetRequiredService<SpeedTestOptions>();
        return new
        {
            options.ThreadCount,
            options.TestTimeoutSec,
            options.AverageDelaySec,
            options.RateWindowSec,
            options.NicPollIntervalMs,
            options.ThreadRampUpMs,
            options.LatencyPollIntervalMs,
            options.JitterTargetHost,
            options.JitterPollIntervalMs,
            options.PacketLossTargetHost,
            options.PacketLossPollIntervalMs,
            options.CompensationEnabled,
            options.CompensationThreshold,
            options.CompensationConfirmSec,
            options.AdaptiveThreadsEnabled,
            theme = ThemeService.Current.ToString(),
            language = LocalizationService.Current.ToString(),
            webServerEnabled = Enabled
        };
    }

    private async Task HandleSettingsPostAsync(HttpListenerContext ctx)
    {
        try
        {
            var body = await ReadBodyAsync(ctx.Request);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var options = _serviceProvider.GetRequiredService<SpeedTestOptions>();

            if (root.TryGetProperty("threadCount", out var threadCount)) options.ThreadCount = Math.Clamp(threadCount.GetInt32(), 2, 1024);
            if (root.TryGetProperty("testTimeoutSec", out var testTimeoutSec)) options.TestTimeoutSec = Math.Clamp(testTimeoutSec.GetInt32(), 5, 600);
            if (root.TryGetProperty("averageDelaySec", out var averageDelaySec)) options.AverageDelaySec = Math.Clamp(averageDelaySec.GetInt32(), 1, 30);
            if (root.TryGetProperty("rateWindowSec", out var rateWindowSec)) options.RateWindowSec = Math.Clamp(rateWindowSec.GetDouble(), 0.5, 10);
            if (root.TryGetProperty("nicPollIntervalMs", out var nicPollIntervalMs)) options.NicPollIntervalMs = Math.Clamp(nicPollIntervalMs.GetInt32(), 200, 5000);
            if (root.TryGetProperty("threadRampUpMs", out var threadRampUpMs)) options.ThreadRampUpMs = Math.Clamp(threadRampUpMs.GetInt32(), 0, 5000);
            if (root.TryGetProperty("latencyPollIntervalMs", out var latencyPollIntervalMs)) options.LatencyPollIntervalMs = Math.Clamp(latencyPollIntervalMs.GetInt32(), 500, 10000);
            if (root.TryGetProperty("jitterTargetHost", out var jitterTargetHost)) options.JitterTargetHost = jitterTargetHost.GetString() ?? options.JitterTargetHost;
            if (root.TryGetProperty("jitterPollIntervalMs", out var jitterPollIntervalMs)) options.JitterPollIntervalMs = Math.Clamp(jitterPollIntervalMs.GetInt32(), 500, 5000);
              if (root.TryGetProperty("packetLossTargetHost", out var packetLossTargetHost)) options.PacketLossTargetHost = packetLossTargetHost.GetString() ?? options.PacketLossTargetHost;
              if (root.TryGetProperty("packetLossPollIntervalMs", out var packetLossPollIntervalMs)) options.PacketLossPollIntervalMs = Math.Clamp(packetLossPollIntervalMs.GetInt32(), 500, 5000);
            if (root.TryGetProperty("compensationEnabled", out var compensationEnabled)) options.CompensationEnabled = compensationEnabled.GetBoolean();
            if (root.TryGetProperty("compensationThreshold", out var compensationThreshold)) options.CompensationThreshold = Math.Clamp(compensationThreshold.GetDouble(), 0.3, 0.8);
            if (root.TryGetProperty("compensationConfirmSec", out var compensationConfirmSec)) options.CompensationConfirmSec = Math.Clamp(compensationConfirmSec.GetInt32(), 1, 10);
            if (root.TryGetProperty("adaptiveThreadsEnabled", out var adaptiveThreadsEnabled)) options.AdaptiveThreadsEnabled = adaptiveThreadsEnabled.GetBoolean();

            if (root.TryGetProperty("theme", out var theme))
            {
                var t = theme.GetString();
                var mode = t == nameof(ThemeMode.Light) ? ThemeMode.Light : ThemeMode.Dark;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ThemeService.Apply(mode);
                    ThemeService.Save(mode);
                });
            }

            if (root.TryGetProperty("language", out var language))
            {
                var l = language.GetString();
                var mode = l == nameof(LanguageMode.EnUS) ? LanguageMode.EnUS : LanguageMode.ZhCN;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LocalizationService.Apply(mode);
                    LocalizationService.Save(mode);
                });
            }

            if (root.TryGetProperty("webServerEnabled", out var webServerEnabled))
            {
                var enabled = webServerEnabled.GetBoolean();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SetEnabled(enabled);
                    SaveEnabled(enabled);
                });
            }

            PersistSpeedOptions(options);
            await WriteJsonAsync(ctx, 200, new { ok = true });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(ctx, 400, new { error = ex.Message });
        }
    }

    private static void PersistSpeedOptions(SpeedTestOptions options)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "appsettings.json");

            JsonObject root;
            if (File.Exists(path))
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var speed = root["SpeedTest"] as JsonObject ?? new JsonObject();
            speed["ThreadCount"] = options.ThreadCount;
            speed["TestTimeoutSec"] = options.TestTimeoutSec;
            speed["AverageDelaySec"] = options.AverageDelaySec;
            speed["RateWindowSec"] = options.RateWindowSec;
            speed["NicPollIntervalMs"] = options.NicPollIntervalMs;
            speed["ThreadRampUpMs"] = options.ThreadRampUpMs;
            speed["LatencyPollIntervalMs"] = options.LatencyPollIntervalMs;
            speed["JitterTargetHost"] = options.JitterTargetHost;
            speed["JitterPollIntervalMs"] = options.JitterPollIntervalMs;
            speed["PacketLossTargetHost"] = options.PacketLossTargetHost;
            speed["PacketLossPollIntervalMs"] = options.PacketLossPollIntervalMs;
            speed["CompensationEnabled"] = options.CompensationEnabled;
            speed["CompensationThreshold"] = options.CompensationThreshold;
            speed["CompensationConfirmSec"] = options.CompensationConfirmSec;
            speed["AdaptiveThreadsEnabled"] = options.AdaptiveThreadsEnabled;
            speed["AdaptiveStartThreads"] = options.AdaptiveStartThreads;
            root["SpeedTest"] = speed;

            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Log($"PersistSpeedOptions failed: {ex.Message}");
        }
    }


    private async Task HandleTestStartAsync(HttpListenerContext ctx)
    {
        try
        {
            var body = await ReadBodyAsync(ctx.Request);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var mode = root.TryGetProperty("mode", out var m) ? m.GetString() : "download";
            var adapterIds = ReadStringList(root, "adapterIds");
            var urlList = ReadStringList(root, "urls");
            foreach (var u in urlList)
            {
                if (!await IsPublicHttpUrlAsync(u))
                    throw new InvalidDataException("Only public http/https URLs are allowed");
            }

            var vm = GetMainViewModel();
            if (vm.IsTesting)
            {
                await WriteJsonAsync(ctx, 409, new { error = "already testing" });
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (adapterIds.Count > 0)
                {
                    foreach (var item in vm.AdapterSelectionItems)
                        item.IsSelected = adapterIds.Contains(item.Adapter.Id);
                }

                if (urlList.Count > 0)
                {
                    if (mode == "upload" && vm.SelectedProfile != null)
                    {
                        vm.SelectedProfile.UploadUrls = urlList;
                    }
                    else
                    {
                        if (vm.SelectedProfile != null) vm.SelectedProfile.DownloadUrls = urlList;
                        vm.UrlSelectionItems = new ObservableCollection<UrlSelectionItem>(
                            urlList.Select(u => new UrlSelectionItem { Url = u, IsSelected = true }));
                    }
                }

                IRelayCommand? command = mode switch
                {
                    "upload" => vm.StartUploadTestCommand,
                    "full" => vm.StartFullTestCommand,
                    _ => vm.StartDownloadTestCommand
                };
                if (command?.CanExecute(null) == true) command.Execute(null);
            });

            await WriteJsonAsync(ctx, 200, new { ok = true });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(ctx, 400, new { error = ex.Message });
        }
    }

    private async Task HandleTestStopAsync(HttpListenerContext ctx)
    {
        var vm = GetMainViewModel();
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (vm.CancelTestCommand.CanExecute(null)) vm.CancelTestCommand.Execute(null);
        });
        await WriteJsonAsync(ctx, 200, new { ok = true });
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxRequestBodyChars)
            throw new InvalidDataException("Request body too large");

        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var buffer = new char[4096];
        var sb = new StringBuilder();
        var total = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0) break;
            total += read;
            if (total > MaxRequestBodyChars)
                throw new InvalidDataException("Request body too large");
            sb.Append(buffer, 0, read);
        }
        return sb.ToString();
    }

    private static async Task WriteJsonAsync(HttpListenerContext ctx, int statusCode, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.OutputStream.Close();
    }

    private async Task ServeStaticAsync(HttpListenerContext ctx, string path)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (path == "/") path = "/index.html";
        var relative = path.TrimStart('/');
        var relativeFile = relative.Replace('/', Path.DirectorySeparatorChar);
        var file = Path.GetFullPath(Path.Combine(root, relativeFile));
        byte[]? bytes;
        if (file.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) && File.Exists(file))
        {
            bytes = await File.ReadAllBytesAsync(file);
        }
        else
        {
            bytes = TryReadEmbeddedFile(relative);
            if (bytes == null)
            {
                await WriteJsonAsync(ctx, 404, new { error = "Not Found" });
                return;
            }
        }
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var contentType = ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes!.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.OutputStream.Close();
    }

    private static byte[]? TryReadEmbeddedFile(string relative)
    {
        var resourceName = "NetSpeedTest.wwwroot." + relative.Replace('/', '.').Replace('\\', '.');
        using var stream = typeof(WebServerService).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }


    private void EnsureWwwRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Directory.CreateDirectory(root);
        ExtractEmbeddedWwwRoot(root);

        var indexPath = Path.Combine(root, "index.html");
        if (!File.Exists(indexPath))
        {
            File.WriteAllText(indexPath, DefaultIndexHtml);
        }
    }

    /// <summary>
    /// 将嵌入 exe 的 wwwroot 资源释放到输出目录（已存在的文件视为用户自定义覆盖，不覆盖）。
    /// </summary>
    private void ExtractEmbeddedWwwRoot(string root)
    {
        const string prefix = "NetSpeedTest.wwwroot.";
        var assembly = typeof(WebServerService).Assembly;
        var rootFull = Path.GetFullPath(root);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = resourceName.Substring(prefix.Length);
            var dot = relative.LastIndexOf('.');
            if (dot <= 0)
                continue;

            var relativePath = relative.Substring(0, dot).Replace('.', '/') + relative.Substring(dot);
            var target = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || File.Exists(target))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                continue;

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var fs = File.Create(target);
            stream.CopyTo(fs);
        }
    }

    private const string DefaultIndexHtml = """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>NetSpeedTest Web</title>
  <style>
    body { font-family: Segoe UI, Microsoft YaHei, sans-serif; background:#0d1117; color:#e6edf3; margin:0; padding:24px; }
    h1 { font-size:22px; } h2 { font-size:16px; }
    .card { background:#161b22; border:1px solid #30363d; border-radius:10px; padding:16px; margin:12px 0; }
    button { background:#21262d; color:#e6edf3; border:1px solid #30363d; border-radius:6px; padding:8px 14px; margin:4px; cursor:pointer; }
    button:hover { background:#292e36; }
    .row { display:flex; gap:12px; flex-wrap:wrap; align-items:center; }
    .value { font-size:24px; font-weight:700; }
    .muted { color:#7d8590; font-size:12px; }
    table { width:100%; border-collapse:collapse; margin-top:10px; }
    td,th { text-align:left; padding:6px 8px; border-bottom:1px solid #30363d; font-size:13px; }
  </style>
</head>
<body>
  <h1>NetSpeedTest Web</h1>
  <div class="card">
    <h2>状态</h2>
    <div class="row">
      <div><div class="muted">状态</div><div id="status" class="value">--</div></div>
      <div><div class="muted">下载</div><div id="download" class="value">--</div></div>
      <div><div class="muted">上传</div><div id="upload" class="value">--</div></div>
      <div><div class="muted">延迟</div><div id="latency" class="value">--</div></div>
    </div>
    <div class="row">
      <button onclick="startTest('download')">下载测速</button>
      <button onclick="startTest('upload')">上传测速</button>
      <button onclick="startTest('full')">双向测速</button>
      <button onclick="stopTest()">停止</button>
    </div>
  </div>
  <div class="card">
    <h2>网卡</h2>
    <div id="adapters"></div>
  </div>
  <div class="card">
    <h2>历史记录</h2>
    <div id="history"></div>
  </div>
  <script>
    async function api(path, options){ const r = await fetch(path, options); return r.json(); }
    async function refresh(){
      const s = await api('/api/status');
      document.getElementById('status').textContent = s.status || (s.running ? '测速中' : '就绪');
      document.getElementById('download').textContent = fmt(s.downloadMbps);
      document.getElementById('upload').textContent = fmt(s.uploadMbps);
      document.getElementById('latency').textContent = s.latencyMs == null ? '--' : s.latencyMs + ' ms';
      const ads = await api('/api/adapters');
      document.getElementById('adapters').innerHTML = ads.map(a => `<label><input type="checkbox" data-id="${a.id}" ${a.selected?'checked':''}/> ${a.name} · ${a.ip||'无IP'}</label>`).join('<br/>');
      const h = await api('/api/history?page=1&pageSize=10');
      document.getElementById('history').innerHTML = '<table><tr><th>时间</th><th>下载</th><th>上传</th><th>网卡</th></tr>' + (h.records||[]).map(r => `<tr><td>${r.timestamp}</td><td>${fmt(r.downloadMbps)}</td><td>${fmt(r.uploadMbps)}</td><td>${r.networkAdapterName}</td></tr>`).join('') + '</table>';
    }
    function fmt(v){ return v == null ? '--' : (v >= 1000 ? (v/1000).toFixed(2)+' Gbps' : v >= 1 ? v.toFixed(2)+' Mbps' : (v*1000).toFixed(0)+' Kbps'); }
    async function startTest(mode){
      const ids=[...document.querySelectorAll('#adapters input:checked')].map(x=>x.dataset.id);
      await api('/api/test/start',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mode,adapterIds:ids})});
    }
    async function stopTest(){ await api('/api/test/stop',{method:'POST'}); }
    setInterval(refresh,1000); refresh();
  </script>
</body>
</html>
""";
}
