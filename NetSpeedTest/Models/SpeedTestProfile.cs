namespace NetSpeedTest.Models;

/// <summary>
/// 测速配置：一组下载/上传 URL 的集合
/// </summary>
public class SpeedTestProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新建配置";
    public List<string> DownloadUrls { get; set; } = new();
    public List<string> UploadUrls { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
