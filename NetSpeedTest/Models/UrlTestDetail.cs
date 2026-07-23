namespace NetSpeedTest.Models;

/// <summary>
/// 单个 URL 的测速明细
/// </summary>
public class UrlTestDetail
{
    /// <summary>
    /// 测速 URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// URL 域名/主机名
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 平均下载速率 (Mbps)
    /// </summary>
    public double AvgMbps { get; set; }

    /// <summary>
    /// 峰值速率 (Mbps)
    /// </summary>
    public double PeakMbps { get; set; }

    /// <summary>
    /// 下载总字节数
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// 测速耗时 (秒)
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// 是否超时/失败
    /// </summary>
    public bool IsFailed { get; set; }

    /// <summary>
    /// 失败原因
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 速率波形数据（仅内存）
    /// </summary>
    public List<RateDataPoint> RateHistory { get; set; } = new();
}
