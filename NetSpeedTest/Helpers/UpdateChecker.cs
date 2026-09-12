using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NetSpeedTest.Services;

[assembly: InternalsVisibleTo("NetSpeedTest.Tests")]

namespace NetSpeedTest.Helpers;

public enum CheckStatus { NoUpdate, HasUpdate, Failed, NotConfigured }

public record UpdateInfo(string Version, string Body, string DownloadUrl);

/// <summary>
/// GitHub Release 候选版本。Tag 为原始 tag_name，Version 为去除 v 前缀与后缀后的数字版本。
/// </summary>
internal sealed record ReleaseCandidate(
    string Tag,
    string Body,
    string? DownloadUrl,
    bool Prerelease,
    DateTimeOffset? PublishedAt,
    Version Version,
    string? PrereleaseLabel);

/// <summary>
/// GitHub Releases 更新检查。
/// 支持 /releases/latest 在仅有预发布时返回 404 的回退；并发调用共享同一次检查。
/// </summary>
public static class UpdateChecker
{
    private const string LatestSuffix = "/releases/latest";
    private const string ReleaseListSuffix = "/releases?per_page=20";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly object _gate = new();

    private static Task<(CheckStatus status, UpdateInfo? info)>? _inFlight;

    public static bool IsChecking
    {
        get { lock (_gate) return _inFlight != null; }
    }

    public static Task<(CheckStatus status, UpdateInfo? info)> CheckAsync(
        IConfiguration config,
        HttpClient? client = null,
        string? currentVersion = null)
    {
        lock (_gate)
        {
            if (_inFlight != null) return _inFlight;

            var completion = new TaskCompletionSource<(CheckStatus status, UpdateInfo? info)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight = completion.Task;

            // 在锁内启动；RunAsync 完成时会回到同一把锁清除 in-flight 状态。
            _ = RunAsync(completion, config, client, currentVersion);
            return completion.Task;
        }
    }

