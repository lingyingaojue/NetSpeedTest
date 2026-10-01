using System.Collections.Generic;
using System.Linq;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// B1-1 (F-02) 补充回归测试：上传路径必须产出逐 URL 结果明细。
/// 旧实现让 RunUploadTestAsync 返回空 UrlDetails，导致服务器全部拒收时
/// 状态仍退化为“测速完成”，用户无法看出失败。
/// </summary>
public class UrlDetailReportingTests
{
    private static SpeedTestService.UrlBalancer NewBalancer(params string[] urls)
        => new(urls, useFastestAfterProbe: false);

    [Fact]
    public void BuildDetails_reports_every_url_including_untouched_ones()
    {
        var balancer = NewBalancer("https://a.example/x", "https://b.example/x");

        var details = balancer.BuildDetails();

        Assert.Equal(2, details.Count);
        Assert.Equal(new[] { "https://a.example/x", "https://b.example/x" }, details.Select(d => d.Url).ToArray());
    }

    [Fact]
    public void BuildDetails_marks_rejected_url_as_failed()
    {
        var balancer = NewBalancer("https://reject.example/x");
        _ = balancer.GetUrlForWorker(0);
        balancer.ReportFailure("https://reject.example/x");

        var detail = Assert.Single(balancer.BuildDetails());

        Assert.True(detail.IsFailed);
        Assert.NotNull(detail.ErrorMessage);
        Assert.Contains("Rejected", detail.ErrorMessage!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDetails_marks_timed_out_url_as_failed_with_timeout_reason()
    {
        var balancer = NewBalancer("https://slow.example/x");
        _ = balancer.GetUrlForWorker(0);
        balancer.ReportTimeout("https://slow.example/x");

        var detail = Assert.Single(balancer.BuildDetails());

        Assert.True(detail.IsFailed);
        Assert.Contains("Timeout", detail.ErrorMessage!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDetails_does_not_mark_successful_url_as_failed()
    {
        var balancer = NewBalancer("https://ok.example/x");
        _ = balancer.GetUrlForWorker(0);
        balancer.ReportSuccess("https://ok.example/x", 42.5, 1.0);

        var detail = Assert.Single(balancer.BuildDetails());

        Assert.False(detail.IsFailed);
        Assert.Null(detail.ErrorMessage);
        Assert.Equal(42.5, detail.AvgMbps);
    }

    [Fact]
    public void BuildDetails_does_not_mark_untouched_url_as_failed()
    {
        // 未被分配到的 URL 不应被算成失败，否则成功率会被误报。
        var balancer = NewBalancer("https://touched.example/x", "https://untouched.example/x");
        _ = balancer.GetUrlForWorker(0);
        balancer.ReportSuccess("https://touched.example/x", 10, 1);

        var details = balancer.BuildDetails();

        Assert.False(details.Single(d => d.Url == "https://untouched.example/x").IsFailed);
        Assert.False(details.Single(d => d.Url == "https://touched.example/x").IsFailed);
    }

    [Fact]
    public void BuildDetails_treats_mixed_success_and_failure_as_success()
    {
        // 只要有一次成功就不算失败（与下载路径的成功/失败判定保持一致）。
        var balancer = NewBalancer("https://flaky.example/x");
        _ = balancer.GetUrlForWorker(0);
        balancer.ReportFailure("https://flaky.example/x");
        balancer.ReportSuccess("https://flaky.example/x", 5, 1);

        var detail = Assert.Single(balancer.BuildDetails());

        Assert.False(detail.IsFailed);
    }

    [Fact]
    public void BuildDetails_populates_host_for_each_url()
    {
        var balancer = NewBalancer("https://host.example:8443/path/file.bin");

        var detail = Assert.Single(balancer.BuildDetails());

        Assert.Equal("host.example", detail.Host);
    }

    [Fact]
    public void BuildDetails_is_empty_for_empty_url_list()
    {
        var balancer = NewBalancer();
        Assert.Empty(balancer.BuildDetails());
    }

    [Fact]
    public async System.Threading.Tasks.Task BuildDetails_is_safe_under_concurrent_reporting()
    {
        var urls = Enumerable.Range(0, 8).Select(i => $"https://h{i}.example/x").ToArray();
        var balancer = NewBalancer(urls);

        var tasks = urls.Select((u, i) => System.Threading.Tasks.Task.Run(() =>
        {
            for (var n = 0; n < 200; n++)
            {
                if ((i + n) % 3 == 0) balancer.ReportFailure(u);
                else balancer.ReportSuccess(u, n, 1);
                _ = balancer.GetUrlForWorker(i);
            }
        })).ToArray();

        var reader = System.Threading.Tasks.Task.Run(() =>
        {
            for (var n = 0; n < 200; n++) _ = balancer.BuildDetails();
        });

        await System.Threading.Tasks.Task.WhenAll(tasks.Append(reader));
        Assert.Equal(urls.Length, balancer.BuildDetails().Count);
    }

    // ===== BUG-BIDI-002：双向测速必须合并下载/上传两侧明细，不能丢空 =====

    private static void ReportAllSuccess(SpeedTestService.UrlBalancer balancer, params string[] urls)
    {
        for (var i = 0; i < urls.Length; i++)
        {
            _ = balancer.GetUrlForWorker(i);
            balancer.ReportSuccess(urls[i], 10 + i, 1);
        }
    }

    [Fact]
    public void MergeFullUrlDetails_concatenates_download_and_upload_without_cross_dedup()
    {
        var dlUrls = new[] { "https://d1.example/x", "https://d2.example/x", "https://d3.example/x" };
        var ulUrls = new[] { "https://u1.example/x", "https://u2.example/x" };
        var dl = NewBalancer(dlUrls);
        var ul = NewBalancer(ulUrls);
        ReportAllSuccess(dl, dlUrls);
        ReportAllSuccess(ul, ulUrls);

        var merged = SpeedTestService.MergeFullUrlDetails(dl.BuildDetails(), ul.BuildDetails());

        Assert.Equal(5, merged.Count);
        Assert.Equal(3, merged.Count(d => d.Direction == "下载"));
        Assert.Equal(2, merged.Count(d => d.Direction == "上传"));
        Assert.All(merged, d => Assert.False(d.IsFailed));
    }

    [Fact]
    public void MergeFullUrlDetails_keeps_same_url_on_both_directions_as_two_entries()
    {
        const string same = "https://same.example/x";
        var dl = NewBalancer(same);
        _ = dl.GetUrlForWorker(0);
        dl.ReportSuccess(same, 10, 1);
        var ul = NewBalancer(same);
        _ = ul.GetUrlForWorker(0);
        ul.ReportSuccess(same, 4, 1);

        var merged = SpeedTestService.MergeFullUrlDetails(dl.BuildDetails(), ul.BuildDetails());

        Assert.Equal(2, merged.Count(d => d.Url == same));
        Assert.Contains(merged, d => d.Url == same && d.Direction == "下载");
        Assert.Contains(merged, d => d.Url == same && d.Direction == "上传");
    }

    [Fact]
    public void MergeFullUrlDetails_preserves_failed_items_from_either_side()
    {
        var dl = NewBalancer("https://ok-dl.example/x", "https://bad-dl.example/x");
        _ = dl.GetUrlForWorker(0);
        dl.ReportSuccess("https://ok-dl.example/x", 10, 1);
        _ = dl.GetUrlForWorker(1);
        dl.ReportFailure("https://bad-dl.example/x");

        var ul = NewBalancer("https://ok-ul.example/x", "https://slow-ul.example/x");
        _ = ul.GetUrlForWorker(0);
        ul.ReportSuccess("https://ok-ul.example/x", 5, 1);
        _ = ul.GetUrlForWorker(1);
        ul.ReportTimeout("https://slow-ul.example/x");

        var merged = SpeedTestService.MergeFullUrlDetails(dl.BuildDetails(), ul.BuildDetails());

        Assert.Equal(4, merged.Count);
        Assert.Equal(2, merged.Count(d => !d.IsFailed));
        Assert.Equal(2, merged.Count(d => d.IsFailed));

        var badDl = merged.Single(d => d.Url == "https://bad-dl.example/x");
        Assert.True(badDl.IsFailed);
        Assert.Equal("下载", badDl.Direction);
        Assert.Contains("Rejected", badDl.ErrorMessage!, System.StringComparison.OrdinalIgnoreCase);

        var slowUl = merged.Single(d => d.Url == "https://slow-ul.example/x");
        Assert.True(slowUl.IsFailed);
        Assert.Equal("上传", slowUl.Direction);
        Assert.Contains("Timeout", slowUl.ErrorMessage!, System.StringComparison.OrdinalIgnoreCase);
    }
}
