using System.Net;
using System.Text;
using System.Threading;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// B2 (F-01 / FN-06) 回归测试：会话令牌、写操作判定与 HTML 令牌注入。
/// 目的：局域网/浏览器跨源请求不能在没有令牌的情况下触发测速或删除数据。
/// </summary>
public class WebSecurityTests
{
    [Fact]
    public void SessionToken_is_generated_and_not_guessable()
    {
        var token = WebServerService.SessionToken;

        Assert.False(string.IsNullOrWhiteSpace(token));
        // 32 字节随机数的十六进制表示 = 64 个字符。
        Assert.Equal(64, token.Length);
        Assert.All(token, c => Assert.True(Uri.IsHexDigit(c), $"unexpected character '{c}' in token"));
    }

    [Fact]
    public void IsTokenValid_accepts_the_session_token()
    {
        Assert.True(WebServerService.IsTokenValid(WebServerService.SessionToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("not-the-token")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public void IsTokenValid_rejects_anything_else(string? provided)
    {
        Assert.False(WebServerService.IsTokenValid(provided));
    }

    [Fact]
    public void IsTokenValid_is_case_sensitive()
    {
        var lowered = WebServerService.SessionToken.ToLowerInvariant();
        Assert.False(WebServerService.IsTokenValid(lowered));
    }

    [Fact]
    public void IsTokenValid_rejects_prefix_and_extension_of_real_token()
    {
        var token = WebServerService.SessionToken;
        Assert.False(WebServerService.IsTokenValid(token[..^1]));
        Assert.False(WebServerService.IsTokenValid(token + "0"));
    }

    [Theory]
    [InlineData("POST", true)]
    [InlineData("post", true)]
    [InlineData("DELETE", true)]
    [InlineData("delete", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    [InlineData(null, false)]
    public void IsStateChangingMethod_flags_writes_only(string? method, bool expected)
    {
        Assert.Equal(expected, WebServerService.IsStateChangingMethod(method));
    }

    [Fact]
    public void IsLoopbackRequest_recognises_ipv4_ipv6_and_mapped_loopback()
    {
        Assert.True(WebServerService.IsLoopbackRequest(new IPEndPoint(IPAddress.Loopback, 1234)));
        Assert.True(WebServerService.IsLoopbackRequest(new IPEndPoint(IPAddress.IPv6Loopback, 1234)));
        Assert.True(WebServerService.IsLoopbackRequest(new IPEndPoint(IPAddress.Parse("::ffff:127.0.0.1"), 1234)));
        Assert.True(WebServerService.IsLoopbackRequest(new IPEndPoint(IPAddress.Parse("127.5.5.5"), 1234)));
    }

    [Fact]
    public void IsLoopbackRequest_rejects_lan_and_null_endpoints()
    {
        Assert.False(WebServerService.IsLoopbackRequest(new IPEndPoint(IPAddress.Parse("192.168.1.50"), 1234)));
        Assert.False(WebServerService.IsLoopbackRequest(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 1234)));
        Assert.False(WebServerService.IsLoopbackRequest(null));
    }

    [Fact]
    public void InjectSessionToken_replaces_placeholder_with_session_token()
    {
        var html = Encoding.UTF8.GetBytes(
            "<html><head><meta name=\"nst-token\" content=\"" + WebServerService.TokenPlaceholder + "\" /></head></html>");

        var result = Encoding.UTF8.GetString(WebServerService.InjectSessionToken(html));

        Assert.DoesNotContain(WebServerService.TokenPlaceholder, result, StringComparison.Ordinal);
        Assert.Contains(WebServerService.SessionToken, result, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectSessionToken_is_a_noop_when_placeholder_absent()
    {
        var html = Encoding.UTF8.GetBytes("<html><body>no placeholder here</body></html>");

        var result = WebServerService.InjectSessionToken(html);

        Assert.Equal(html, result);
    }

    [Fact]
    public void ErrorPayload_never_leaks_internal_details()
    {
        // 错误响应文案必须是固定的分类信息，不能包含异常/路径/netsh 输出。
        foreach (var status in new[] { 400, 401, 403, 500 })
        {
            var payload = WebServerService.ErrorPayload(status);
            var json = System.Text.Json.JsonSerializer.Serialize(payload);

            foreach (var marker in new[] { "netsh", "\\", "C:", "Exception", "at NetSpeedTest" })
                Assert.DoesNotContain(marker, json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