    private static async Task RunAsync(
        TaskCompletionSource<(CheckStatus status, UpdateInfo? info)> completion,
        IConfiguration config,
        HttpClient? client,
        string? currentVersion)
    {
        (CheckStatus status, UpdateInfo? info) result;
        try
        {
            result = await CheckCoreAsync(config, client, currentVersion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check failed: {ex.Message}");
            result = (CheckStatus.Failed, null);
        }

        lock (_gate)
        {
            _inFlight = null;
            completion.TrySetResult(result);
        }
    }

    private static async Task<(CheckStatus status, UpdateInfo? info)> CheckCoreAsync(
        IConfiguration config,
        HttpClient? client,
        string? currentVersion)
    {
        var apiUrl = config["UpdateApiUrl"];
        if (string.IsNullOrWhiteSpace(apiUrl))
            return (CheckStatus.NotConfigured, null);

        currentVersion ??= AppVersion.Short;
        if (!TryParseReleaseVersion(currentVersion, out var current, out _))
            return (CheckStatus.Failed, null);

        var usesGlobalClient = client == null;
        if (usesGlobalClient)
        {
            // 仅在使用全局 HttpClient 时才访问 WPF 容器；测试注入 client 时完全不触碰 Application.Current。
            if (System.Windows.Application.Current is not App app)
                throw new InvalidOperationException("无法获取应用 HttpClient，请注入 HttpClient 后再检查更新。");

            client = app.GetService<HttpClient>();
        }

        var result = await TryCheckViaHttpAsync(
            client!, apiUrl, currentVersion, logHttpErrors: !usesGlobalClient).ConfigureAwait(false);

        if (!usesGlobalClient || result.status != CheckStatus.Failed)
            return result;

        // P2：应用直连（UseProxy=false）失败时，用系统代理独立重试一次。
        // 重试使用单独的 HttpClient，不改变测速用全局客户端的 UseProxy=false 设置；
        // 注入 HttpClient 的调用方（测试）不触发该回退。
        Logger.Log("Update check: direct request failed, retrying with system proxy.");
        using var proxyClient = new HttpClient { Timeout = RequestTimeout };
        var retried = await TryCheckViaHttpAsync(
            proxyClient, apiUrl, currentVersion, logHttpErrors: true).ConfigureAwait(false);

        if (retried.status != CheckStatus.Failed)
            Logger.Log("Update check: system proxy retry succeeded.");

        return retried;
    }

    private static async Task<(CheckStatus status, UpdateInfo? info)> TryCheckViaHttpAsync(
        HttpClient client,
        string apiUrl,
        string currentVersion,
        bool logHttpErrors)
    {
        using var cts = new CancellationTokenSource(RequestTimeout);
        string? json;
        try
        {
            json = await GetReleaseJsonAsync(client, apiUrl, cts.Token, logHttpErrors).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkFailure(ex))
        {
            if (logHttpErrors) Logger.Log($"Update check failed: {ex.Message}");
            return (CheckStatus.Failed, null);
        }

        if (json == null) return (CheckStatus.Failed, null);

        return ParseReleaseJson(json, currentVersion);
    }

    private static bool IsNetworkFailure(Exception ex)
        => ex is HttpRequestException or OperationCanceledException or System.Net.Sockets.SocketException;

    private static async Task<string?> GetReleaseJsonAsync(
        HttpClient client,
        string apiUrl,
        CancellationToken token,
        bool logHttpErrors)
    {
        using var response = await SendAsync(client, apiUrl, token).ConfigureAwait(false);

        // 仅 404 + /releases/latest：仓库全部为预发布时 GitHub 的必然结果，回退到 Release 列表。
        // 403 限流、超时与其他非 2xx 不回退。
        if (response.StatusCode == HttpStatusCode.NotFound && IsLatestReleaseUrl(apiUrl))
        {
            var fallbackUrl = DeriveReleaseListUrl(apiUrl);
            using var fallback = await SendAsync(client, fallbackUrl, token).ConfigureAwait(false);
            return await ReadJsonIfSuccessAsync(fallback, fallbackUrl, token, logHttpErrors).ConfigureAwait(false);
        }

        return await ReadJsonIfSuccessAsync(response, apiUrl, token, logHttpErrors).ConfigureAwait(false);
    }

    private static async Task<string?> ReadJsonIfSuccessAsync(
        HttpResponseMessage response,
        string url,
        CancellationToken token,
        bool logHttpErrors)
    {
        if ((int)response.StatusCode is < 200 or >= 300)
        {
            if (logHttpErrors)
                Logger.Log($"Update check failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase} ({url})");
            return null;
        }

        return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string url,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd(AppVersion.UserAgent);

        return await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
    }

    private static (CheckStatus status, UpdateInfo? info) ParseReleaseJson(string json, string currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var candidates = new List<ReleaseCandidate>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                var candidate = ToCandidate(element);
                if (candidate != null) candidates.Add(candidate);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            var candidate = ToCandidate(root);
            if (candidate != null) candidates.Add(candidate);
        }
        else
        {
            return (CheckStatus.Failed, null);
        }

        var latest = SelectLatestCandidate(candidates, currentVersion);
        if (latest == null) return (CheckStatus.Failed, null);

        if (!TryParseReleaseVersion(currentVersion, out var current, out _))
            return (CheckStatus.Failed, null);

        if (latest.Version <= current) return (CheckStatus.NoUpdate, null);

        if (string.IsNullOrWhiteSpace(latest.DownloadUrl))
        {
            Logger.Log($"Update check failed: release {latest.Tag} has no downloadable asset.");
            return (CheckStatus.Failed, null);
        }

        return (CheckStatus.HasUpdate, new UpdateInfo(FormatVersion(latest), latest.Body, latest.DownloadUrl));
    }

    private static ReleaseCandidate? ToCandidate(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object) return null;

        // 排除草稿：草稿不应出现在更新检查结果中。
        if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            return null;

