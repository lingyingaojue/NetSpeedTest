namespace NetSpeedTest.Models;

/// <summary>
/// 单批丢包率探测结果。
/// </summary>
public sealed class PacketLossSample
{
    public int Sent { get; init; }

    public int Received { get; init; }

    public string Target { get; init; } = "";

    public string Method { get; init; } = "";

    /// <summary>
    /// 本批丢包率（百分比，0~100）。
    /// </summary>
    public double Percent => Sent <= 0 ? 0 : Math.Max(0, Math.Min(100, (Sent - Received) * 100.0 / Sent));
}
