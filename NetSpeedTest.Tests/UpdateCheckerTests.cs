using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetSpeedTest.Helpers;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// 检查更新回归测试：全部通过假 HttpMessageHandler 驱动，不访问真实网络。
/// </summary>
public class UpdateCheckerTests
{
    private const string LatestUrl = "https://api.github.com/repos/lingyingaojue/NetSpeedTest/releases/latest";
    private const string ListUrl = "https://api.github.com/repos/lingyingaojue/NetSpeedTest/releases?per_page=20";

    private static IConfiguration Config(string? updateApiUrl = LatestUrl)
    {
        var values = new Dictionary<string, string?>();
        if (updateApiUrl != null) values["UpdateApiUrl"] = updateApiUrl;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage NotFound()
        => Json(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");

    private static string ListJson(params string[] releases) => "[" + string.Join(",", releases) + "]";

    private static string ReleaseJson(
        string tag,
        bool prerelease,
        string body = "",
        string? assetName = "NetSpeedTest.exe",
        string? assetUrl = null,
        bool draft = false,
        string publishedAt = "2026-09-01T00:00:00Z")
    {
        var assets = new List<object>();
        if (assetName != null)
        {
            assets.Add(new
            {
                name = assetName,
                browser_download_url = assetUrl
                    ?? $"https://github.com/lingyingaojue/NetSpeedTest/releases/download/{tag}/{assetName}"
            });
        }

        return JsonSerializer.Serialize(new
        {
            tag_name = tag,
            body,
            draft,
            prerelease,
            published_at = publishedAt,
            html_url = $"https://github.com/lingyingaojue/NetSpeedTest/releases/tag/{tag}",
            assets
        });
    }

    private static string ReleaseWithAssets(
        string tag,
        bool prerelease,
        params (string Name, string Url)[] assets)
        => JsonSerializer.Serialize(new
        {
            tag_name = tag,
            body = "",
            draft = false,
            prerelease,
            published_at = "2026-09-01T00:00:00Z",
            html_url = $"https://github.com/lingyingaojue/NetSpeedTest/releases/tag/{tag}",
            assets = assets.Select(a => new { name = a.Name, browser_download_url = a.Url }).ToArray()
        });

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("condition was not met in time");
            await Task.Delay(10);
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        private readonly object _sync = new();
        private readonly List<string> _requests = new();

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            => _respond = respond;

        public IReadOnlyList<string> Requests
        {
            get { lock (_sync) return _requests.ToArray(); }
        }

        public int RequestCount
        {
            get { lock (_sync) return _requests.Count; }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_sync) _requests.Add(request.RequestUri?.AbsoluteUri ?? "");
            return _respond(request, cancellationToken);
        }
    }

    private static FakeHandler Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        => new(respond);

    private static HttpClient NewClient(FakeHandler handler) => new(handler);

    // ------------------------------------------------------------------
    // 回归用例
    // ------------------------------------------------------------------

