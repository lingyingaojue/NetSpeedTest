using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// B2 (F-05) 回归测试：SSRF 防护。测速目标必须是公网地址、端口在白名单内，
/// 且重定向必须逐跳校验，不能借 302 跳到内网。
/// </summary>
public class SsrfGuardTests
{
    [Theory]
    // 回环与私有段
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // 云元数据服务
    // 新增覆盖：0.0.0.0/8、CGNAT、IETF 协议分配、基准测试
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.0.170")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.254")]
    // 组播与保留
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    // IPv6
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    // IPv4-mapped IPv6 必须回到 IPv4 判定
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:192.168.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    public void IsPrivateIp_rejects_internal_and_reserved_addresses(string address)
    {
        Assert.True(WebServerService.IsPrivateIp(IPAddress.Parse(address)), $"{address} must be treated as non-public");
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]   // 刚好在 172.16/12 之外
    [InlineData("172.15.255.255")] // 刚好在 172.16/12 之前
    [InlineData("100.63.255.255")] // 刚好在 CGNAT 之前
    [InlineData("100.128.0.1")]    // 刚好在 CGNAT 之后
    [InlineData("198.17.255.255")] // 刚好在 198.18/15 之前
    [InlineData("198.20.0.1")]     // 刚好在 198.18/15 之后
    [InlineData("192.0.1.1")]      // 刚好在 192.0.0.0/24 之外
    [InlineData("223.255.255.255")] // 组播段之前
    [InlineData("2001:4860:4860::8888")]
    public void IsPrivateIp_accepts_public_addresses(string address)
    {
        Assert.False(WebServerService.IsPrivateIp(IPAddress.Parse(address)), $"{address} is a public address");
    }

    [Fact]
    public void IsPrivateIp_treats_null_as_private()
    {
        Assert.True(WebServerService.IsPrivateIp(null!));
    }

    [Theory]
    [InlineData("http://127.0.0.1/x")]
    [InlineData("http://localhost:80/x")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://foo.local/")]
    [InlineData("http://foo.internal/")]
    [InlineData("ftp://8.8.8.8/")]
    [InlineData("file:///C:/windows/win.ini")]
    public async Task ResolvePublicHttpTargetAsync_rejects_non_public_targets(string url)
    {
        var (allowed, _) = await WebServerService.ResolvePublicHttpTargetAsync(url, CancellationToken.None);
        Assert.False(allowed, $"{url} must be rejected");
    }

    [Theory]
    [InlineData("http://8.8.8.8/", 80)]
    [InlineData("https://1.1.1.1/", 443)]
    public async Task ResolvePublicHttpTargetAsync_accepts_public_host_on_allowlisted_port(string url, int expectedPort)
    {
        var (allowed, addresses) = await WebServerService.ResolvePublicHttpTargetAsync(url, CancellationToken.None);

        Assert.True(allowed, $"{url} must be allowed");
        Assert.NotEmpty(addresses);
        Assert.Equal(expectedPort, new Uri(url).Port);
    }

    [Theory]
    [InlineData("http://8.8.8.8:8080/")]
    [InlineData("http://8.8.8.8:9/")]
    [InlineData("http://8.8.8.8:22/")]
    [InlineData("http://8.8.8.8:3389/")]
    public async Task ResolvePublicHttpTargetAsync_rejects_ports_outside_allowlist(string url)
    {
        var (allowed, _) = await WebServerService.ResolvePublicHttpTargetAsync(url, CancellationToken.None);
        Assert.False(allowed, $"{url} uses a port outside the allowlist");
    }

    [Fact]
    public void TryResolveRedirect_resolves_relative_location_against_current_uri()
    {
        var current = new Uri("https://example.com/a/b/c");

        Assert.True(SpeedTestService.TryResolveRedirect(current, "/next", out var absolute));
        Assert.Equal("https://example.com/next", absolute.ToString());

        Assert.True(SpeedTestService.TryResolveRedirect(current, "sibling", out var relative));
        Assert.Equal("https://example.com/a/b/sibling", relative.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void TryResolveRedirect_rejects_missing_or_non_http_locations(string? location)
    {
        var current = new Uri("https://example.com/");
        Assert.False(SpeedTestService.TryResolveRedirect(current, location, out _));
    }

    [Fact]
    public async Task SendWithValidatedRedirectsAsync_returns_first_response_when_not_a_redirect()
    {
        using var plain = new RedirectServer(new[] { (200, (string?)null) });

        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
        using var resp = await SpeedTestService.SendWithValidatedRedirectsAsync(
            client, plain.Url, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(1, plain.RequestCount);
    }

    [Fact]
    public async Task SendWithValidatedRedirectsAsync_follows_validated_redirect()
    {
        using var target = new RedirectServer(new[] { (200, (string?)null) });
        using var origin = new RedirectServer(new (int, string?)[] { (302, target.Url) });

        var validatorCalls = new List<string>();
        var previous = SpeedTestService.TargetValidator;
        SpeedTestService.TargetValidator = (url, _) =>
        {
            lock (validatorCalls) validatorCalls.Add(url);
            return Task.FromResult(true);
        };

        try
        {
            using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
            using var resp = await SpeedTestService.SendWithValidatedRedirectsAsync(
                client, origin.Url, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            lock (validatorCalls)
            {
                Assert.Single(validatorCalls);
                Assert.Equal(target.Url, validatorCalls[0]);
            }
        }
        finally
        {
            SpeedTestService.TargetValidator = previous;
        }
    }

    [Fact]
    public async Task SendWithValidatedRedirectsAsync_rejects_redirect_to_private_target()
    {
        using var target = new RedirectServer(new[] { (200, (string?)null) });
        using var origin = new RedirectServer(new (int, string?)[] { (302, target.Url) });

        var previous = SpeedTestService.TargetValidator;
        // 模拟解析到内网地址：校验器拒绝。
        SpeedTestService.TargetValidator = (_, _) => Task.FromResult(false);

        try
        {
            using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                SpeedTestService.SendWithValidatedRedirectsAsync(
                    client, origin.Url, TimeSpan.FromSeconds(5), CancellationToken.None));

            Assert.Contains("rejected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SpeedTestService.TargetValidator = previous;
        }
    }

    [Fact]
    public async Task SendWithValidatedRedirectsAsync_gives_up_after_hop_limit()
    {
        // 服务器对每个请求都回 302 指向自己：必须在上限内停止而不是无限循环。
        using var loop = new RedirectServer(Array.Empty<(int, string?)>(), alwaysRedirectToSelf: true);

        var previous = SpeedTestService.TargetValidator;
        SpeedTestService.TargetValidator = (_, _) => Task.FromResult(true);

        try
        {
            using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                SpeedTestService.SendWithValidatedRedirectsAsync(
                    client, loop.Url, TimeSpan.FromSeconds(5), CancellationToken.None));

            Assert.Contains("Too many redirects", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(loop.RequestCount <= SpeedTestService.MaxSpeedTestRedirects + 1,
                $"expected at most {SpeedTestService.MaxSpeedTestRedirects + 1} requests, saw {loop.RequestCount}");
        }
        finally
        {
            SpeedTestService.TargetValidator = previous;
        }
    }

    [Fact]
    public async Task SendWithValidatedRedirectsAsync_rejects_redirect_without_location()
    {
        using var origin = new RedirectServer(new[] { (302, (string?)null) });

        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            SpeedTestService.SendWithValidatedRedirectsAsync(
                client, origin.Url, TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently, true)]
    [InlineData(HttpStatusCode.Found, true)]
    [InlineData(HttpStatusCode.SeeOther, true)]
    [InlineData(HttpStatusCode.TemporaryRedirect, true)]
    [InlineData(HttpStatusCode.PermanentRedirect, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NoContent, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public void IsRedirectStatusCode_classifies_redirects(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, SpeedTestService.IsRedirectStatusCode(status));
    }

    /// <summary>
    /// 极简回环 HTTP 服务：按脚本依次返回状态码与 Location，用完后返回 200。
    /// </summary>
    private sealed class RedirectServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly (int Status, string? Location)[] _script;
        private readonly bool _alwaysRedirectToSelf;
        private int _requestCount;

        public string Url { get; }
        public int RequestCount => Volatile.Read(ref _requestCount);

        public RedirectServer((int Status, string? Location)[] script, bool alwaysRedirectToSelf = false)
        {
            _script = script;
            _alwaysRedirectToSelf = alwaysRedirectToSelf;

            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }

                try
                {
                    var index = Interlocked.Increment(ref _requestCount) - 1;
                    int status;
                    string? location;

                    if (_alwaysRedirectToSelf)
                    {
                        status = 302;
                        location = Url;
                    }
                    else if (index < _script.Length)
                    {
                        (status, location) = _script[index];
                    }
                    else
                    {
                        (status, location) = (200, null);
                    }

                    ctx.Response.StatusCode = status;
                    if (!string.IsNullOrEmpty(location)) ctx.Response.Headers["Location"] = location;
                    ctx.Response.ContentLength64 = 0;
                    ctx.Response.OutputStream.Close();
                }
                catch
                {
                    try { ctx.Response.Abort(); } catch { }
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            _cts.Dispose();
        }
    }
}
