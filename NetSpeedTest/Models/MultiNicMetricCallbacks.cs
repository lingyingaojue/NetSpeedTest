namespace NetSpeedTest.Models;

/// <summary>
/// 多网卡测速时，按网卡回传质量指标的委托集合。
/// </summary>
public sealed class MultiNicMetricCallbacks
{
    public Action<NetworkAdapterInfo, double>? OnLatency { get; init; }

    public Action<NetworkAdapterInfo, double>? OnWanLatency { get; init; }

    public Action<NetworkAdapterInfo, double>? OnJitterRtt { get; init; }
}
