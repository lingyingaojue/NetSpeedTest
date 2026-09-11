using System;

namespace NetSpeedTest.Models;

/// <summary>
/// 同一网段下的网卡访问地址分组。
/// </summary>
public sealed class AdapterSubnetGroup
{
    public string Subnet { get; init; } = "";

    public IReadOnlyList<AdapterAccessBinding> Bindings { get; init; } = Array.Empty<AdapterAccessBinding>();

    public int Count => Bindings.Count;
}

