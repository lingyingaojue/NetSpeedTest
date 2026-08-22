namespace NetSpeedTest.Models;

/// <summary>
/// 网卡 → 网段 访问映射。
/// </summary>
public sealed class AdapterAccessBinding
{
    public string AdapterName { get; init; } = "";

    public string Description { get; init; } = "";

    public string IPAddress { get; init; } = "";

    public string SubnetMask { get; init; } = "";

    public int PrefixLength { get; init; }

    public string Subnet { get; init; } = "";

    public string Url { get; init; } = "";

    public string DisplayText => $"{IPAddress}/{PrefixLength}";
}
