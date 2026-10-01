using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NetSpeedTest.Models;
using NetSpeedTest.ViewModels;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// BUG-API-SAVE-008 回归：经 Web API 发起的测速（不弹结果窗）也必须落库并刷新“最近结果/历史”。
/// MainViewModel 构造函数依赖 WPF Application/Dispatcher、真实网卡枚举与 SQLite，无头 xUnit 无法完整构造；
/// 这里用 GetUninitializedObject 取得未运行构造函数的裸实例，只注入被测方法 PersistResultAndUpdateRecent
/// 实际触碰的 _recentRecords 字段与 internal PersistHook 落库缝，从而在不写真实库、不弹窗的前提下验证
/// “持久化不受 showDialog/API 标记门控”。模态结果窗（ShowDialog）本身只能由 GUI/真机回归覆盖。
/// </summary>
public class ApiInitiatedPersistenceTests
{
    private static MainViewModel NewBareViewModel()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var field = typeof(MainViewModel).GetField("_recentRecords", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("未找到 _recentRecords 字段");
        field.SetValue(vm, new ObservableCollection<SpeedTestResult>());
        return vm;
    }

    private static SpeedTestResult SampleResult() => new()
    {
        TestType = "下载",
        DownloadMbps = 123.45
    };

    [Fact]
    public async Task ApiInitiated_persistsResult_and_updates_recent_even_without_dialog()
    {
        var vm = NewBareViewModel();
        vm.MarkApiInitiatedTest();                       // 模拟 Web API 发起（FinishTest 据此令 showDialog=false）
        var result = SampleResult();
        var tcs = new TaskCompletionSource<SpeedTestResult>();
        var calls = 0;
        vm.PersistHook = r => { calls++; tcs.TrySetResult(r); };

        vm.PersistResultAndUpdateRecent(result);         // 持久化已与弹窗解耦，应无条件执行

        var persisted = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(result, persisted);                 // 恰好落库一次，落的就是本次结果
        Assert.Equal(1, calls);
        Assert.True(vm.HasRecentResult);                // 最近结果同步更新，不等 DB flush
        Assert.NotNull(vm.RecentDownloadMbps);
        Assert.Equal(123.45, vm.RecentDownloadMbps!.Value, 6);
        Assert.Single(vm.RecentRecords);
        Assert.Same(result, vm.RecentRecords[0]);
    }

    [Fact]
    public async Task GuiPath_still_persists_and_updates_recent_no_regression()
    {
        var vm = NewBareViewModel();                    // 不置 API 标记 => GUI 路径（showDialog=true）
        var result = SampleResult();
        var tcs = new TaskCompletionSource<SpeedTestResult>();
        var calls = 0;
        vm.PersistHook = r => { calls++; tcs.TrySetResult(r); };

        vm.PersistResultAndUpdateRecent(result);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);                         // GUI 路径仍落库，不回归
        Assert.True(vm.HasRecentResult);
        Assert.Single(vm.RecentRecords);
        Assert.Same(result, vm.RecentRecords[0]);
    }

    [Fact]
    public async Task Persist_throws_is_caught_and_does_not_break_recent_update()
    {
        var vm = NewBareViewModel();
        var result = SampleResult();
        var tcs = new TaskCompletionSource<SpeedTestResult>();
        vm.PersistHook = r =>
        {
            try { throw new InvalidOperationException("disk full"); }
            finally { tcs.TrySetResult(r); }            // 证明落库委托确实在后台被调到
        };

        vm.PersistResultAndUpdateRecent(result);        // 落库异常由后台 try/catch 吞掉并记日志，不得外溢

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);                          // 留出后台 catch 记录日志时间
        Assert.True(vm.HasRecentResult);                // 落库失败不影响“最近结果/历史”UI 状态
        Assert.Single(vm.RecentRecords);
        Assert.Same(result, vm.RecentRecords[0]);
    }
}