    [Fact]
    public async Task Latest_404_falls_back_to_release_list_and_selects_prerelease()
    {
        using var handler = Handler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal)
                ? NotFound()
                : Json(HttpStatusCode.OK, ListJson(ReleaseJson("v1.4.2", prerelease: true, body: "prerelease")))));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(), client, "1.4.1");

        Assert.Equal(CheckStatus.HasUpdate, status);
        Assert.NotNull(info);
        Assert.Equal("1.4.2", info!.Version);
        Assert.Equal("prerelease", info.Body);
        Assert.Equal(
            "https://github.com/lingyingaojue/NetSpeedTest/releases/download/v1.4.2/NetSpeedTest.exe",
            info.DownloadUrl);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(ListUrl, handler.Requests[1]);
    }

    [Fact]
    public async Task Current_version_equal_to_latest_list_entry_returns_no_update()
    {
        using var handler = Handler((_, _) => Task.FromResult(
            Json(HttpStatusCode.OK, ListJson(ReleaseJson("v1.4.2", prerelease: true)))));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(ListUrl), client, "1.4.2");

        Assert.Equal(CheckStatus.NoUpdate, status);
        Assert.Null(info);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Single_release_object_response_is_supported()
    {
        using var handler = Handler((_, _) => Task.FromResult(
            Json(HttpStatusCode.OK, ReleaseJson("v1.4.3", prerelease: true))));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(), client, "1.4.2");

        Assert.Equal(CheckStatus.HasUpdate, status);
        Assert.NotNull(info);
        Assert.Equal("1.4.3", info!.Version);
    }

    [Fact]
    public async Task Higher_prerelease_beats_lower_stable()
    {
        var json = ListJson(
            ReleaseJson("v1.4.2", prerelease: false, body: "stable-1.4.2"),
            ReleaseJson("v1.4.3-beta", prerelease: true, body: "beta-1.4.3"));
        using var handler = Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, json)));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(ListUrl), client, "1.4.1");

        Assert.Equal(CheckStatus.HasUpdate, status);
        Assert.NotNull(info);
        Assert.Equal("1.4.3-beta", info!.Version);
        Assert.Equal("beta-1.4.3", info.Body);
    }

    [Fact]
    public async Task Stable_beats_prerelease_when_numeric_versions_are_equal()
    {
        var json = ListJson(
            ReleaseJson("v1.4.3-beta", prerelease: true, body: "beta"),
            ReleaseJson("v1.4.3", prerelease: false, body: "stable"));
        using var handler = Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, json)));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(ListUrl), client, "1.4.2");

        Assert.Equal(CheckStatus.HasUpdate, status);
        Assert.NotNull(info);
        Assert.Equal("1.4.3", info!.Version);
        Assert.Equal("stable", info.Body);
        Assert.Equal(
            "https://github.com/lingyingaojue/NetSpeedTest/releases/download/v1.4.3/NetSpeedTest.exe",
            info.DownloadUrl);
    }

    [Fact]
    public async Task Version_1_4_10_is_newer_than_1_4_9()
    {
        var json = ListJson(
            ReleaseJson("v1.4.9", prerelease: false),
            ReleaseJson("v1.4.10", prerelease: false));
        using var handler = Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, json)));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(ListUrl), client, "1.4.9");

        Assert.Equal(CheckStatus.HasUpdate, status);
        Assert.NotNull(info);
        Assert.Equal("1.4.10", info!.Version);
    }

    [Fact]
    public async Task Draft_release_is_excluded_from_selection()
    {
        var json = ListJson(
            ReleaseJson("v1.4.3", prerelease: false, body: "draft", draft: true),
            ReleaseJson("v1.4.2", prerelease: true, body: "current"));
        using var handler = Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, json)));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(ListUrl), client, "1.4.2");

        Assert.Equal(CheckStatus.NoUpdate, status);
        Assert.Null(info);
    }

    [Fact]
    public async Task Asset_selection_prefers_netspeedtest_exe_over_source_zip()
    {
        var exeUrl =
            "https://github.com/lingyingaojue/NetSpeedTest/releases/download/v1.4.3/NetSpeedTest.exe";
        var json = ListJson(ReleaseWithAssets(
            "v1.4.3",
            prerelease: true,
            ("source.zip", "https://github.com/lingyingaojue/NetSpeedTest/releases/download/v1.4.3/source.zip"),
            ("NetSpeedTest.exe", exeUrl)));
        using var handler = Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, json)));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(ListUrl), client, "1.4.2");

        Assert.Equal(CheckStatus.HasUpdate, status);
        Assert.NotNull(info);
        Assert.Equal(exeUrl, info!.DownloadUrl);
    }

    [Fact]
    public async Task Forbidden_rate_limit_fails_without_release_list_fallback()
    {
        using var handler = Handler((_, _) => Task.FromResult(
            Json(HttpStatusCode.Forbidden, "{\"message\":\"API rate limit exceeded\"}")));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(), client, "1.4.1");

        Assert.Equal(CheckStatus.Failed, status);
        Assert.Null(info);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(LatestUrl, handler.Requests[0]);
    }

    [Fact]
    public async Task Empty_update_source_returns_not_configured_without_http()
    {
        using var handler = Handler((_, _) => throw new InvalidOperationException("HTTP must not be called"));
        using var client = NewClient(handler);
        var config = new ConfigurationBuilder().Build();

        var (status, info) = await UpdateChecker.CheckAsync(config, client);

        Assert.Equal(CheckStatus.NotConfigured, status);
        Assert.Null(info);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Injected_http_client_is_used_without_touching_wpf_application()
    {
        // 测试进程没有 WPF Application；若实现错误地访问 Application.Current，本用例会失败。
        Assert.Null(System.Windows.Application.Current);

        using var handler = Handler((_, _) => Task.FromResult(
            Json(HttpStatusCode.OK, ListJson(ReleaseJson("v1.4.2", prerelease: true)))));
        using var client = NewClient(handler);

        var (status, info) = await UpdateChecker.CheckAsync(Config(ListUrl), client, "1.4.1");

        Assert.Equal(CheckStatus.HasUpdate, status);
        Assert.NotNull(info);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Concurrent_calls_share_single_http_request_and_result()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = Handler((_, token) => pending.Task.WaitAsync(token));
        using var client = NewClient(handler);

        var first = UpdateChecker.CheckAsync(Config(ListUrl), client, "1.4.1");
        var second = UpdateChecker.CheckAsync(Config(ListUrl), client, "1.4.1");

        Assert.Same(first, second);
        Assert.True(UpdateChecker.IsChecking);

        await WaitUntilAsync(() => handler.RequestCount == 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1, handler.RequestCount);

        pending.SetResult(Json(HttpStatusCode.OK, ListJson(ReleaseJson("v1.4.2", prerelease: true))));
        var firstResult = await first;
        var secondResult = await second;

        Assert.Equal(firstResult, secondResult);
        Assert.Equal(CheckStatus.HasUpdate, firstResult.status);
        Assert.NotNull(firstResult.info);
        Assert.False(UpdateChecker.IsChecking);
        Assert.Equal(1, handler.RequestCount);
    }

    // ------------------------------------------------------------------
    // 纯函数
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("v1.4.2", "1.4.2", null)]
    [InlineData("1.4.10", "1.4.10", null)]
    [InlineData("V1.4.3-beta", "1.4.3", "beta")]
    [InlineData("v1.4.3-beta.1+build.5", "1.4.3", "beta.1")]
    public void TryParseReleaseVersion_handles_prefix_and_suffix(
        string tag,
        string expectedVersion,
        string? expectedPrerelease)
    {
        Assert.True(UpdateChecker.TryParseReleaseVersion(tag, out var version, out var prerelease));
        Assert.Equal(expectedVersion, version.ToString());
        Assert.Equal(expectedPrerelease, prerelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("not-a-version")]
    public void TryParseReleaseVersion_rejects_invalid_tags(string tag)
    {
        Assert.False(UpdateChecker.TryParseReleaseVersion(tag, out _, out _));
    }
}