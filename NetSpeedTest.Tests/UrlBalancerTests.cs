using System.Collections.Generic;
using System.Linq;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// QA 补充：URL 负载均衡器（探索覆盖 → 最快节点 → 失败冷却避让）契约测试。
/// 对应风险 RM-SPEED-003：URL 明细与节点选择错误会直接导致测速结论失真。
/// </summary>
public class UrlBalancerTests
{
    private static SpeedTestService.UrlBalancer CreateDlBalancer(params string[] urls)
        => new SpeedTestService.UrlBalancer(urls, useFastestAfterProbe: true);

    [Fact]
    public void ExplorePhase_EveryUrl_IsAssignedBeforeFastestSelection()
    {
        var urls = new[] { "http://a.example/100mb.bin", "http://b.example/100mb.bin", "http://c.example/100mb.bin" };
        var balancer = CreateDlBalancer(urls);

        // 单 worker 连续取 3 次，探索阶段必须覆盖全部 3 个 URL，不能只盯住第一个。
        var assigned = new List<string>();
        for (var i = 0; i < urls.Length; i++)
            assigned.Add(balancer.GetUrlForWorker(workerId: 0));

        Assert.Equal(urls.OrderBy(u => u), assigned.Distinct().OrderBy(u => u));
        Assert.Equal(urls.Length, assigned.Distinct().Count());
    }

    [Fact]
    public void ExplorePhase_DistributesAcrossUrls_EvenWhenWorkersFewerThanUrls()
    {
        var urls = new[] { "http://a/x", "http://b/x", "http://c/x", "http://d/x" };
        var balancer = CreateDlBalancer(urls);

        // 2 个 worker 各取 2 次，合计 4 次仍应覆盖 4 个 URL（游标轮转，避免某些 URL 永远分不到线程）。
        var got = new List<string>
        {
            balancer.GetUrlForWorker(0),
            balancer.GetUrlForWorker(1),
            balancer.GetUrlForWorker(0),
            balancer.GetUrlForWorker(1)
        };

        Assert.Equal(urls.Length, got.Distinct().Count());
    }

    [Fact]
    public void AfterAllProbed_FastestUrl_IsSelected()
    {
        var urls = new[] { "http://slow/x", "http://fast/x", "http://mid/x" };
        var balancer = CreateDlBalancer(urls);

        balancer.ReportSuccess("http://slow/x", avgMbps: 10, duration: 1);
        balancer.ReportSuccess("http://fast/x", avgMbps: 100, duration: 1);
        balancer.ReportSuccess("http://mid/x", avgMbps: 50, duration: 1);

        // 全部探测完成后，连续多次都应选到最快节点。
        for (var i = 0; i < 5; i++)
            Assert.Equal("http://fast/x", balancer.GetUrlForWorker(i));
    }

    [Fact]
    public void ConsecutiveFailures_PutUrlIntoCooldown_AndHealthyUrlIsPreferred()
    {
        var urls = new[] { "http://bad/x", "http://good/x" };
        var balancer = CreateDlBalancer(urls);

        balancer.ReportSuccess("http://good/x", avgMbps: 80, duration: 1);
        balancer.ReportFailure("http://bad/x");
        balancer.ReportFailure("http://bad/x"); // 连续 2 次失败进入 10s 冷却

        for (var i = 0; i < 6; i++)
            Assert.Equal("http://good/x", balancer.GetUrlForWorker(i));
    }

    [Fact]
    public void Timeout_PutsUrlIntoCooldown_Immediately()
    {
        var urls = new[] { "http://timeout/x", "http://good/x" };
        var balancer = CreateDlBalancer(urls);

        balancer.ReportSuccess("http://good/x", avgMbps: 80, duration: 1);
        balancer.ReportTimeout("http://timeout/x"); // 超时一次即冷却

        for (var i = 0; i < 6; i++)
            Assert.Equal("http://good/x", balancer.GetUrlForWorker(i));
    }

    [Fact]
    public void WhenAllUrlsUnhealthy_DoesNotDeadlock_ReturnsAUrl()
    {
        var urls = new[] { "http://only/x" };
        var balancer = CreateDlBalancer(urls);
        balancer.ReportFailure("http://only/x");
        balancer.ReportFailure("http://only/x");

        // 只有一个 URL 且在冷却期，也必须返回一个 URL（回退），不能返回空串导致 worker 空转。
        var picked = balancer.GetUrlForWorker(0);
        Assert.Equal("http://only/x", picked);
    }

    [Fact]
    public void BuildDetails_MarksFailedAndTimeoutUrls_AndKeepsHealthyClean()
    {
        var urls = new[] { "http://ok/x", "http://fail/x", "http://timeout/x", "http://untouched/x" };
        var balancer = CreateDlBalancer(urls);

        balancer.ReportSuccess("http://ok/x", avgMbps: 90, duration: 1);
        balancer.ReportFailure("http://fail/x");
        balancer.ReportFailure("http://fail/x");
        balancer.ReportTimeout("http://timeout/x");

        var details = balancer.BuildDetails().ToDictionary(d => d.Url);

        Assert.False(details["http://ok/x"].IsFailed);
        Assert.True(details["http://fail/x"].IsFailed);
        Assert.Contains("failed", details["http://fail/x"].ErrorMessage ?? "");
        Assert.True(details["http://timeout/x"].IsFailed);
        Assert.Contains("Timeout", details["http://timeout/x"].ErrorMessage ?? "");
        // 从未分配过的 URL 不得标记为失败。
        Assert.False(details["http://untouched/x"].IsFailed);
        Assert.Null(details["http://untouched/x"].ErrorMessage);
    }

    [Fact]
    public void Constructor_DeduplicatesUrls_CaseInsensitive()
    {
        var balancer = new SpeedTestService.UrlBalancer(
            new[] { "http://A/x", "http://a/x", "http://b/x" }, useFastestAfterProbe: false);

        balancer.ReportSuccess("http://a/x", avgMbps: 10, duration: 1);
        var details = balancer.BuildDetails();
        Assert.Equal(2, details.Count);
    }

    [Fact]
    public void UploadBalancer_DoesNotAutoSwitchToFastest_RotatesUrls()
    {
        // 上传路径 useFastestAfterProbe=false：不应因为某个 URL“快”就完全压到一个地址。
        var balancer = new SpeedTestService.UrlBalancer(
            new[] { "http://a/u", "http://b/u" }, useFastestAfterProbe: false);

        var seen = new HashSet<string>
        {
            balancer.GetUrlForWorker(0),
            balancer.GetUrlForWorker(0),
            balancer.GetUrlForWorker(0),
            balancer.GetUrlForWorker(0)
        };

        // 轮转游标下两个 URL 都应被取到。
        Assert.Equal(2, seen.Count);
    }
}
