namespace NetSpeedTest.Models;

/// <summary>
/// 测速结果模型
/// </summary>
public class SpeedTestResult
{
    /// <summary>
    /// 自增主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 测速时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// 下载速率（Mbps）
    /// </summary>
    public double DownloadMbps { get; set; }

    public double PeakMbps { get; set; }

    public double? UploadMbps { get; set; }

    /// <summary>
    /// 平均延迟（ms）
    /// </summary>
    public double LatencyMs { get; set; }

    /// <summary>
    /// 抖动（标准差，ms）
    /// </summary>
    public double JitterMs { get; set; }

    /// <summary>
    /// 丢包率（百分比）
    /// </summary>
    public double PacketLoss { get; set; }

    /// <summary>
    /// 使用的节点名称
    /// </summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>
    /// 使用的网卡名称
    /// </summary>
    public string NetworkAdapterName { get; set; } = string.Empty;

    /// <summary>
    /// 下载字节数
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// 上传字节数
    /// </summary>
    public long BytesUploaded { get; set; }

    /// <summary>
    /// 测速耗时（秒）
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// 并发线程数
    /// </summary>
    public int ThreadCount { get; set; }

    /// <summary>
    /// 速率波形数据（仅内存，不入库）
    /// </summary>
    public List<RateDataPoint> DownloadRateHistory { get; set; } = new();

    /// <summary>
    /// 每个 URL 的测速明细（仅内存，不入库）
    /// </summary>
    public List<UrlTestDetail> UrlDetails { get; set; } = new();
}
