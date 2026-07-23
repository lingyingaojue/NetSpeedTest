using System.Globalization;
using Microsoft.Data.Sqlite;
using NetSpeedTest.Models;
using System.IO;

namespace NetSpeedTest.Services;

/// <summary>
/// SQLite 数据持久化服务
/// </summary>
public class DataService
{
    private readonly string _connectionString;

    public DataService()
    {
        var dbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
        Directory.CreateDirectory(dbDir);
        _connectionString = $"Data Source={Path.Combine(dbDir, "NetSpeedTest.db")}";
    }

    /// <summary>
    /// 初始化数据库，自动建表
    /// </summary>
    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // 测速记录表
        using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = """
            CREATE TABLE IF NOT EXISTS SpeedTestRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                DownloadMbps REAL NOT NULL,
                UploadMbps REAL,
                LatencyMs REAL NOT NULL,
                JitterMs REAL NOT NULL,
                PacketLoss REAL NOT NULL,
                NodeName TEXT NOT NULL,
                NetworkAdapterName TEXT NOT NULL,
                BytesDownloaded INTEGER NOT NULL,
                BytesUploaded INTEGER NOT NULL,
                DurationSeconds REAL NOT NULL,
                ThreadCount INTEGER NOT NULL DEFAULT 1,
                PeakMbps REAL NOT NULL DEFAULT 0,
                WanLatencyMs REAL,
                AverageTotalMbps REAL NOT NULL DEFAULT 0,
                TotalBytes INTEGER NOT NULL DEFAULT 0,
                TestType TEXT NOT NULL DEFAULT ''
            )
            """;
        cmd1.ExecuteNonQuery();

        // 自动迁移：兼容旧版本数据库
        MigrateTable(connection);

        // 自定义节点表
        using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = """
            CREATE TABLE IF NOT EXISTS CustomNodes (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                DownloadUrl TEXT NOT NULL,
                UploadUrl TEXT,
                PingHost TEXT,
                ISP TEXT NOT NULL,
                SortOrder INTEGER NOT NULL
            )
            """;
        cmd2.ExecuteNonQuery();
    }

    /// <summary>
    /// 自动迁移：兼容旧版本数据库缺失列
    /// </summary>
    private static void MigrateTable(SqliteConnection connection)
    {
        var columns = new Dictionary<string, string>
        {
            ["PeakMbps"] = "ALTER TABLE SpeedTestRecords ADD COLUMN PeakMbps REAL NOT NULL DEFAULT 0",
            ["WanLatencyMs"] = "ALTER TABLE SpeedTestRecords ADD COLUMN WanLatencyMs REAL",
            ["AverageTotalMbps"] = "ALTER TABLE SpeedTestRecords ADD COLUMN AverageTotalMbps REAL NOT NULL DEFAULT 0",
            ["TotalBytes"] = "ALTER TABLE SpeedTestRecords ADD COLUMN TotalBytes INTEGER NOT NULL DEFAULT 0",
            ["TestType"] = "ALTER TABLE SpeedTestRecords ADD COLUMN TestType TEXT NOT NULL DEFAULT ''",
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(SpeedTestRecords)";
        var existing = new HashSet<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            existing.Add(reader.GetString(1));

        foreach (var (name, sql) in columns)
        {
            if (!existing.Contains(name))
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = sql;
                try { alter.ExecuteNonQuery(); } catch { }
            }
        }
    }

    /// <summary>
    /// 保存测速结果
    /// </summary>
    public void SaveResult(SpeedTestResult result)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SpeedTestRecords (Timestamp, DownloadMbps, UploadMbps, LatencyMs, JitterMs,
                PacketLoss, NodeName, NetworkAdapterName, BytesDownloaded, BytesUploaded, DurationSeconds, ThreadCount, PeakMbps,
                WanLatencyMs, AverageTotalMbps, TotalBytes, TestType)
            VALUES (@ts, @dl, @ul, @lat, @jit, @pl, @nn, @na, @bd, @bu, @dur, @tc, @pk, @wl, @at, @tb, @tt)
            """;

        cmd.Parameters.AddWithValue("@ts", result.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@dl", result.DownloadMbps);
        cmd.Parameters.AddWithValue("@ul", (object?)result.UploadMbps ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lat", result.LatencyMs);
        cmd.Parameters.AddWithValue("@jit", result.JitterMs);
        cmd.Parameters.AddWithValue("@pl", result.PacketLoss);
        cmd.Parameters.AddWithValue("@nn", result.NodeName);
        cmd.Parameters.AddWithValue("@na", result.NetworkAdapterName);
        cmd.Parameters.AddWithValue("@bd", result.BytesDownloaded);
        cmd.Parameters.AddWithValue("@bu", result.BytesUploaded);
        cmd.Parameters.AddWithValue("@dur", result.DurationSeconds);
        cmd.Parameters.AddWithValue("@tc", result.ThreadCount);
        cmd.Parameters.AddWithValue("@pk", result.PeakMbps);
        cmd.Parameters.AddWithValue("@wl", (object?)result.WanLatencyMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at", result.AverageTotalMbps);
        cmd.Parameters.AddWithValue("@tb", result.TotalBytes);
        cmd.Parameters.AddWithValue("@tt", result.TestType);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 分页获取测速记录
    /// </summary>
    public List<SpeedTestResult> GetRecords(int page = 1, int pageSize = 20)
    {
        var results = new List<SpeedTestResult>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Timestamp, DownloadMbps, UploadMbps, LatencyMs, JitterMs, PacketLoss,
                   NodeName, NetworkAdapterName, BytesDownloaded, BytesUploaded, DurationSeconds, ThreadCount, PeakMbps,
                   WanLatencyMs, AverageTotalMbps, TotalBytes, TestType
            FROM SpeedTestRecords
            ORDER BY Timestamp DESC
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddWithValue("@limit", pageSize);
        cmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                int threadCount = 1;
                try { threadCount = reader.GetInt32(12); } catch { }
                double peakMbps = 0;
                try { peakMbps = reader.GetDouble(13); } catch { }
                string testType = "";
                try { testType = reader.GetString(17); } catch { }

                results.Add(new SpeedTestResult
                {
                    Id = reader.GetInt32(0),
                    Timestamp = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    DownloadMbps = reader.GetDouble(2),
                    UploadMbps = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    LatencyMs = reader.GetDouble(4),
                    JitterMs = reader.GetDouble(5),
                    PacketLoss = reader.GetDouble(6),
                    NodeName = reader.GetString(7),
                    NetworkAdapterName = reader.GetString(8),
                    BytesDownloaded = reader.GetInt64(9),
                    BytesUploaded = reader.GetInt64(10),
                    DurationSeconds = reader.GetDouble(11),
                    ThreadCount = threadCount,
                    PeakMbps = peakMbps,
                    WanLatencyMs = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                    AverageTotalMbps = reader.GetDouble(15),
                    TotalBytes = reader.GetInt64(16),
                    TestType = testType
                });
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// 删除测速记录
    /// </summary>
    public void DeleteRecord(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SpeedTestRecords WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 获取测速记录总数
    /// </summary>
    public int GetRecordCount()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SpeedTestRecords";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
}
