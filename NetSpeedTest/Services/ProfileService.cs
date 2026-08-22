using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using NetSpeedTest.Models;

namespace NetSpeedTest.Services;

/// <summary>
/// 测速配置（Profile）管理服务
/// </summary>
public class ProfileService
{
    private readonly string _connectionString;

    public ProfileService(string connectionString, IConfiguration? config = null)
    {
        _connectionString = connectionString;
        InitializeTable();
        SeedDefaultProfiles(config);
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
    private void SeedDefaultProfiles(IConfiguration? config)
    {
        var existing = GetAllProfiles();
        if (existing.Count > 0) return;

        var dlUrls = new List<string>();
        var ulUrls = new List<string>();
        try
        {
            var nodes = config?.GetSection("PresetNodes").GetChildren();
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var dl = node["DownloadUrl"];
                    if (!string.IsNullOrWhiteSpace(dl) && !dlUrls.Contains(dl)) dlUrls.Add(dl);
                    var ul = node["UploadUrl"];
                    if (!string.IsNullOrWhiteSpace(ul) && !ulUrls.Contains(ul)) ulUrls.Add(ul);
                }
            }
        }
        catch { }

        if (dlUrls.Count == 0)
        {
            dlUrls.Add("https://speedtest.tele2.net/100MB.zip");
            dlUrls.Add("https://ipv4.download.thinkbroadband.com/100MB.zip");
            dlUrls.Add("https://proof.ovh.net/files/100Mb.dat");
        }
        if (ulUrls.Count == 0)
        {
            ulUrls.Add("https://speedtest.tele2.net/upload.php");
            ulUrls.Add("https://httpbin.org/post");
        }

        var defaultProfile = new SpeedTestProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "默认配置",
            DownloadUrls = dlUrls,
            UploadUrls = ulUrls,
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
                CreatedAt = SafeParseDate(reader.GetString(4)),
                UpdatedAt = SafeParseDate(reader.GetString(5))
            });
        }
        return result;
    }

    private static DateTime SafeParseDate(string s)
    {
        try { return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }
        catch (Exception ex) { Logger.Log($"Date parse failed: {ex.Message}"); return DateTime.MinValue; }
    }

    private static List<string> SafeDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (Exception ex) { Logger.Log($"JSON deserialize failed: {ex.Message}"); return new(); }
    }

    /// <summary>
    /// 保存配置（新建或更新）
    /// </summary>
    public void SaveProfile(SpeedTestProfile profile)
    {
        profile.UpdatedAt = DateTime.Now;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var createdAt = profile.CreatedAt;
        try { using var ck = conn.CreateCommand(); ck.CommandText = "SELECT CreatedAt FROM SpeedTestProfiles WHERE Id=@id"; ck.Parameters.AddWithValue("@id", profile.Id); var existing = ck.ExecuteScalar(); if (existing != null && existing != DBNull.Value) createdAt = SafeParseDate(existing.ToString()!); } catch { }
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO SpeedTestProfiles (Id, Name, DownloadUrls, UploadUrls, CreatedAt, UpdatedAt)
            VALUES (@id, @name, @downloadUrls, @uploadUrls, @createdAt, @updatedAt)";
        cmd.Parameters.AddWithValue("@id", profile.Id);
        cmd.Parameters.AddWithValue("@name", profile.Name);
        cmd.Parameters.AddWithValue("@downloadUrls", JsonSerializer.Serialize(profile.DownloadUrls));
        cmd.Parameters.AddWithValue("@uploadUrls", JsonSerializer.Serialize(profile.UploadUrls));
        cmd.Parameters.AddWithValue("@createdAt", createdAt.ToString("O"));
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
    /// 从 .bin/.json 文件导入配置（HBCS 格式）。urlFilter 非空时在入库前过滤（返回 true 的 URL 被剔除）。
    /// </summary>
    public List<SpeedTestProfile> ImportFromFile(string filePath, Func<string, bool>? urlFilter = null)
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
            if (entry == null) continue;
            var downloads = entry.DownloadUrls ?? new List<string>();
            var uploads = entry.UploadUrls ?? new List<string>();
            var profile = new SpeedTestProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(entry.Name) ? "导入配置" : entry.Name,
                DownloadUrls = urlFilter == null ? downloads : downloads.Where(u => !urlFilter(u)).ToList(),
                UploadUrls = urlFilter == null ? uploads : uploads.Where(u => !urlFilter(u)).ToList(),
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