        var tag = release.TryGetProperty("tag_name", out var tagProperty) && tagProperty.ValueKind == JsonValueKind.String
            ? tagProperty.GetString()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(tag)) return null;
        if (!TryParseReleaseVersion(tag, out var version, out var prereleaseLabel)) return null;

        var body = release.TryGetProperty("body", out var bodyProperty) && bodyProperty.ValueKind == JsonValueKind.String
            ? bodyProperty.GetString() ?? ""
            : "";

        // tag 里的 -beta 后缀与 GitHub 的 prerelease 字段任一为真即视为预发布。
        var prerelease = prereleaseLabel != null
            || (release.TryGetProperty("prerelease", out var prereleaseProperty)
                && prereleaseProperty.ValueKind == JsonValueKind.True);

        DateTimeOffset? publishedAt = null;
        if (release.TryGetProperty("published_at", out var publishedProperty)
            && publishedProperty.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                publishedProperty.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsedPublishedAt))
        {
            publishedAt = parsedPublishedAt;
        }

        return new ReleaseCandidate(
            tag!,
            body,
            SelectDownloadUrl(release),
            prerelease,
            publishedAt,
            version,
            prereleaseLabel);
    }

    /// <summary>
    /// 去除 v/V 前缀，并兼容 -beta / +build 后缀的数字版本解析。
    /// </summary>
    internal static bool TryParseReleaseVersion(string? tag, out Version version, out string? prerelease)
    {
        version = new Version(0, 0, 0);
        prerelease = null;
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var text = tag.Trim();
        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V')) text = text[1..];
        if (text.Length == 0) return false;

        var buildIndex = text.IndexOf('+');
        if (buildIndex >= 0) text = text[..buildIndex];
        if (text.Length == 0) return false;

        var prereleaseIndex = text.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            var label = text[(prereleaseIndex + 1)..].Trim();
            if (label.Length > 0) prerelease = label;
            text = text[..prereleaseIndex];
        }

        text = text.Trim();
        if (text.Length == 0 || !Version.TryParse(text, out var parsed)) return false;

        version = parsed;
        return true;
    }

    /// <summary>
    /// 选择最新候选：先比数字版本；同版本正式版优先；仍相同按发布时间降序。
    /// 不能全局「正式版优先」，否则低版本正式版会压过高版本预发布。
    /// </summary>
    internal static ReleaseCandidate? SelectLatestCandidate(
        IEnumerable<ReleaseCandidate> candidates,
        string currentVersion)
    {
        if (candidates == null) return null;

        var pool = candidates.ToList();
        if (pool.Count == 0) return null;

        if (TryParseReleaseVersion(currentVersion, out var current, out _))
        {
            var newer = pool.Where(c => c.Version > current).ToList();
            if (newer.Count > 0) pool = newer;
        }

        return pool
            .OrderByDescending(c => c.Version)
            .ThenBy(c => c.Prerelease)
            .ThenByDescending(c => c.PublishedAt)
            .First();
    }

    /// <summary>
    /// 资产优先级：NetSpeedTest*.exe &gt; 任意 .exe &gt; release html_url；
    /// 跳过 source.zip / .sha256 / .txt。
    /// </summary>
    internal static string? SelectDownloadUrl(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object) return null;

        string? anyExeUrl = null;
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;

                var name = asset.TryGetProperty("name", out var nameProperty)
                    && nameProperty.ValueKind == JsonValueKind.String
                    ? nameProperty.GetString()?.Trim() ?? ""
                    : "";
                if (IsSkippedAsset(name)) continue;

                var url = asset.TryGetProperty("browser_download_url", out var urlProperty)
                    && urlProperty.ValueKind == JsonValueKind.String
                    ? urlProperty.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(url)) continue;

                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                if (name.StartsWith("NetSpeedTest", StringComparison.OrdinalIgnoreCase))
                    return url;

                anyExeUrl ??= url;
            }
        }

        if (anyExeUrl != null) return anyExeUrl;

        return release.TryGetProperty("html_url", out var htmlProperty)
            && htmlProperty.ValueKind == JsonValueKind.String
            ? htmlProperty.GetString()
            : null;
    }

    private static bool IsSkippedAsset(string name)
    {
        if (name.Length == 0) return false;

        return name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("source.zip", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatVersion(ReleaseCandidate candidate)
        => string.IsNullOrEmpty(candidate.PrereleaseLabel)
            ? candidate.Version.ToString()
            : $"{candidate.Version}-{candidate.PrereleaseLabel}";

    private static bool IsLatestReleaseUrl(string url)
        => StripQueryAndFragment(url).TrimEnd('/').EndsWith(LatestSuffix, StringComparison.OrdinalIgnoreCase);

    private static string DeriveReleaseListUrl(string url)
    {
        var trimmed = StripQueryAndFragment(url).TrimEnd('/');
        var baseUrl = trimmed[..^LatestSuffix.Length];
        return baseUrl + ReleaseListSuffix;
    }

    private static string StripQueryAndFragment(string url)
    {
        var index = url.IndexOfAny(new[] { '?', '#' });
        return index < 0 ? url : url[..index];
    }
}