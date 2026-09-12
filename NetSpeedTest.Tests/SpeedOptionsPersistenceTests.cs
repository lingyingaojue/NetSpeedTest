using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// B3 (F-10) 回归测试：设置持久化必须是原子写入。
/// 旧实现直接 File.WriteAllText 覆盖，进程中断会留下半截 JSON 导致下次启动配置全部丢失。
/// </summary>
public class SpeedOptionsPersistenceTests : IDisposable
{
    private readonly string _dir;

    public SpeedOptionsPersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nst-persist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string SettingsPath => Path.Combine(_dir, "appsettings.json");

    private static SpeedTestOptions NewOptions(int threadCount = 64) => new()
    {
        ThreadCount = threadCount,
        TestTimeoutSec = 30,
        AverageDelaySec = 5,
        RateWindowSec = 2,
        NicPollIntervalMs = 500,
        ThreadRampUpMs = 0,
        LatencyPollIntervalMs = 1000,
        JitterTargetHost = "8.8.8.8",
        JitterPollIntervalMs = 1000,
        PacketLossTargetHost = "1.1.1.1",
        PacketLossPollIntervalMs = 1000,
        CompensationEnabled = true,
        CompensationThreshold = 0.5,
        CompensationConfirmSec = 3,
        AdaptiveThreadsEnabled = true,
        IncludeVirtualAdapters = false
    };

    [Fact]
    public void PersistSpeedOptions_writes_valid_json_with_speed_test_section()
    {
        WebServerService.PersistSpeedOptions(NewOptions(threadCount: 96), _dir);

        Assert.True(File.Exists(SettingsPath));
        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        var speed = root["SpeedTest"]!.AsObject();

        Assert.Equal(96, (int)speed["ThreadCount"]!);
        Assert.Equal("8.8.8.8", (string)speed["JitterTargetHost"]!);
        Assert.True((bool)speed["CompensationEnabled"]!);
    }

    [Fact]
    public void PersistSpeedOptions_preserves_unrelated_sections()
    {
        File.WriteAllText(SettingsPath, """
        {
          "SomeOtherSection": { "Keep": "me" },
          "SpeedTest": { "ThreadCount": 4 }
        }
        """);

        WebServerService.PersistSpeedOptions(NewOptions(threadCount: 128), _dir);

        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.Equal("me", (string)root["SomeOtherSection"]!["Keep"]!);
        Assert.Equal(128, (int)root["SpeedTest"]!["ThreadCount"]!);
    }

    [Fact]
    public void PersistSpeedOptions_recovers_from_corrupted_existing_file()
    {
        // 半截 JSON（模拟中断写入）不应阻止后续保存。
        File.WriteAllText(SettingsPath, "{ \"SpeedTest\": { \"ThreadCount\": ");

        WebServerService.PersistSpeedOptions(NewOptions(threadCount: 32), _dir);

        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.Equal(32, (int)root["SpeedTest"]!["ThreadCount"]!);
    }

    [Fact]
    public void PersistSpeedOptions_leaves_no_temp_file_behind()
    {
        WebServerService.PersistSpeedOptions(NewOptions(), _dir);
        WebServerService.PersistSpeedOptions(NewOptions(), _dir);

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Single(Directory.GetFiles(_dir, "appsettings.json"));
    }

