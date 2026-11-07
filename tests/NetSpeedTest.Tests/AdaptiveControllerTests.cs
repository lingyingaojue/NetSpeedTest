using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// B3 (F-07) 回归测试：自适应并发控制器在并发读写下必须保持不变式。
/// 旧实现中 _increaseAllowed 非 volatile、PulseLoop 直接读取 _target/_capacity、
/// _bestTarget 无锁读写，可能把目标并发数抬到容量之上或读到过期容量。
/// </summary>
public class AdaptiveControllerConcurrencyTests
{
    [Fact]
    public void Constructor_initialises_within_bounds()
    {
        var controller = new SpeedTestService.AdaptiveController(maxBase: 128, startThreads: 4, testTimeoutSec: 30, onActive: null);

        Assert.Equal(128, controller.MaxBase);
        Assert.InRange(controller.Target, 1, controller.Capacity);
        Assert.InRange(controller.Peak, 0, controller.Capacity);
    }

    [Fact]
    public void Constructor_clamps_start_threads_to_capacity()
    {
        var controller = new SpeedTestService.AdaptiveController(maxBase: 128, startThreads: 10_000, testTimeoutSec: 30, onActive: null);

        Assert.InRange(controller.Target, 1, controller.Capacity);
    }

    [Fact]
    public void SetTarget_clamps_to_valid_range()
    {
        var controller = new SpeedTestService.AdaptiveController(maxBase: 128, startThreads: 2, testTimeoutSec: 30, onActive: null);

        controller.SetTarget(int.MaxValue);
        Assert.InRange(controller.Target, 1, controller.Capacity);

        controller.SetTarget(int.MinValue);
        Assert.InRange(controller.Target, 1, controller.Capacity);
    }

    [Fact]
    public async Task Target_never_exceeds_capacity_under_concurrent_set_calls()
    {
        // PulseLoop 与 UI 线程会并发调用 SetTarget；夹取必须与容量读取原子化。
        var controller = new SpeedTestService.AdaptiveController(maxBase: 128, startThreads: 2, testTimeoutSec: 30, onActive: null);
        using var stop = new CancellationTokenSource();
        var violations = new List<string>();

        var writers = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            var rng = new Random(t);
            while (!stop.IsCancellationRequested)
            {
                controller.SetTarget(rng.Next(1, 2000));
            }
        })).ToList();

        var watcher = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var target = controller.Target;
                var capacity = controller.Capacity;
                if (target > capacity || target < 1)
                {
                    lock (violations) violations.Add($"target={target} capacity={capacity}");
                }
            }
        });

        await Task.Delay(300);
        stop.Cancel();
        await Task.WhenAll(writers.Append(watcher));

        Assert.Empty(violations);
    }

    [Fact]
    public async Task Observe_is_safe_under_concurrent_target_changes()
    {
        var controller = new SpeedTestService.AdaptiveController(maxBase: 256, startThreads: 4, testTimeoutSec: 30, onActive: null);
        using var stop = new CancellationTokenSource();
        var errors = new List<Exception>();

        var observers = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            var rng = new Random(t);
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    controller.Observe(rng.NextDouble() * 1000, false, 1.0);
                }
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
        })).ToList();

        var setter = Task.Run(() =>
        {
            var rng = new Random(99);
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    controller.SetTarget(rng.Next(1, 400));
                }
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
        });

        await Task.Delay(300);
        stop.Cancel();
        await Task.WhenAll(observers.Append(setter));

        Assert.Empty(errors);
        Assert.InRange(controller.Target, 1, controller.Capacity);
    }

    [Fact]
    public void Compensating_observe_keeps_target_in_range()
    {
        var controller = new SpeedTestService.AdaptiveController(maxBase: 128, startThreads: 8, testTimeoutSec: 30, onActive: null);

        controller.Observe(100.0, compensating: true, elapsed: 1.0);

        Assert.InRange(controller.Target, 1, controller.Capacity);
    }

    [Fact]
    public async Task Acquire_release_cycles_stay_within_capacity()
    {
        var controller = new SpeedTestService.AdaptiveController(maxBase: 32, startThreads: 4, testTimeoutSec: 30, onActive: null);

        for (var i = 0; i < 200; i++)
        {
            var slot = await controller.AcquireAsync(CancellationToken.None);
            Assert.InRange(slot, 0, controller.MaxBase - 1);
            controller.Release(slot);
        }

        Assert.InRange(controller.Peak, 0, controller.Capacity);
    }

    [Fact]
    public async Task SetTarget_above_current_wakes_waiters()
    {
        // 目标上调后，等待中的 worker 必须能拿到槽位，否则会出现“并发数上不去”的静默退化。
        var controller = new SpeedTestService.AdaptiveController(maxBase: 16, startThreads: 1, testTimeoutSec: 30, onActive: null);
        controller.SetTarget(1);

        var first = await controller.AcquireAsync(CancellationToken.None);

        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var secondTask = controller.AcquireAsync(waitCts.Token);
        controller.SetTarget(2);

        var second = await secondTask;
        Assert.NotEqual(first, second);

        controller.Release(first);
        controller.Release(second);
    }

    [Fact]
    public async Task AcquireAsync_honours_cancellation_while_waiting()
    {
        var controller = new SpeedTestService.AdaptiveController(maxBase: 8, startThreads: 1, testTimeoutSec: 30, onActive: null);
        controller.SetTarget(1);
        var slot = await controller.AcquireAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var waiting = controller.AcquireAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        controller.Release(slot);
    }

    [Fact]
    public void Release_without_acquire_does_not_underflow()
    {
        var controller = new SpeedTestService.AdaptiveController(maxBase: 8, startThreads: 1, testTimeoutSec: 30, onActive: null);

        controller.Release(0);
        controller.Release(0);

        Assert.InRange(controller.Current, 0, controller.Capacity);
    }
}

/// <summary>
/// B3 (F-22)：崩溃级异常日志必须落盘，不能因常规日志关闭而静默丢弃。
/// </summary>
public class FatalLoggingTests
{
    [Fact]
    public void Logger_writes_only_when_enabled()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        var before = File.Exists(path) ? File.ReadAllText(path) : "";
        var wasEnabled = Logger.Enabled;

        try
        {
            Logger.Enabled = false;
            Logger.Log("nst-test-should-not-appear");

            var afterDisabled = File.Exists(path) ? File.ReadAllText(path) : "";
            Assert.Equal(before, afterDisabled);

            // F-22 的处理程序先强制打开日志，再写入崩溃信息。
            Logger.Enabled = true;
            Logger.Log("nst-test-fatal-marker-" + Guid.NewGuid().ToString("N"));

            Assert.Contains("nst-test-fatal-marker-", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Logger.Enabled = wasEnabled;
        }
    }
}
