namespace NetSpeedTest.Models;

/// <summary>
/// 网卡信息模型
/// </summary>
public class NetworkAdapterInfo
{
    /// <summary>
    /// 网卡唯一标识
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 硬件描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// IPv4 地址
    /// </summary>
    public string? IPAddress { get; set; }

    /// <summary>
    /// 子网掩码
    /// </summary>
    public string? SubnetMask { get; set; }

    /// <summary>
    /// 默认网关
    /// </summary>
    public string? Gateway { get; set; }

    /// <summary>
    /// MAC 地址
    /// </summary>
    public string? MacAddress { get; set; }

    /// <summary>
    /// 协商速率（bps）
    /// </summary>
    public long? LinkSpeedBps { get; set; }

    /// <summary>
    /// 是否物理网卡
    /// </summary>
    public bool IsPhysical { get; set; }

    /// <summary>
    /// 是否 WiFi
    /// </summary>
    public bool IsWifi { get; set; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsOperational { get; set; }
}
