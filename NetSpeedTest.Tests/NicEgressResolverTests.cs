using System.Collections.Generic;
using System.Net.Http;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// QA 补充：测速出口 HttpClient 解析（多网卡源 IP 绑定）纯逻辑测试，无网络依赖。
/// 对应风险 RM-NIC-005：绑定失效会让测速流量按默认路由从错误网卡出去。
/// </summary>
public class NicEgressResolverTests
{
    private static NetworkAdapterInfo Adapter(string name, string ip) => new()
    {
        Name = name,
        Id = name,
        IPAddress = ip,
        Gateway = "192.168.1.1"
    };

    [Fact]
    public void CallerProvidedClient_IsUsedAndMarkedBound_ButNotOwned()
    {
        var requested = new HttpClient();
        var fallback = new HttpClient();
        try
        {
            var (client, bound, owns, bindFailed) = SpeedTestService.ResolveEgressClientCore(
                new[] { Adapter("eth0", "192.168.1.10") },
                requestedClient: requested,
                hasMultipleActiveAdapters: true,
                boundClientFactory: _ => new HttpClient(),
                fallbackClient: fallback);

            Assert.Same(requested, client);
            Assert.True(bound);
            Assert.False(owns);
            Assert.False(bindFailed);
        }
        finally
        {
            requested.Dispose();
            fallback.Dispose();
        }
    }

    [Fact]
    public void SingleAdapter_MultiActiveSystem_FactoryClient_IsBoundAndOwned()
    {
        var fallback = new HttpClient();
        var bound = new HttpClient();
        try
        {
            var (client, isBound, owns, bindFailed) = SpeedTestService.ResolveEgressClientCore(
                new[] { Adapter("eth0", "192.168.1.10") },
                requestedClient: null,
                hasMultipleActiveAdapters: true,
                boundClientFactory: _ => bound,
                fallbackClient: fallback);

            Assert.Same(bound, client);
            Assert.True(isBound);
            Assert.True(owns);
            Assert.False(bindFailed);
        }
        finally
        {
            bound.Dispose();
            fallback.Dispose();
        }
    }

    [Fact]
    public void SingleAdapter_BindFactoryReturnsNull_IsMarkedBindFailed_AndFallsBack()
    {
        var fallback = new HttpClient();
        try
        {
            var (client, isBound, owns, bindFailed) = SpeedTestService.ResolveEgressClientCore(
                new[] { Adapter("eth0-no-ip", "") },
                requestedClient: null,
                hasMultipleActiveAdapters: true,
                boundClientFactory: _ => null, // 网卡无可用 IPv4，绑定失败
                fallbackClient: fallback);

            Assert.Same(fallback, client);
            Assert.False(isBound);
            Assert.False(owns);
            Assert.True(bindFailed); // 必须明确暴露绑定失败，由上层抛出而不是静默走错网卡
        }
        finally
        {
            fallback.Dispose();
        }
    }

    [Fact]
    public void SingleAdapter_SingleActiveSystem_UsesDefaultRoute_NoBind()
    {
        var fallback = new HttpClient();
        try
        {
            var (client, isBound, owns, bindFailed) = SpeedTestService.ResolveEgressClientCore(
                new[] { Adapter("eth0", "192.168.1.10") },
                requestedClient: null,
                hasMultipleActiveAdapters: false,
                boundClientFactory: _ => new HttpClient(),
                fallbackClient: fallback);

            Assert.Same(fallback, client);
            Assert.False(isBound);
            Assert.False(owns);
            Assert.False(bindFailed);
        }
        finally
        {
            fallback.Dispose();
        }
    }

    [Fact]
    public void MultipleAdapters_UseDefaultRoute_PerNicBindingHandledByCaller()
    {
        var fallback = new HttpClient();
        try
        {
            var (client, isBound, owns, bindFailed) = SpeedTestService.ResolveEgressClientCore(
                new[] { Adapter("eth0", "192.168.1.10"), Adapter("eth1", "10.0.0.10") },
                requestedClient: null,
                hasMultipleActiveAdapters: true,
                boundClientFactory: _ => new HttpClient(),
                fallbackClient: fallback);

            // 多网卡的逐卡绑定在 RunMultiNicTestsAsync 中分别创建，这里不应误绑定到第一张卡。
            Assert.Same(fallback, client);
            Assert.False(isBound);
            Assert.False(bindFailed);
        }
        finally
        {
            fallback.Dispose();
        }
    }
}