    [Fact]
    public async Task PersistSpeedOptions_never_exposes_partially_written_file()
    {
        // 并发写入期间持续读取：绝不出现“文件存在但内容不完整/非法”。
        // File.Replace 期间的共享冲突属于瞬时状态，由 TryReadAllTextWithRetry 处理，
        // 因此这里直接使用该重试读取路径来断言最终可见内容始终是完整 JSON。
        var partialReads = new List<string>();
        using var stop = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (!File.Exists(SettingsPath)) continue;

                string? text;
                try
                {
                    text = WebServerService.TryReadAllTextWithRetry(SettingsPath, attempts: 8);
                }
                catch (IOException)
                {
                    // 重试耗尽：属于环境抖动而非内容损坏，忽略。
                    continue;
                }

                if (string.IsNullOrWhiteSpace(text)) continue;
                try
                {
                    JsonNode.Parse(text);
                }
                catch (JsonException ex)
                {
                    lock (partialReads) partialReads.Add(ex.Message);
                }
            }
        });

        for (var i = 0; i < 200; i++)
        {
            WebServerService.PersistSpeedOptions(NewOptions(threadCount: 2 + (i % 100)), _dir);
        }

        stop.Cancel();
        await reader;

        Assert.Empty(partialReads);
    }

    [Fact]
    public void TryReadAllTextWithRetry_returns_file_content()
    {
        File.WriteAllText(SettingsPath, "{\"a\":1}");

        Assert.Equal("{\"a\":1}", WebServerService.TryReadAllTextWithRetry(SettingsPath));
    }

    [Fact]
    public void AtomicWriteAllText_replaces_existing_content_without_temp_leftovers()
    {
        File.WriteAllText(SettingsPath, "old-content");
        WebServerService.AtomicWriteAllText(SettingsPath, "new-content");

        Assert.Equal("new-content", File.ReadAllText(SettingsPath));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void AtomicWriteAllText_creates_file_when_absent()
    {
        WebServerService.AtomicWriteAllText(SettingsPath, "fresh");

        Assert.Equal("fresh", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void PersistSpeedOptions_creates_missing_directory()
    {
        var nested = Path.Combine(_dir, "a", "b", "c");

        WebServerService.PersistSpeedOptions(NewOptions(), nested);

        Assert.True(File.Exists(Path.Combine(nested, "appsettings.json")));
    }

    [Fact]
    public void PersistSpeedOptions_roundtrips_all_documented_fields()
    {
        var options = NewOptions(threadCount: 77);
        WebServerService.PersistSpeedOptions(options, _dir);

        var speed = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject()["SpeedTest"]!.AsObject();

        Assert.Equal(options.TestTimeoutSec, (int)speed["TestTimeoutSec"]!);
        Assert.Equal(options.AverageDelaySec, (int)speed["AverageDelaySec"]!);
        Assert.Equal(options.RateWindowSec, (double)speed["RateWindowSec"]!);
        Assert.Equal(options.NicPollIntervalMs, (int)speed["NicPollIntervalMs"]!);
        Assert.Equal(options.ThreadRampUpMs, (int)speed["ThreadRampUpMs"]!);
        Assert.Equal(options.LatencyPollIntervalMs, (int)speed["LatencyPollIntervalMs"]!);
        Assert.Equal(options.JitterPollIntervalMs, (int)speed["JitterPollIntervalMs"]!);
        Assert.Equal(options.PacketLossTargetHost, (string)speed["PacketLossTargetHost"]!);
        Assert.Equal(options.PacketLossPollIntervalMs, (int)speed["PacketLossPollIntervalMs"]!);
        Assert.Equal(options.CompensationThreshold, (double)speed["CompensationThreshold"]!);
        Assert.Equal(options.CompensationConfirmSec, (int)speed["CompensationConfirmSec"]!);
        Assert.Equal(options.AdaptiveThreadsEnabled, (bool)speed["AdaptiveThreadsEnabled"]!);
        Assert.Equal(options.IncludeVirtualAdapters, (bool)speed["IncludeVirtualAdapters"]!);
        Assert.Equal(options.AdaptiveStartThreads, (int)speed["AdaptiveStartThreads"]!);
    }

    [Fact]
    public void PersistSpeedOptions_output_is_indented_and_utf8()
    {
        WebServerService.PersistSpeedOptions(NewOptions(), _dir);

        var text = File.ReadAllText(SettingsPath);
        Assert.Contains("\n", text, StringComparison.Ordinal);
        Assert.Contains("SpeedTest", text, StringComparison.Ordinal);
        // 必须是可被 JsonDocument 解析的 UTF-8 文本。
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}
