using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// B1-2 / B1-3 回归测试：UDP 探测结果判定。
/// 已连接 UDP socket 在主机可达但端口关闭时会收到 ICMP Port Unreachable，
/// .NET 将其表现为 ConnectionReset 异常。旧实现把该异常吞掉且不计入成功，
/// 导致丢包恒 100%、网关延迟为 0。这些用例锁定修复后的语义。
/// </summary>
public class UdpProbeTests
{
    private static Task FaultedTask(Exception ex) => Task.FromException(ex);

    [Fact]
    public void ClassifyUdpProbeResult_treats_connection_reset_as_reachable()
    {
        // 主机可达：ICMP Port Unreachable 使 ReceiveAsync 以 ConnectionReset 失败。
        var ex = new SocketException((int)SocketError.ConnectionReset);
        var outcome = SpeedTestService.ClassifyUdpProbeResult(FaultedTask(ex));

        Assert.Equal(SpeedTestService.UdpProbeOutcome.PortUnreachable, outcome);
        Assert.NotEqual(SpeedTestService.UdpProbeOutcome.Timeout, outcome);
    }

    [Fact]
    public void ClassifyUdpProbeResult_treats_echoed_datagram_as_response()
    {
        var outcome = SpeedTestService.ClassifyUdpProbeResult(Task.CompletedTask);
        Assert.Equal(SpeedTestService.UdpProbeOutcome.Response, outcome);
    }

    [Fact]
    public void ClassifyUdpProbeResult_treats_pending_task_as_timeout()
    {
        var tcs = new TaskCompletionSource();
        try
        {
            var outcome = SpeedTestService.ClassifyUdpProbeResult(tcs.Task);
            Assert.Equal(SpeedTestService.UdpProbeOutcome.Timeout, outcome);
        }
        finally
        {
            tcs.SetResult();
        }
    }

    [Fact]
    public void ClassifyUdpProbeResult_ignores_unrelated_socket_errors()
    {
        // 只有 ConnectionReset 代表可达；其他错误仍按无响应处理。
        var ex = new SocketException((int)SocketError.HostUnreachable);
        var outcome = SpeedTestService.ClassifyUdpProbeResult(FaultedTask(ex));
        Assert.Equal(SpeedTestService.UdpProbeOutcome.Timeout, outcome);
    }

    [Fact]
    public void ClassifyUdpProbeResult_handles_aggregated_exceptions()
    {
        var aggregated = Task.WhenAll(
            FaultedTask(new InvalidOperationException("boom")),
            FaultedTask(new SocketException((int)SocketError.ConnectionReset)));

        Assert.True(aggregated.IsFaulted);
        Assert.Equal(SpeedTestService.UdpProbeOutcome.PortUnreachable,
            SpeedTestService.ClassifyUdpProbeResult(aggregated));
    }

    [Fact]
    public void ClassifyUdpProbeResult_rejects_null_task()
    {
        Assert.Equal(SpeedTestService.UdpProbeOutcome.Timeout,
            SpeedTestService.ClassifyUdpProbeResult(null!));
    }

    [Fact]
    public async Task SendUdpProbeAsync_reports_timeout_when_nothing_answers()
    {
        // 服务端收包但不回包，模拟真正的无响应主机。
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var drain = new CancellationTokenSource();
        var sink = Task.Run(async () =>
        {
            try
            {
                while (!drain.IsCancellationRequested) await server.ReceiveAsync();
            }
            catch { }
        });

        using var udp = new UdpClient();
        udp.Connect(new IPEndPoint(IPAddress.Loopback, serverPort));
        var outcome = await SpeedTestService.SendUdpProbeAsync(
            udp, new byte[] { 0x00 }, 400, CancellationToken.None);

        drain.Cancel();
        Assert.Equal(SpeedTestService.UdpProbeOutcome.Timeout, outcome);
        await Task.WhenAny(sink, Task.Delay(1000));
    }

    [Fact]
    public async Task SendUdpProbeAsync_reports_response_when_peer_echoes()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        using var udp = new UdpClient();
        udp.Connect(new IPEndPoint(IPAddress.Loopback, serverPort));

        var serverTask = Task.Run(async () =>
        {
            var received = await server.ReceiveAsync();
            await server.SendAsync(new byte[] { 0x01 }, 1, received.RemoteEndPoint);
        });

        var outcome = await SpeedTestService.SendUdpProbeAsync(
            udp, new byte[] { 0x00 }, 3000, CancellationToken.None);

        Assert.Equal(SpeedTestService.UdpProbeOutcome.Response, outcome);
        await serverTask;
    }

    [Fact]
    public async Task SendUdpProbeAsync_reports_port_unreachable_on_loopback_closed_port()
    {
        // Windows 会对回环上未监听的 UDP 端口回 ICMP Port Unreachable，
        // 这正是 B1-2/B1-3 修复要覆盖的真实路径。
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var port = FindClosedUdpPort();
        using var udp = new UdpClient();
        udp.Connect(new IPEndPoint(IPAddress.Loopback, port));

        var outcome = await SpeedTestService.SendUdpProbeAsync(
            udp, new byte[] { 0x00 }, 2000, CancellationToken.None);

        Assert.Equal(SpeedTestService.UdpProbeOutcome.PortUnreachable, outcome);
    }

    [Fact]
    public async Task SendUdpProbeAsync_never_reports_success_when_cancelled()
    {
        // 取消后不得再把结果当作“主机有响应”，否则测速停止时会污染丢包/延迟统计。
        var port = FindClosedUdpPort();
        using var udp = new UdpClient();
        udp.Connect(new IPEndPoint(IPAddress.Loopback, port));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await SpeedTestService.SendUdpProbeAsync(
            udp, new byte[] { 0x00 }, 5000, cts.Token);

        Assert.True(outcome != SpeedTestService.UdpProbeOutcome.Response,
            "cancelled probe must never be classified as an echoed datagram");
        Assert.True(outcome == SpeedTestService.UdpProbeOutcome.Timeout
                        || outcome == SpeedTestService.UdpProbeOutcome.PortUnreachable,
            $"unexpected outcome after cancellation: {outcome}");
    }

    /// <summary>取一个当前没有 UDP 监听的端口。</summary>
    private static int FindClosedUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}
