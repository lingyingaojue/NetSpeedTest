using System;
using System.Threading;
using System.Threading.Tasks;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// B3 (FN-01) 回归测试：Web API 报“已启动”之前，必须确认测速真的进入运行态。
/// 命令内部有前置准备阶段（最长 15s 的 URL 预探测），因此 200 不能抢跑。
/// </summary>
public class TestStartConfirmationTests
{
    [Fact]
    public async Task WaitForTestRunning_returns_immediately_when_already_running()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = await WebServerService.WaitForTestRunningAsync(() => true, CancellationToken.None, TimeSpan.FromSeconds(5));
        sw.Stop();

        Assert.True(ok);
        Assert.True(sw.ElapsedMilliseconds < 500, $"should not wait when already running (took {sw.ElapsedMilliseconds}ms)");
    }

    [Fact]
    public async Task WaitForTestRunning_observes_a_late_transition()
    {
        // 模拟命令内部的前置准备阶段：稍后才进入运行态。
        var running = false;
        _ = Task.Run(async () =>
        {
            await Task.Delay(700);
            Volatile.Write(ref running, true);
        });

        var ok = await WebServerService.WaitForTestRunningAsync(
            () => Volatile.Read(ref running), CancellationToken.None, TimeSpan.FromSeconds(10));

        Assert.True(ok);
    }

    [Fact]
    public async Task WaitForTestRunning_times_out_when_test_never_starts()
    {
        // 命令执行了但内部校验失败（例如未选网卡）：必须汇报失败，不能假成功。
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = await WebServerService.WaitForTestRunningAsync(
            () => false, CancellationToken.None, TimeSpan.FromMilliseconds(400));
        sw.Stop();

        Assert.False(ok);
        Assert.True(sw.ElapsedMilliseconds >= 300, $"should have waited for the timeout (took {sw.ElapsedMilliseconds}ms)");
    }

    [Fact]
    public async Task WaitForTestRunning_honours_client_cancellation()
    {
        using var cts = new CancellationTokenSource(150);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = await WebServerService.WaitForTestRunningAsync(() => false, cts.Token, TimeSpan.FromSeconds(30));
        sw.Stop();

        Assert.False(ok);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"cancellation should short-circuit (took {sw.ElapsedMilliseconds}ms)");
    }

    [Fact]
    public void TestStartConfirmationTimeout_exceeds_the_internal_preparation_budget()
    {
        // 命令内部 URL 预探测上限为 15s；确认窗口必须更长，否则正常启动会被误判为失败。
        Assert.True(WebServerService.TestStartConfirmationTimeout > TimeSpan.FromSeconds(15),
            $"confirmation timeout is {WebServerService.TestStartConfirmationTimeout}");
    }
}

/// <summary>
/// B2 (F-09) 回归测试：Dispatcher 辅助方法在 UI 线程不可用/超时时必须返回失败，
/// 而不是无限等待或假装成功。
/// </summary>
public class UiDispatchGuardTests
{
    [Fact]
    public void TryRunOnUiThread_returns_false_when_no_application_exists()
    {
        // 单元测试进程没有 Application.Current：必须安全失败。
        var ran = false;
        var ok = WebServerService.TryRunOnUiThread(() => ran = true, TimeSpan.FromMilliseconds(200));

        Assert.False(ok);
        Assert.False(ran);

        var result = WebServerService.TryRunOnUiThread<string>(() => "value", TimeSpan.FromMilliseconds(200));
        Assert.Null(result);
    }
}

/// <summary>
/// B2 (FN-06 / FN-07) 回归测试：错误响应文案固定，不泄露内部细节。
/// </summary>
public class ErrorPayloadTests
{
    [Theory]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(500, "Internal Server Error")]
    public void ErrorPayload_uses_stable_classification(int status, string expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(WebServerService.ErrorPayload(status));
        Assert.Contains(expected, json, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorPayload_403_matches_the_token_rejection_message()
    {
        // 令牌缺失与网段拒绝共用同一 403 文案（FN-07）。
        var json = System.Text.Json.JsonSerializer.Serialize(WebServerService.ErrorPayload(403));
        Assert.Contains("Forbidden", json, StringComparison.Ordinal);
        Assert.DoesNotContain("netsh", json, StringComparison.OrdinalIgnoreCase);
    }
}
