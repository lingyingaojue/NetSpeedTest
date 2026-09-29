using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// QA 补充：SQLite 历史记录的建表、写入、分页、计数、删除与清空。
/// 使用临时数据库（反射替换连接串），不污染用户真实 %LOCALAPPDATA% 数据库。
/// 对应风险 RM-DATA-013。
/// </summary>
public sealed class DataServicePaginationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DataService _data;

    public DataServicePaginationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nst-qa-{Guid.NewGuid():N}.db");
        _data = new DataService();

        var field = typeof(DataService).GetField("_connectionString", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(_data, $"Data Source={_dbPath};Default Timeout=5");

        _data.Initialize();
        _data.ClearAllRecords();
    }

    private SpeedTestResult MakeResult(int index) => new SpeedTestResult
    {
        Timestamp = new DateTime(2026, 9, 1, 12, 0, 0).AddSeconds(index),
        DownloadMbps = 100 + index,
        UploadMbps = 20 + index,
        LatencyMs = 5 + index,
        JitterMs = 1.5,
        PacketLoss = 0,
        NodeName = $"QA节点{index}",
        NetworkAdapterName = "QA-ETH",
        BytesDownloaded = 1000L * (index + 1),
        BytesUploaded = 500L * (index + 1),
        DurationSeconds = 30,
        ThreadCount = 128,
        PeakMbps = 120 + index,
        TestType = "下载"
    };

    [Fact]
    public void SaveAndPagination_ReturnsCorrectPagesAndTotal()
    {
        for (var i = 0; i < 25; i++)
            _data.SaveResult(MakeResult(i));

        Assert.Equal(25, _data.GetRecordCount());

        var page1 = _data.GetRecords(page: 1, pageSize: 20);
        var page2 = _data.GetRecords(page: 2, pageSize: 20);
        Assert.Equal(20, page1.Count);
        Assert.Equal(5, page2.Count);

        // 按时间倒序：第一页第一条是最新写入的记录。
        Assert.Equal("QA节点24", page1[0].NodeName);
        Assert.Equal(24 + 100, page1[0].DownloadMbps);
        // 页间不重复。
        Assert.Empty(page1.Select(r => r.Id).Intersect(page2.Select(r => r.Id)));
    }

    [Fact]
    public void PageSizeBoundary_LastPagePartial_AndBeyondLastEmpty()
    {
        for (var i = 0; i < 25; i++)
            _data.SaveResult(MakeResult(i));

        Assert.Equal(10, _data.GetRecords(1, 10).Count);
        Assert.Equal(5, _data.GetRecords(3, 10).Count);
        Assert.Empty(_data.GetRecords(4, 10));
    }

    [Fact]
    public void DeleteRecord_ReducesCount_AndRemovesRow()
    {
        for (var i = 0; i < 5; i++)
            _data.SaveResult(MakeResult(i));
        var first = _data.GetRecords(1, 20).First();

        _data.DeleteRecord(first.Id);

        Assert.Equal(4, _data.GetRecordCount());
        Assert.DoesNotContain(_data.GetAllRecords(), r => r.Id == first.Id);
    }

    [Fact]
    public void ClearAllRecords_ResetsCountToZero()
    {
        for (var i = 0; i < 7; i++)
            _data.SaveResult(MakeResult(i));
        Assert.Equal(7, _data.GetRecordCount());

        _data.ClearAllRecords();

        Assert.Equal(0, _data.GetRecordCount());
        Assert.Empty(_data.GetRecords(1, 50));
        Assert.Empty(_data.GetAllRecords());
    }

    [Fact]
    public void RoundTrip_PreservesKeyFields()
    {
        _data.SaveResult(MakeResult(1));
        var loaded = _data.GetRecords(1, 1).Single();

        Assert.Equal("QA节点1", loaded.NodeName);
        Assert.Equal("QA-ETH", loaded.NetworkAdapterName);
        Assert.Equal("下载", loaded.TestType);
        Assert.Equal(101, loaded.DownloadMbps);
        Assert.Equal(121, loaded.PeakMbps);
        Assert.Equal(2000, loaded.BytesDownloaded);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + suffix;
            try { if (File.Exists(p)) File.Delete(p); } catch { /* 临时文件清理尽力而为 */ }
        }
    }
}
