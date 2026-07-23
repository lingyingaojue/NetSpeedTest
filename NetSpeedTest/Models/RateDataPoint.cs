namespace NetSpeedTest.Models;

/// <summary>
/// 速率波形数据点
/// </summary>
public class RateDataPoint
{
    /// <summary>
    /// 相对时间（秒）
    /// </summary>
    public double TimeSeconds { get; set; }

    /// <summary>
    /// 当前速率（Mbps）
    /// </summary>
    public double RateMbps { get; set; }
}
