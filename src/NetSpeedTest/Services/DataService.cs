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
    private readonly object _cacheLock = new();
    private readonly SemaphoreSlim _countGate = new(1, 1);
    private readonly SemaphoreSlim _statsGate = new(1, 1);
    private int? _recordCountCache;
    private SpeedTestStats? _statsCache;
    private double _downloadSumMbps;
    private int _downloadSampleCount;
    private int _recordCountVersion;
    private int _statsVersion;

    public DataService()
    {
        var dbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
        Directory.CreateDirectory(dbDir);
        _connectionString = $"Data Source={Path.Combine(dbDir, "NetSpeedTest.db")};Default Timeout=5";
    }

    /// <summary>
    /// 统一打开连接，并为每个连接设置 busy_timeout，避免与测速写入并发时长时间锁等待。
    /// </summary>
    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// 初始化数据库，自动建表
    /// </summary>
    public void Initialize()
    {
        using var connection = OpenConnection();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL";
        pragma.ExecuteNonQuery();
        pragma.CommandText = "PRAGMA busy_timeout = 5000";
        pragma.ExecuteNonQuery();

        // 测速记录表
        using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = """
            CREATE TABLE IF NOT EXISTS SpeedTestRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                DownloadMbps REAL,
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
                TestType TEXT NOT NULL DEFAULT '',
                BatchId TEXT,
                ErrorMessage TEXT
            )
            """;
        cmd1.ExecuteNonQuery();

        // 自动迁移：兼容旧版本数据库
        MigrateTable(connection);

        using var idxCmd = connection.CreateCommand();
        idxCmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_SpeedTestRecords_Timestamp ON SpeedTestRecords(Timestamp)";
        idxCmd.ExecuteNonQuery();

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
            ["BatchId"] = "ALTER TABLE SpeedTestRecords ADD COLUMN BatchId TEXT",
            ["ErrorMessage"] = "ALTER TABLE SpeedTestRecords ADD COLUMN ErrorMessage TEXT",
            ["TestType"] = "ALTER TABLE SpeedTestRecords ADD COLUMN TestType TEXT NOT NULL DEFAULT ''",
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(SpeedTestRecords)";
        var existing = new HashSet<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                existing.Add(reader.GetString(1));
        }

        foreach (var (name, sql) in columns)
        {
            if (!existing.Contains(name))
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = sql;
                try { alter.ExecuteNonQuery(); } catch (Exception ex) { Logger.Log($"Migration failed: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// 保存测速结果
    /// </summary>
    public void SaveResult(SpeedTestResult result)
    {
        using var connection = OpenConnection();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SpeedTestRecords (Timestamp, DownloadMbps, UploadMbps, LatencyMs, JitterMs,
                PacketLoss, NodeName, NetworkAdapterName, BytesDownloaded, BytesUploaded, DurationSeconds, ThreadCount, PeakMbps,
                WanLatencyMs, AverageTotalMbps, TotalBytes, TestType, BatchId, ErrorMessage)
            VALUES (@ts, @dl, @ul, @lat, @jit, @pl, @nn, @na, @bd, @bu, @dur, @tc, @pk, @wl, @at, @tb, @tt, @bid, @err)
            """;

        cmd.Parameters.AddWithValue("@ts", result.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@dl", (object?)result.DownloadMbps ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ul", (object?)result.UploadMbps ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lat", result.LatencyMs);
        cmd.Parameters.AddWithValue("@jit", (object?)result.JitterMs ?? 0.0);
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
        cmd.Parameters.AddWithValue("@bid", (object?)result.BatchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@err", (object?)result.ErrorMessage ?? DBNull.Value);

        cmd.ExecuteNonQuery();
        UpdateCachesAfterInsert(result);
    }

    /// <summary>
    /// 分页获取测速记录
    /// </summary>
    public List<SpeedTestResult> GetRecords(int page = 1, int pageSize = 20)
    {
        var results = new List<SpeedTestResult>();

        using var connection = OpenConnection();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Timestamp, DownloadMbps, UploadMbps, LatencyMs, JitterMs, PacketLoss,
                   NodeName, NetworkAdapterName, BytesDownloaded, BytesUploaded, DurationSeconds, ThreadCount, PeakMbps,
                   WanLatencyMs, AverageTotalMbps, TotalBytes, TestType, BatchId, ErrorMessage
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
                int threadCount = reader.IsDBNull(12) ? 1 : reader.GetInt32(12);
                double peakMbps = reader.IsDBNull(13) ? 0 : reader.GetDouble(13);
                string testType = reader.IsDBNull(17) ? "" : reader.GetString(17);

                results.Add(new SpeedTestResult
                {
                    Id = reader.GetInt32(0),
                    Timestamp = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    DownloadMbps = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                    UploadMbps = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    LatencyMs = reader.GetDouble(4),
                    JitterMs = reader.IsDBNull(5) ? null : (reader.GetDouble(5) == 0 ? null : reader.GetDouble(5)),
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
                    TestType = testType,
                    BatchId = reader.IsDBNull(18) ? null : reader.GetString(18),
                    ErrorMessage = reader.IsDBNull(19) ? null : reader.GetString(19),
                });
            }
            catch (Exception ex) { Logger.Log($"Record read failed: {ex.Message}"); }
        }

        return results;
    }

    /// <summary>
    /// 删除测速记录
    /// </summary>
    public void DeleteRecord(int id)
    {
        using var connection = OpenConnection();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SpeedTestRecords WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var affected = cmd.ExecuteNonQuery();
        if (affected <= 0) return;

        lock (_cacheLock)
        {
            _recordCountVersion++;
            _statsVersion++;

            if (_recordCountCache is int cachedCount)
                _recordCountCache = Math.Max(0, cachedCount - 1);

            InvalidateStatisticsCacheLocked();
        }
    }

    public void ClearAllRecords()
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SpeedTestRecords";
        cmd.ExecuteNonQuery();

        lock (_cacheLock)
        {
            _recordCountVersion++;
            _statsVersion++;
            _recordCountCache = 0;
            _statsCache = new SpeedTestStats();
            _downloadSumMbps = 0;
            _downloadSampleCount = 0;
        }
    }

    /// <summary>
    /// 获取测速记录总数（带内存缓存与 single-flight）。
    /// </summary>
    public int GetRecordCount()
    {
        lock (_cacheLock)
        {
            if (_recordCountCache is int cachedCount)
                return cachedCount;
        }

        _countGate.Wait();
        try
        {
            lock (_cacheLock)
            {
                if (_recordCountCache is int cachedCount)
                    return cachedCount;
            }

            var version = Volatile.Read(ref _recordCountVersion);
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM SpeedTestRecords";
            var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);

            lock (_cacheLock)
            {
                if (version == _recordCountVersion)
                    _recordCountCache = count;
            }

            return count;
        }
        finally
        {
            _countGate.Release();
        }
    }

    /// <summary>
    /// 获取历史记录统计（带内存缓存与 single-flight）。
    /// </summary>
    public SpeedTestStats GetStatistics()
    {
        lock (_cacheLock)
        {
            if (_statsCache != null)
                return CloneStats(_statsCache);
        }

        _statsGate.Wait();
        try
        {
            var lastStats = new SpeedTestStats();

            for (var attempt = 0; attempt < 2; attempt++)
            {
                int version;
                lock (_cacheLock)
                {
                    if (_statsCache != null)
                        return CloneStats(_statsCache);

                    version = _statsVersion;
                }

                var stats = ReadStatisticsFromDatabase(out var downloadSum, out var downloadSampleCount);
                lastStats = stats;

                lock (_cacheLock)
                {
                    if (version == _statsVersion)
                    {
                        _statsCache = stats;
                        _downloadSumMbps = downloadSum;
                        _downloadSampleCount = downloadSampleCount;
                        return CloneStats(stats);
                    }
                }
            }

            return CloneStats(lastStats);
        }
        finally
        {
            _statsGate.Release();
        }
    }

    private SpeedTestStats ReadStatisticsFromDatabase(out double downloadSum, out int downloadSampleCount)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*), MAX(DownloadMbps), MAX(UploadMbps), AVG(DownloadMbps), MIN(LatencyMs),
                       COALESCE(SUM(DownloadMbps), 0), COUNT(DownloadMbps)
                FROM SpeedTestRecords
                """;
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                downloadSum = reader.IsDBNull(5) ? 0 : reader.GetDouble(5);
                downloadSampleCount = reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetInt64(6));
                return new SpeedTestStats
                {
                    TotalCount = reader.GetInt32(0),
                    MaxDownloadMbps = reader.IsDBNull(1) ? null : reader.GetDouble(1),
                    MaxUploadMbps = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                    AvgDownloadMbps = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    MinLatencyMs = reader.IsDBNull(4) ? null : reader.GetDouble(4)
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"GetStatistics failed: {ex.Message}");
            throw;
        }

        downloadSum = 0;
        downloadSampleCount = 0;
        return new SpeedTestStats();
    }

    private void UpdateCachesAfterInsert(SpeedTestResult result)
    {
        lock (_cacheLock)
        {
            _recordCountVersion++;
            _statsVersion++;

            if (_recordCountCache is int cachedCount)
                _recordCountCache = cachedCount + 1;

            if (_statsCache == null) return;

            var stats = _statsCache;
            stats.TotalCount++;

            if (TryGetFinite(result.DownloadMbps, out var downloadMbps))
            {
                if (!stats.MaxDownloadMbps.HasValue || downloadMbps > stats.MaxDownloadMbps.Value)
                    stats.MaxDownloadMbps = downloadMbps;

                _downloadSumMbps += downloadMbps;
                _downloadSampleCount++;
                stats.AvgDownloadMbps = _downloadSampleCount > 0
                    ? _downloadSumMbps / _downloadSampleCount
                    : null;
            }

            if (TryGetFinite(result.UploadMbps, out var uploadMbps)
                && (!stats.MaxUploadMbps.HasValue || uploadMbps > stats.MaxUploadMbps.Value))
            {
                stats.MaxUploadMbps = uploadMbps;
            }

            if (!double.IsNaN(result.LatencyMs)
                && (!stats.MinLatencyMs.HasValue || result.LatencyMs < stats.MinLatencyMs.Value))
            {
                stats.MinLatencyMs = result.LatencyMs;
            }
        }
    }

    private void InvalidateStatisticsCacheLocked()
    {
        _statsCache = null;
        _downloadSumMbps = 0;
        _downloadSampleCount = 0;
    }

    private static bool TryGetFinite(double? value, out double result)
    {
        if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
        {
            result = value.Value;
            return true;
        }

        result = 0;
        return false;
    }

    private static SpeedTestStats CloneStats(SpeedTestStats stats) => new()
    {
        TotalCount = stats.TotalCount,
        MaxDownloadMbps = stats.MaxDownloadMbps,
        MaxUploadMbps = stats.MaxUploadMbps,
        AvgDownloadMbps = stats.AvgDownloadMbps,
        MinLatencyMs = stats.MinLatencyMs
    };

    public List<SpeedTestResult> GetAllRecords()
    {
        return GetRecords(1, 100000);
    }
}
