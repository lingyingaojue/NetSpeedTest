using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetSpeedTest.Models;

namespace NetSpeedTest.Services;

/// <summary>
/// 测速配置（Profile）管理服务
/// </summary>
public class ProfileService
{
    private readonly string _connectionString;

    public ProfileService(string connectionString)
    {
        _connectionString = connectionString;
        InitializeTable();
        SeedDefaultProfiles();
    }

    private void InitializeTable()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS SpeedTestProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                DownloadUrls TEXT NOT NULL,
                UploadUrls TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 首次启动时插入默认配置
    /// </summary>
    private void SeedDefaultProfiles()
    {
        var existing = GetAllProfiles();
        if (existing.Count > 0) return;

        var defaultProfile = new SpeedTestProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "默认配置",
            DownloadUrls = new List<string>
            {
                "https://speedtest.tele2.net/100MB.zip",
                "https://ipv4.download.thinkbroadband.com/100MB.zip",
                "https://proof.ovh.net/files/100Mb.dat"
            },
            UploadUrls = new List<string>
            {
                "https://speedtest.tele2.net/upload.php",
                "https://httpbin.org/post"
            },
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        SaveProfile(defaultProfile);
    }

    /// <summary>
    /// 获取所有配置
    /// </summary>
    public List<SpeedTestProfile> GetAllProfiles()
    {
        var result = new List<SpeedTestProfile>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM SpeedTestProfiles ORDER BY UpdatedAt DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SpeedTestProfile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                DownloadUrls = SafeDeserialize(reader.GetString(2)),
                UploadUrls = SafeDeserialize(reader.GetString(3)),
                CreatedAt = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                UpdatedAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }
        return result;
    }

    private static List<string> SafeDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    /// <summary>
    /// 保存配置（新建或更新）
    /// </summary>
    public void SaveProfile(SpeedTestProfile profile)
    {
        profile.UpdatedAt = DateTime.Now;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO SpeedTestProfiles (Id, Name, DownloadUrls, UploadUrls, CreatedAt, UpdatedAt)
            VALUES (@id, @name, @downloadUrls, @uploadUrls, @createdAt, @updatedAt)";
        cmd.Parameters.AddWithValue("@id", profile.Id);
        cmd.Parameters.AddWithValue("@name", profile.Name);
        cmd.Parameters.AddWithValue("@downloadUrls", JsonSerializer.Serialize(profile.DownloadUrls));
        cmd.Parameters.AddWithValue("@uploadUrls", JsonSerializer.Serialize(profile.UploadUrls));
        cmd.Parameters.AddWithValue("@createdAt", profile.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", profile.UpdatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 删除配置
    /// </summary>
    public void DeleteProfile(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SpeedTestProfiles WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    // ========== 导入导出 ==========

    /// <summary>
    /// 从 .bin/.json 文件导入配置（HBCS 格式）
    /// </summary>
    public List<SpeedTestProfile> ImportFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空", nameof(filePath));
        var json = File.ReadAllText(filePath);
        var config = JsonSerializer.Deserialize<HbcsConfigFile>(json);

        if (config?.Profiles == null || config.Profiles.Count == 0)
            throw new Exception("配置文件中没有找到有效的测速配置");

        var imported = new List<SpeedTestProfile>();

        foreach (var entry in config.Profiles)
        {
            var profile = new SpeedTestProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = entry.Name,
                DownloadUrls = entry.DownloadUrls,
                UploadUrls = entry.UploadUrls,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            SaveProfile(profile);
            imported.Add(profile);
        }

        return imported;
    }

    /// <summary>
    /// 导出所有配置到 .json 文件（兼容 HBCS 格式）
    /// </summary>
    public void ExportToFile(List<SpeedTestProfile> profiles, string filePath)
    {
        if (profiles == null)
            throw new ArgumentNullException(nameof(profiles));
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空", nameof(filePath));
        var config = new HbcsConfigFile
        {
            Profiles = profiles.Select((p, i) => new HbcsProfileEntry
            {
                Id = i + 1,
                Name = p.Name,
                DownloadUrls = p.DownloadUrls,
                UploadUrls = p.UploadUrls
            }).ToList()
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }
}
