using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    public int CurrentPort { get; private set; } = 8080;
    public WebServerPortMode PortMode { get; private set; } = WebServerPortMode.Auto;
    public int CustomPort { get; private set; } = 8080;


    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const int MaxRequestBodyChars = 256 * 1024;
    private static readonly SemaphoreSlim RequestGate = new(64, 64);

    /// <summary>
    /// 写操作（POST/DELETE）必须携带的会话令牌请求头名称。
    /// </summary>
    internal const string TokenHeaderName = "X-NST-Token";

    /// <summary>
    /// index.html 中用于注入会话令牌的占位符，避免把令牌硬编码进静态资源。
    /// </summary>
    internal const string TokenPlaceholder = "%%NST_TOKEN%%";

    /// <summary>
    /// index.html 中用于注入版本号的占位符，版本号唯一来源为程序集版本。
    /// </summary>
    internal const string VersionPlaceholder = "%%NST_VERSION%%";

    /// <summary>
    /// 请求级超时，防止慢连接长期占用 RequestGate。
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 等待 UI 线程完成操作的超时时间。
    /// </summary>
    internal static readonly TimeSpan DispatcherWaitTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// FN-01：确认测速真正启动的最长等待时间。必须长于命令内部 15s 的 URL 预探测上限。
    /// </summary>
    internal static readonly TimeSpan TestStartConfirmationTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// 每次启动生成的随机会话令牌。回环访问免校验，非回环写操作必须携带。
    /// </summary>
    internal static readonly string SessionToken = GenerateSessionToken();

    private static string GenerateSessionToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// 恒定时间比较，避免通过响应时间侧信道逐字节猜测令牌。
    /// </summary>
    internal static bool IsTokenValid(string? provided)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        var expected = Encoding.UTF8.GetBytes(SessionToken);
        var actual = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>
    /// 在 UI 线程执行操作，带超时。返回 false 表示 UI 线程不可用或超时，
    /// 调用方必须按失败处理，不能把未生效的操作当成功返回。
    /// </summary>
    internal static bool TryRunOnUiThread(Action action, TimeSpan timeout)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return false;
        if (dispatcher.CheckAccess())
        {
            action();
            return true;
        }

        try
        {
            var task = dispatcher.InvokeAsync(action);
            return task.Task.Wait(timeout) && task.Task.IsCompletedSuccessfully;
        }
        catch (AggregateException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    internal static bool TryRunOnUiThread(Action action) => TryRunOnUiThread(action, DispatcherWaitTimeout);

    /// <summary>
    /// 在 UI 线程执行操作并取回结果，带超时；超时返回 default 而不是继续等待。
    /// </summary>
    internal static T? TryRunOnUiThread<T>(Func<T> func, TimeSpan timeout) where T : class
    {
        T? result = null;
        var ok = TryRunOnUiThread(() => { result = func(); }, timeout);
        return ok ? result : null;
    }

    internal static T? TryRunOnUiThread<T>(Func<T> func) where T : class
        => TryRunOnUiThread(func, DispatcherWaitTimeout);

    /// <summary>
    /// 统一的错误响应文案：对外只暴露分类信息，细节写入日志，避免泄露内部实现。
    /// </summary>
    internal static object ErrorPayload(int statusCode) => statusCode switch
    {
        400 => new { error = "Bad Request", message = "The request could not be processed" },
        401 => new { error = "Unauthorized", message = "A valid session token is required" },
        403 => new { error = "Forbidden", message = "Remote IP is not in a local subnet" },
        500 => new { error = "Internal Server Error", message = "The request could not be completed" },
        _ => new { error = "Request Failed", message = "The request could not be processed" }
    };

    /// <summary>
    /// 请求级取消：超时后中止本次响应，避免不再需要的连接继续占用资源。
    /// </summary>
    private static void AbortOnCancellation(HttpListenerContext ctx, CancellationToken ct)
    {
        if (!ct.CanBeCanceled) return;
        ct.Register(static state =>
        {
            var context = (HttpListenerContext)state!;
            try { context.Response.Abort(); } catch { }
        }, ctx);
    }

    /// <summary>
    /// 判断请求是否来自回环地址。
    /// </summary>
    internal static bool IsLoopbackRequest(IPEndPoint? endpoint)
    {
        if (endpoint == null) return false;
        var address = endpoint.Address;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address);
    }

    private readonly IServiceProvider _serviceProvider;
    private readonly NetworkMonitorService _networkMonitor;
    private bool _usingWildcardListener;
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
    private readonly object _optionsGate = new();
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

    public IReadOnlyList<AdapterAccessBinding> Bindings => BindingsSnapshot();

    /// <summary>
    /// 最近一次启动失败信息；成功启动或停止后清空。
    /// </summary>
    public string LastError { get; private set; } = "";

    public event Action? StateChanged;

    /// <summary>
    /// 局域网绑定列表发生变化时触发。
    /// </summary>
    public event Action? BindingsChanged;

    public WebServerService(IServiceProvider serviceProvider, NetworkMonitorService networkMonitorService)
    {
        _serviceProvider = serviceProvider;
        _networkMonitor = networkMonitorService;
        _networkMonitor.NetworkChanged += OnNetworkChanged;
        var settings = LoadSettings();
        _allowLanAccess = settings.AllowLanAccess;
        PortMode = settings.PortMode;
        CustomPort = settings.CustomPort;
        CurrentPort = settings.LastActualPort;
        _bindings = BuildAdapterBindings(CurrentPort);
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

    public void SetPortMode(WebServerPortMode mode, int customPort)
    {
        var oldMode = PortMode;
        var oldCustomPort = CustomPort;
        var oldCurrentPort = CurrentPort;
        var wasEnabled = _enabled;

        if (mode == WebServerPortMode.Custom && !PortHelper.IsValidPort(customPort))
            throw new ArgumentOutOfRangeException(nameof(customPort), "端口范围必须在 1024~65535");

        PortMode = mode;
        CustomPort = customPort;
        SaveSettings();

        if (!wasEnabled)
        {
            StateChanged?.Invoke();
            return;
        }

        Stop();
        Start();

        if (!_enabled)
        {
            var error = LastError;
            PortMode = oldMode;
            CustomPort = oldCustomPort;
            CurrentPort = oldCurrentPort;
            SaveSettings();
            Start();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "端口启动失败" : error);
        }
    }
        public void Start()
    {
        lock (_gate)
        {
            if (_listeners.Count > 0) return;

            try
            {
                _aclReady = false;
                _firewallReady = false;
                _lanReady = false;
                _lanError = "";

                Exception? lastError = null;
                var startedPort = -1;

                foreach (var port in GetPortCandidates())
                {
                    if (TryStartAtPort(port, out lastError))
                    {
                        startedPort = port;
                        break;
                    }
                }

                if (startedPort < 0)
                    throw new InvalidOperationException(lastError?.Message ?? "没有可用端口");

                CurrentPort = startedPort;
                SaveSettings();
                LastError = "";
                Logger.Log(_lanReady
                    ? $"Web server started on http://127.0.0.1:{CurrentPort} with LAN subnet mapping"
                    : $"Web server started loopback-only on http://127.0.0.1:{CurrentPort}");
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
    private IEnumerable<int> GetPortCandidates()
    {
        var seen = new HashSet<int>();
        if (PortMode == WebServerPortMode.Custom)
        {
            if (PortHelper.IsValidPort(CustomPort)) yield return CustomPort;
            yield break;
        }

        var preferred = PortHelper.IsValidPort(CurrentPort) ? CurrentPort : PortHelper.DefaultPort;
        foreach (var port in PortHelper.GetCandidates(preferred))
        {
            if (seen.Add(port)) yield return port;
        }
    }

    private bool TryStartAtPort(int port, out Exception? error)
    {
        error = null;
        try
        {
            _bindings = BuildAdapterBindings(port);
            _bindingsBuiltAtUtc = DateTime.UtcNow;

            var loopback = StartListener($"http://127.0.0.1:{port}/", $"http://localhost:{port}/");
            if (loopback == null)
            {
                error = new InvalidOperationException($"无法监听 127.0.0.1:{port}");
                return false;
            }

            _listeners.Add(loopback);

            if (_allowLanAccess)
                StartLanListeners(port);

            _enabled = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            foreach (var listener in _listeners.ToArray())
            {
                var current = listener;
                _ = Task.Run(() => ListenLoopAsync(current, ct));
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            CleanupListeners();
            _enabled = false;
            _lanReady = false;
            return false;
        }
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
        var settings = LoadSettings();
        _allowLanAccess = settings.AllowLanAccess;
        PortMode = settings.PortMode;
        CustomPort = settings.CustomPort;
        CurrentPort = settings.LastActualPort;
        if (settings.Enabled) Start();
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

    private void StartLanListeners(int port)
    {
        if (_bindings.Count == 0)
        {
            _lanError = LocalizationService.Get("WebServer_NoBindings");
            return;
        }
        _usingWildcardListener = false;

        try
        {
            EnsureUrlAcl(port);
        }
        catch (Exception ex)
        {
            _lanError = ex.Message;
            Logger.Log($"URL ACL setup failed: {ex.Message}");
        }

        try
        {
            EnsureFirewallRule(port);
            _firewallReady = true;
        }
        catch (Exception ex)
        {
            _firewallReady = false;
            if (string.IsNullOrWhiteSpace(_lanError)) _lanError = ex.Message;
            Logger.Log($"Firewall rule setup failed: {ex.Message}");
        }

        // 优先通配符监听：网卡/网段变化后无需重设 ACL
        var wildcard = StartListener($"http://+:{port}/");
        if (wildcard != null)
        {
            _listeners.Add(wildcard);
            _usingWildcardListener = true;
            _lanReady = true;
            _aclReady = true;
            _lanError = "";
            return;
        }

        // 回退：逐网卡监听其本机 IP
        var lanCount = 0;
        foreach (var binding in _bindings)
        {
            var listener = StartListener($"http://{binding.IPAddress}:{port}/");
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

    private void EnsureUrlAcl(int port)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        RunNetsh($"http add urlacl url=http://+:{port}/ user=\"{user}\"");
    }

    private void EnsureFirewallRule(int port)
    {
        const string ruleName = "NetSpeedTest Web Server";
        try
        {
            try { RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\""); } catch { }
            return;
        }
        catch { }

        RunNetsh($"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any");
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

    private sealed class WebServerSettings
    {
        public bool Enabled { get; set; }
        public bool AllowLanAccess { get; set; } = true;
        public WebServerPortMode PortMode { get; set; } = WebServerPortMode.Auto;
        public int CustomPort { get; set; } = PortHelper.DefaultPort;
        public int LastActualPort { get; set; } = PortHelper.DefaultPort;
    }

    /// <summary>
    /// F-10：原子写入文本文件（临时文件 + 替换），避免中断留下半截内容。
    /// </summary>
    internal static void AtomicWriteAllText(string path, string content)
    {
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
            tempPath = null;
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    /// <summary>
    /// F-10：读取文本文件，容忍替换瞬间的共享冲突重试。
    /// 若直接放弃，用户设置会被静默丢弃（App 启动时尤其危险）。
    /// </summary>
    internal static string? TryReadAllTextWithRetry(string path, int attempts = 4)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < attempts - 1)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }

    private WebServerSettings LoadSettings()
    {
        var result = new WebServerSettings();
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            var path = Path.Combine(dir, "web.json");
            if (!File.Exists(path)) return result;

            var json = TryReadAllTextWithRetry(path);
            if (string.IsNullOrWhiteSpace(json)) return result;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            result.Enabled = root.TryGetProperty("Enabled", out var e) && e.GetBoolean();
            result.AllowLanAccess = !root.TryGetProperty("AllowLanAccess", out var a) || a.GetBoolean();

            var modeText = root.TryGetProperty("PortMode", out var pm) ? pm.GetString() : null;
            if (!Enum.TryParse<WebServerPortMode>(modeText, true, out var mode))
                mode = WebServerPortMode.Auto;
            result.PortMode = mode;

            var customPort = root.TryGetProperty("CustomPort", out var cp) && cp.TryGetInt32(out var c)
                ? c
                : PortHelper.DefaultPort;
            result.CustomPort = PortHelper.ClampPort(customPort);

            var lastPort = root.TryGetProperty("LastActualPort", out var lp) && lp.TryGetInt32(out var l)
                ? l
                : PortHelper.DefaultPort;
            result.LastActualPort = PortHelper.ClampPort(lastPort);
        }
        catch (Exception ex)
        {
            Logger.Log($"Web state load failed: {ex.Message}");
        }
        return result;
    }

    private void SaveSettings(bool? enabled = null)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "web.json");
            var target = enabled ?? _enabled;
            AtomicWriteAllText(path, JsonSerializer.Serialize(new
            {
                Enabled = target,
                AllowLanAccess = _allowLanAccess,
                PortMode = PortMode.ToString(),
                CustomPort = CustomPort,
                LastActualPort = CurrentPort
            }));
        }
        catch (Exception ex)
        {
            Logger.Log($"Web state save failed: {ex.Message}");
        }
    }
    private List<AdapterAccessBinding> BuildAdapterBindings(int port)
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
                        Url = $"http://{unicast.Address}:{port}/"
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

    /// <summary>
    /// 取绑定列表快照。集合会被网络变化事件整体替换，读取方必须在锁内取快照再使用。
    /// </summary>
    private List<AdapterAccessBinding> BindingsSnapshot()
    {
        lock (_bindingsGate) { return _bindings.ToList(); }
    }


    private void RefreshBindingsIfStale(bool force = false)
    {
        lock (_bindingsGate)
        {
            var oldSignature = string.Join("|", _bindings.Select(b => $"{b.IPAddress}/{b.Subnet}"));
            if (!force && (DateTime.UtcNow - _bindingsBuiltAtUtc).TotalSeconds < 5) return;
            _bindings = BuildAdapterBindings(CurrentPort);
            var newSignature = string.Join("|", _bindings.Select(b => $"{b.IPAddress}/{b.Subnet}"));
            _bindingsBuiltAtUtc = DateTime.UtcNow;
            if (!string.Equals(oldSignature, newSignature, StringComparison.Ordinal))
                BindingsChanged?.Invoke();


        }
    }

    private void OnNetworkChanged()
    {
        try
        {
            RefreshBindingsIfStale(force: true);

            // 通配符监听不依赖具体 IP；回退逐 IP 模式需要重新建立监听器。
            if (_enabled && _allowLanAccess && !_usingWildcardListener)
            {
                try
                {
                    Stop();
                    Start();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Web server rebind failed: {ex.Message}");
                }
            }
            else
            {
                StateChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Web server network refresh failed: {ex.Message}");
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
        foreach (var binding in BindingsSnapshot())
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
                // F-12：请求级超时与服务器生命周期解耦，关闭服务不会直接掐断在途响应。
                using var requestCts = new CancellationTokenSource(RequestTimeout);
                var entered = false;
                try
                {
                    await RequestGate.WaitAsync(requestCts.Token);
                    entered = true;
                    await HandleContextAsync(ctx, requestCts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Log($"Unhandled web request error: {ex}");
                }
                finally
                {
                    if (entered) RequestGate.Release();
                }
            });
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken requestCt)
    {
        try
        {
            ApplySecurityHeaders(ctx);
            AbortOnCancellation(ctx, requestCt);

            if (!IsRemoteAllowed(ctx.Request.RemoteEndPoint))
            {
                await WriteJsonAsync(ctx, 403, ErrorPayload(403), requestCt);
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod;

            // F-01：非回环的写操作必须携带会话令牌，防止局域网/浏览器跨源静默触发测速或删数据。
            if (!IsLoopbackRequest(ctx.Request.RemoteEndPoint) && IsStateChangingMethod(method))
            {
                var provided = ctx.Request.Headers[TokenHeaderName];
                if (!IsTokenValid(provided))
                {
                    Logger.Log($"Web request rejected: missing or invalid {TokenHeaderName} from {ctx.Request.RemoteEndPoint}");
                    await WriteJsonAsync(ctx, 403, ErrorPayload(403), requestCt);
                    return;
                }
            }

            if (method == "GET" && (path == "/" || path == "/index.html" || path.StartsWith("/assets/")))
            {
                await ServeStaticAsync(ctx, path, requestCt);
                return;
            }

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleApiAsync(ctx, method, path, requestCt);
                return;
            }

            await WriteJsonAsync(ctx, 404, new { error = "Not Found" }, requestCt);
        }
        catch (JsonException ex)
        {
            // 格式错误的请求体属于客户端问题，固定回 400 而不是 500。
            Logger.Log($"Malformed request body rejected: {ex.Message}");
            await SafeWriteErrorAsync(ctx, 400, CancellationToken.None);
        }
        catch (InvalidDataException ex)
        {
            Logger.Log($"Invalid request body rejected: {ex.Message}");
            await SafeWriteErrorAsync(ctx, 400, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Log($"Web request failed: {ex.Message}");
            await SafeWriteErrorAsync(ctx, 500, CancellationToken.None);
        }
    }

    /// <summary>
    /// 需要令牌校验的写操作。
    /// </summary>
    internal static bool IsStateChangingMethod(string? method)
        => string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
           || string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase)
           || string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)
           || string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// FN-06：对所有响应统一追加安全响应头。
    /// </summary>
    private static void ApplySecurityHeaders(HttpListenerContext ctx)
    {
        try
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["X-Frame-Options"] = "DENY";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        }
        catch (Exception ex)
        {
            Logger.Log($"Security header apply failed: {ex.Message}");
        }
    }

    private async Task HandleApiAsync(HttpListenerContext ctx, string method, string path, CancellationToken ct)
    {
        switch (path.ToLowerInvariant())
        {
            case "/api/status":
                await WriteJsonAsync(ctx, 200, GetStatus(), ct);
                return;
            case "/api/adapters":
                await WriteJsonAsync(ctx, 200, GetAdapters(), ct);
                return;
            case "/api/adapters/select":
                if (method == "POST") { await HandleAdaptersSelectAsync(ctx, ct); return; }
                break;
            case "/api/profiles":
                if (method == "GET") { await WriteJsonAsync(ctx, 200, GetProfiles(), ct); return; }
                if (method == "POST") { await HandleProfilesPostAsync(ctx, ct); return; }
                break;
            case "/api/history":
                if (method == "GET") { await HandleHistoryAsync(ctx, ct); return; }
                if (method == "DELETE") { await HandleHistoryDeleteAsync(ctx, ct); return; }
                break;
            case "/api/settings":
                if (method == "GET") { await WriteJsonAsync(ctx, 200, GetSettings(), ct); return; }
                if (method == "POST") { await HandleSettingsPostAsync(ctx, ct); return; }
                break;
            case "/api/test/start":
                if (method == "POST") { await HandleTestStartAsync(ctx, ct); return; }
                break;
            case "/api/test/stop":
                if (method == "POST") { await HandleTestStopAsync(ctx, ct); return; }
                break;
            case "/api/server":
                if (method == "GET") { await WriteJsonAsync(ctx, 200, GetServerInfo(), ct); return; }
                break;
        }

        await WriteJsonAsync(ctx, 404, new { error = "Not Found" }, ct);
    }


    private object GetServerInfo()
    {
        RefreshBindingsIfStale();
        return new
        {
            enabled = Enabled,
            portMode = PortMode.ToString(),
            customPort = CustomPort,
            port = CurrentPort,
            url = $"http://127.0.0.1:{CurrentPort}",
            lanAccess = AllowLanAccess,
            lanReady = LanReady,
            aclReady = AclReady,
            firewallReady = FirewallReady,
            // F-21：lanError 原始文本可能包含 netsh 输出、用户/域与 ACL 细节，对外脱敏。
            lanError = string.IsNullOrWhiteSpace(LanError) ? "" : "LAN setup incomplete; see application log",
            bindings = BindingsSnapshot().Select(b => new
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

    private async Task HandleAdaptersSelectAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var body = await ReadBodyAsync(ctx.Request, ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var ids = ReadStringList(root, "adapterIds");

            var vm = GetMainViewModel();
            var validIds = vm.Adapters.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selected = ids.Where(validIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (selected.Count == 0)
            {
                await WriteJsonAsync(ctx, 400, new { error = "At least one valid adapter id is required" }, ct);
                return;
            }

            var applied = TryRunOnUiThread(() =>
            {
                foreach (var item in vm.AdapterSelectionItems)
                    item.IsSelected = selected.Contains(item.Adapter.Id);
            });

            if (!applied)
            {
                await WriteJsonAsync(ctx, 503, new { error = "UI unavailable", message = "Adapter selection could not be applied" }, ct);
                return;
            }

            await WriteJsonAsync(ctx, 200, new { ok = true, selectedAdapterIds = selected }, ct);
        }
        catch (Exception ex)
        {
            Logger.Log($"Adapter select failed: {ex}");
            await SafeWriteErrorAsync(ctx, 400, ct);
        }
    }

    /// <summary>
    /// F-09：状态快照。所有 ViewModel 集合（AllAdapterRates / AdapterSelectionItems）
    /// 必须在 UI 线程内一次性投影为普通数组，避免与 UI 线程并发枚举 ObservableCollection。
    /// </summary>
    internal object GetStatus()
    {
        var vm = GetMainViewModel();

        var payload = TryRunOnUiThread<object>(() => new
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
            adapterMetrics = GetAdapterMetricsSnapshot(vm),
            packetLossScope = "global",
            serverPort = CurrentPort,
            serverUrl = $"http://127.0.0.1:{CurrentPort}",
            selectedAdapters = GetSelectedAdapterIds(),
            currentProfile = vm.SelectedProfile?.Name,
            recentResult = vm.HasRecentResult ? new
            {
                downloadMbps = vm.RecentDownloadMbps,
                uploadMbps = vm.RecentUploadMbps,
                latencyMs = vm.RecentLatencyMs
            } : null

        });

        if (payload != null) return payload;

        Logger.Log("Status snapshot timed out on the UI thread; returning minimal status");
        return new
        {
            running = false,
            status = "unavailable",
            adapterMetrics = Array.Empty<object>(),
            selectedAdapters = Array.Empty<string>()
        };
    }

    private object[] GetAdapterMetricsSnapshot(MainViewModel vm)
    {
        return vm.AllAdapterRates.Select(r => new
        {
            adapterId = r.AdapterId,
            adapterName = r.Name,
            ipAddress = r.IpAddress,
            latencyMs = r.LatencyMs,
            wanLatencyMs = r.WanLatencyMs,
            jitterMs = r.JitterMs
        }).Cast<object>().ToArray();
    }

    /// <summary>
    /// F-09：已选网卡 ID 快照。必须在 UI 线程内投影，避免并发枚举 ObservableCollection。
    /// </summary>
    private List<string> GetSelectedAdapterIds()
    {
        var vm = GetMainViewModel();
        var ids = TryRunOnUiThread(() => vm.AdapterSelectionItems
            .Where(x => x.IsSelected)
            .Select(x => x.Adapter.Id)
            .ToList());
        return ids ?? new List<string>();
    }

    private object GetAdapters()
    {
        var vm = GetMainViewModel();
        var selectedIds = GetSelectedAdapterIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            selected = selectedIds.Contains(a.Id)
        });
    }

    private object GetProfiles()
    {
        var service = _serviceProvider.GetRequiredService<ProfileService>();
        return service.GetAllProfiles();
    }

    private async Task HandleProfilesPostAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var body = await ReadBodyAsync(ctx.Request, ct);
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

                await WriteJsonAsync(ctx, 200, new { ok = true }, ct);
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


            await WriteJsonAsync(ctx, 200, new { ok = true, id = profile.Id }, ct);
        }
        catch (Exception ex)
        {
            Logger.Log($"Profile save failed: {ex}");
            await SafeWriteErrorAsync(ctx, 400, ct);
        }
    }

    /// <summary>
    /// 允许测速使用的目标端口白名单。
    /// </summary>
    internal static readonly int[] AllowedSpeedTestPorts = { 80, 443 };

    /// <summary>
    /// 目标主机是否为外网地址。
    /// </summary>
    private static async Task<bool> IsPublicHttpUrlAsync(string url)
    {
        return (await ResolvePublicHttpTargetAsync(url, CancellationToken.None)).IsAllowed;
    }

    /// <summary>
    /// F-05：解析并校验测速目标。要求 http/https、端口在白名单内、主机名解析出的
    /// 所有地址都是公网地址。解析失败的地址按拒绝处理。
    /// </summary>
    internal static async Task<(bool IsAllowed, IPAddress[] Addresses)> ResolvePublicHttpTargetAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return (false, Array.Empty<IPAddress>());
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return (false, Array.Empty<IPAddress>());
        if (Array.IndexOf(AllowedSpeedTestPorts, uri.Port) < 0) return (false, Array.Empty<IPAddress>());
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return (false, Array.Empty<IPAddress>());

        if (IPAddress.TryParse(uri.Host, out var literal))
            return (!IsPrivateIp(literal), new[] { literal });

        IPAddress[] addrs;
        try
        {
            using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            dnsCts.CancelAfter(TimeSpan.FromSeconds(3));
            addrs = await Dns.GetHostAddressesAsync(uri.Host, dnsCts.Token);
        }
        catch (Exception ex)
        {
            Logger.Log($"DNS resolve for speed test target failed ({uri.Host}): {ex.Message}");
            return (false, Array.Empty<IPAddress>());
        }

        if (addrs.Length == 0) return (false, Array.Empty<IPAddress>());
        foreach (var addr in addrs)
            if (IsPrivateIp(addr)) return (false, addrs);

        return (true, addrs);
    }

    /// <summary>
    /// F-05：SSRF 黑名单。覆盖回环、RFC1918、链路本地、CGNAT、IETF 协议分配、
    /// 基准测试、文档与组播/保留段，以及 IPv6 唯一本地、链路本地与组播。
    /// </summary>
    internal static bool IsPrivateIp(IPAddress ip)
    {
        if (ip == null) return true;
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) return IsPrivateIp(ip.MapToIPv4());

        var b = ip.GetAddressBytes();
        if (b.Length == 4)
        {
            return b[0] == 0                                   // 0.0.0.0/8    "this network"
                || b[0] == 10                                  // 10.0.0.0/8
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // 100.64.0.0/10 CGNAT
                || (b[0] == 127)                               // 127.0.0.0/8
                || (b[0] == 169 && b[1] == 254)                // 169.254.0.0/16
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // 192.0.0.0/24
                || (b[0] == 192 && b[1] == 168)                // 192.168.0.0/16
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // 198.18.0.0/15
                || b[0] >= 224;                                // 224.0.0.0/4 组播 + 240.0.0.0/4 保留
        }

        if (b.Length == 16)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            if (b[0] == 0xfc || b[0] == 0xfd) return true;    // fc00::/7 唯一本地地址
            if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80) return true; // fe80::/10（兜底）
        }

        return false;
    }
    private static List<string> ReadStringList(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array) return new();
        return arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private async Task HandleHistoryAsync(HttpListenerContext ctx, CancellationToken ct)
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
        }, ct);
    }

    private async Task HandleHistoryDeleteAsync(HttpListenerContext ctx, CancellationToken ct)
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
            await WriteJsonAsync(ctx, 400, new { error = "A valid id or all=true is required" }, ct);
            return;
        }
        await WriteJsonAsync(ctx, 200, new { ok = true }, ct);
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
            options.IncludeVirtualAdapters,
            theme = ThemeService.Current.ToString(),
            language = LocalizationService.Current.ToString(),
            webServerEnabled = Enabled
        };
    }

    private async Task HandleSettingsPostAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var body = await ReadBodyAsync(ctx.Request, ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var options = _serviceProvider.GetRequiredService<SpeedTestOptions>();

            bool? requestedWebServerEnabled = null;

            // F-10：设置写入与持久化互斥，避免并发 POST 产生交错的半套配置。
            lock (_optionsGate)
            {
                requestedWebServerEnabled = ApplySettingsCore(root, options);
            }

            PersistSpeedOptions(options);

            // F-11：关闭 Web 服务器必须先回响应再停止，否则客户端拿到的是连接被重置。
            var stopAfterResponse = requestedWebServerEnabled == false && Enabled;
            if (requestedWebServerEnabled == true && !Enabled)
            {
                // 启动仍同步执行，失败要能回报给调用方。
                if (!TryRunOnUiThread(() => { SetEnabled(true); SaveEnabled(true); }))
                {
                    await WriteJsonAsync(ctx, 503, new { error = "UI unavailable", message = "Web server could not be started" }, ct);
                    return;
                }
            }

            await WriteJsonAsync(ctx, 200, new { ok = true }, ct);

            if (stopAfterResponse) DeferStop();
        }
        catch (Exception ex)
        {
            Logger.Log($"Settings update failed: {ex}");
            await SafeWriteErrorAsync(ctx, 400, ct);
        }
    }

    /// <summary>
    /// F-11：先让响应完成写出，再在后台停止服务器。
    /// 立即 Stop() 会关闭 HttpListener，导致调用方收到连接重置而不是 200。
    /// </summary>
    private void DeferStop()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // 200ms 是响应 flush 的宽限窗口，不是超时。
                await Task.Delay(200).ConfigureAwait(false);
                TryRunOnUiThread(() => { SetEnabled(false); SaveEnabled(false); });
            }
            catch (Exception ex)
            {
                Logger.Log($"Deferred web server stop failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// FN-09：错误响应自身也要容错，避免在已中止的连接上再次抛出。
    /// </summary>
    private static async Task SafeWriteErrorAsync(HttpListenerContext ctx, int statusCode, CancellationToken ct)
    {
        try
        {
            await WriteJsonAsync(ctx, statusCode, ErrorPayload(statusCode), ct);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to write error response {statusCode}: {ex.Message}");
            try { ctx.Response.Abort(); } catch { }
        }
    }

    /// <summary>
    /// F-10：设置应用逻辑。调用方必须持有 <see cref="_optionsGate"/>。
    /// 返回请求中显式指定的 webServerEnabled 值（未指定为 null），
    /// 由调用方在响应之后决定启停时机（F-11）。
    /// </summary>
    private bool? ApplySettingsCore(JsonElement root, SpeedTestOptions options)
    {
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
        if (root.TryGetProperty("includeVirtualAdapters", out var includeVirtualAdapters)) options.IncludeVirtualAdapters = includeVirtualAdapters.GetBoolean();

        if (root.TryGetProperty("theme", out var theme))
        {
            var t = theme.GetString();
            var mode = t == nameof(ThemeMode.Light) ? ThemeMode.Light : ThemeMode.Dark;
            TryRunOnUiThread(() =>
            {
                ThemeService.Apply(mode);
                ThemeService.Save(mode);
            });
        }

        if (root.TryGetProperty("language", out var language))
        {
            var l = language.GetString();
            var mode = l == nameof(LanguageMode.EnUS) ? LanguageMode.EnUS : LanguageMode.ZhCN;
            TryRunOnUiThread(() =>
            {
                LocalizationService.Apply(mode);
                LocalizationService.Save(mode);
            });
        }

        if (root.TryGetProperty("webServerEnabled", out var webServerEnabled))
            return webServerEnabled.GetBoolean();

        return null;
    }

    /// <summary>
    /// F-10：设置持久化。调用方必须持有 <see cref="_optionsGate"/>。
    /// 先写临时文件再原子替换，避免进程中断留下半截 JSON 导致下次启动配置丢失。
    /// </summary>
    private static void PersistSpeedOptions(SpeedTestOptions options)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
        PersistSpeedOptions(options, dir);
    }

    /// <summary>
    /// F-10 的可测实现：显式指定目标目录。
    /// </summary>
    internal static void PersistSpeedOptions(SpeedTestOptions options, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "appsettings.json");

            JsonObject root;
            if (File.Exists(path))
            {
                try
                {
                    var existing = TryReadAllTextWithRetry(path);
                    root = (string.IsNullOrWhiteSpace(existing) ? null : JsonNode.Parse(existing) as JsonObject)
                        ?? new JsonObject();
                }
                catch (JsonException ex)
                {
                    // 旧文件损坏时不要放弃写入，否则设置永远无法恢复。
                    Logger.Log($"Existing appsettings.json is not valid JSON, rewriting: {ex.Message}");
                    root = new JsonObject();
                }
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
            speed["IncludeVirtualAdapters"] = options.IncludeVirtualAdapters;
            speed["AdaptiveStartThreads"] = options.AdaptiveStartThreads;
            root["SpeedTest"] = speed;

            var payload = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            AtomicWriteAllText(path, payload);
        }
        catch (Exception ex)
        {
            Logger.Log($"PersistSpeedOptions failed: {ex.Message}");
        }
    }


    private async Task HandleTestStartAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var started = false;
        bool isApiInitiated = false;
        try
        {
            var body = await ReadBodyAsync(ctx.Request, ct);
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
                await WriteJsonAsync(ctx, 409, new { error = "already testing" }, ct);
                return;
            }

            var applied = TryRunOnUiThread(() =>
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
                if (command?.CanExecute(null) == true)
                {
                    // 标记为 API 发起：结束时跳过模态结果窗，保持 UI 线程可响应。
                    vm.MarkApiInitiatedTest();
                    isApiInitiated = true;
                    command.Execute(null);
                    started = true;
                }
            });

            // F-09：UI 线程不可用/超时按 503 处理，不能继续往下当成功返回。
            if (!applied)
            {
                Logger.Log("Test start aborted: UI thread did not complete the command within the timeout");
                await WriteJsonAsync(ctx, 503, new { error = "UI unavailable", message = "The UI thread did not respond in time" }, ct);
                return;
            }

            // FN-01：命令未真正执行时必须报 409，不能谎报 200 让调用方以为测速已启动。
            if (!started)
            {
                await WriteJsonAsync(ctx, 409, new { error = "test did not start", message = "Command could not be executed" }, ct);
                return;
            }

            // FN-01：命令已执行但测速尚未真正进入运行态时（命令内部有前置校验/准备阶段，
            // 例如 URL 预探测对话框），必须等待状态真的翻转，否则 200 会是假成功。
            if (!await WaitForTestRunningAsync(() => vm.IsTesting, ct))
            {
                Logger.Log("Test start returned 409: command ran but the test never entered the running state");
                await WriteJsonAsync(ctx, 409, new { error = "test did not start", message = "The test did not enter the running state" }, ct);
                return;
            }

            await WriteJsonAsync(ctx, 200, new { ok = true }, ct);
        }
        catch (Exception ex)
        {
            Logger.Log($"Test start failed: {ex}");
            await SafeWriteErrorAsync(ctx, 400, ct);
        }
        finally
        {
            // 只有真正开始运行才保留“API 发起”标记；否则复位，避免影响用户手动测速。
            if (isApiInitiated && !started)
            {
                try { TryRunOnUiThread(() => GetMainViewModel().ClearApiInitiatedTest(), TimeSpan.FromMilliseconds(500)); }
                catch (Exception ex) { Logger.Log($"Failed to reset API-initiated flag: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// FN-01：等待 ViewModel 真正进入测速状态。
    /// 命令内部存在前置准备阶段（适配器解析、URL 预探测对话框，最长 15s），
    /// 因此 200 必须等 IsTesting 翻转后才能返回，否则调用方拿到的是假成功。
    /// </summary>
    internal static async Task<bool> WaitForTestRunningAsync(Func<bool> isRunning, CancellationToken ct, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TestStartConfirmationTimeout);
        while (DateTime.UtcNow < deadline)
        {
            if (isRunning()) return true;
            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { return false; }
        }
        return isRunning();
    }

    private async Task HandleTestStopAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var vm = GetMainViewModel();
        var applied = TryRunOnUiThread(() =>
        {
            // 运行态与前置于准备阶段的启动请求都要能取消，否则停止请求会被静默忽略。
            vm.RequestTestStopForTest();
            if (vm.CancelTestCommand.CanExecute(null)) vm.CancelTestCommand.Execute(null);
        });

        if (!applied)
        {
            Logger.Log("Test stop aborted: UI thread did not complete the command within the timeout");
            await WriteJsonAsync(ctx, 503, new { error = "UI unavailable", message = "The UI thread did not respond in time" }, ct);
            return;
        }

        await WriteJsonAsync(ctx, 200, new { ok = true }, ct);
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request, CancellationToken ct)
    {
        if (request.ContentLength64 > MaxRequestBodyChars)
            throw new InvalidDataException("Request body too large");

        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var buffer = new char[4096];
        var sb = new StringBuilder();
        var total = 0;
        while (true)
        {
            // F-12：读取受请求级超时约束，慢速上传不能永久占住连接。
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0) break;
            total += read;
            if (total > MaxRequestBodyChars)
                throw new InvalidDataException("Request body too large");
            sb.Append(buffer, 0, read);
        }
        return sb.ToString();
    }

    private static async Task WriteJsonAsync(HttpListenerContext ctx, int statusCode, object payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        ctx.Response.OutputStream.Close();
    }

    private async Task ServeStaticAsync(HttpListenerContext ctx, string path, CancellationToken ct)
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
                await WriteJsonAsync(ctx, 404, new { error = "Not Found" }, ct);
                return;
            }
        }
        var ext = Path.GetExtension(file).ToLowerInvariant();

        // F-01：把会话令牌注入 HTML 占位符，静态资源本身不含任何秘密。
        if (ext == ".html")
            bytes = InjectSessionToken(bytes);

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
        await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        ctx.Response.OutputStream.Close();
    }

    /// <summary>
    /// 将 HTML 中的令牌与版本占位符替换为本次运行的实际值。
    /// </summary>
    internal static byte[] InjectSessionToken(byte[] html)
    {
        var text = Encoding.UTF8.GetString(html);
        if (text.IndexOf(TokenPlaceholder, StringComparison.Ordinal) >= 0)
            text = text.Replace(TokenPlaceholder, SessionToken, StringComparison.Ordinal);
        if (text.IndexOf(VersionPlaceholder, StringComparison.Ordinal) >= 0)
            text = text.Replace(VersionPlaceholder, Helpers.AppVersion.Short, StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(text);
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
  <meta name="nst-token" content="%%NST_TOKEN%%" />
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
