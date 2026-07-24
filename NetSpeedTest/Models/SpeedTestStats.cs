namespace NetSpeedTest.Models;

public class SpeedTestStats
{
    public int TotalCount { get; set; }
    public double? MaxDownloadMbps { get; set; }
    public double? MaxUploadMbps { get; set; }
    public double? AvgDownloadMbps { get; set; }
    public double? MinLatencyMs { get; set; }
}
