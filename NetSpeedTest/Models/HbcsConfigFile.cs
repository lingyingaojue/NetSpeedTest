using System.Text.Json.Serialization;

namespace NetSpeedTest.Models;

/// <summary>
/// HBCS 配置文件格式（用于导入导出）
/// </summary>
public class HbcsConfigFile
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "HBCS_SPEED_TEST_CONFIG";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("selectedProfileId")]
    public int SelectedProfileId { get; set; } = 1;

    [JsonPropertyName("profiles")]
    public List<HbcsProfileEntry> Profiles { get; set; } = new();
}

public class HbcsProfileEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrls")]
    public List<string> DownloadUrls { get; set; } = new();

    [JsonPropertyName("uploadUrls")]
    public List<string> UploadUrls { get; set; } = new();
}
