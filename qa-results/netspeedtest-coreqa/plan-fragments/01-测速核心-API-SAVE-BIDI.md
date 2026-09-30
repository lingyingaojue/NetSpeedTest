# 测速核心组 · 修复方案片段（设计稿，不改码）

> 范围：仅负责 BUG-API-SAVE-008 与 BUG-BIDI-002 两个最高优先缺陷。
> 所有行号均已对照 v1.4.3 真实源码重新核对；与 QA 记录有出入处在每节①标注。
> 本轮只做方案设计，不修改任何产品/测试源码、不构建、不跑测试、不 git commit。

---

## BUG-API-SAVE-008 经 Web API 发起的单网卡测速结果不写入历史、不更新最近结果（S3/P2/L3，Web·结果持久化）

### ① 代码级复核：确认后的根因 + 实际文件/方法/行号

文件：`NetSpeedTest/ViewModels/MainViewModel.cs`，方法 `FinishTest(SpeedTestResult result, bool showDialog = true)`，**实际行号 955–1023**。

根因：为避免 API 发起时模态结果窗阻塞 UI 线程而引入 `showDialog=false`，但把"结果持久化 + 最近结果更新"连同弹窗一起错误地塞进了同一个 `if (showDialog)` 块。API 发起时该块整体被跳过。

关键代码（实际）：

```csharp
// MainViewModel.cs:955
private void FinishTest(SpeedTestResult result, bool showDialog = true)
{
    // :959  —— QA 记录此行准确
    if (ConsumeApiInitiatedTest()) showDialog = false;
    ...
    // :975  （不在门控内，URL 明细网格对 API 路径其实是更新的）
    UrlTestDetails = new ObservableCollection<UrlTestDetail>(result.UrlDetails);

    // :976  —— 唯一应当只门控"弹窗"的位置，却把门控范围扩大到了持久化
    if (showDialog)
    {
        // :978  SaveResult（fire-and-forget，已带 try/catch 日志）
        _ = Task.Run(() => { try { _dataService.SaveResult(result); }
            catch (Exception ex) { Logger.Log($"SaveResult failed: {ex.Message}"); } });
        _lastMultiNicResults = null;
        _lastResult = result;                       // :980
        OnPropertyChanged(nameof(HasRecentResult)); // :981-985
        ...
        RecentRecords.Insert(0, result);           // :986
        while (RecentRecords.Count > 20) RecentRecords.RemoveAt(RecentRecords.Count - 1); // :987-988
    }   // :989  门控块结束

    // :990-1002  ok/fail/StatusText（不在门控内）
    ...
    // :1004  —— 这才是本意只想跳过的"结果弹窗"门控
    if (showDialog)
    {
        var dlg = new Views.TestResultWindow(...);  // :1006
        dlg.ShowDialog();                           // :1017 （QA 记"约1004行 ShowDialog"，实际 1004 是 if 行、ShowDialog 调用在 1017）
        TestCompletedNotify?.Invoke(...);           // :1019-1021
    }
}
```

与 QA 记录的差异：
- QA 称"约 1004 行的 `dlg.ShowDialog()`"：实际 **1004 行是第二个 `if (showDialog)` 起判行，`dlg.ShowDialog()` 调用本体在 1017 行**。语义一致，仅行号微调。
- QA 称"SaveResult 约 978、_lastResult/RecentRecords 约 979-988"：**与实际完全一致**（978 / 979–988）。
- API 标记字段位置：`_apiInitiatedTest` 在 **:47**，`MarkApiInitiatedTest()` **:52**，`ClearApiInitiatedTest()` **:57**，`ConsumeApiInitiatedTest()` **:62-67**（读并复位）。与 QA 记录 47/52/57/62 一致。
- 标记来源：`NetSpeedTest/Services/WebServerService.cs:1602` `vm.MarkApiInitiatedTest();`（在 `command.Execute(null)` 之前）；`:1643-1645` finally 中若未真正启动则 `ClearApiInitiatedTest()`。与 QA 一致。

对照 `FinishMultiNicTest`（`:1025-1106`）：其 `SaveResult(r)` 在 **:1072**，位于 `foreach (var r in results)`（:1059-1074）内、**不在任何 showDialog 门控内**；`_lastResult = aggregate` 在 :1078、`OnPropertyChanged` 在 :1079-1083，均无条件。弹窗门控为 :1102 `if (!apiInitiated) dlg.ShowDialog();`。**多网卡路径本就是"持久化无条件、弹窗才门控"的正确范式**，单网卡路径漏改。

真机现象解释：API 发起 → `showDialog=false` → :976 整块跳过 → `Task.Run(SaveResult)` 根本没被调度，因此既不落库、也不会出现 "SaveResult failed" 日志（任务都没启动，自然没有异常可记）。这与真机"history 97→97、recentResult 恒空、日志无 SaveResult failed"完全吻合。

### ② 精确修复方案（改动逻辑、关键代码片段示意，不落盘）

**核心思路：把"持久化 + 最近结果更新"与"结果弹窗 ShowDialog"彻底解耦。** 持久化与最近结果是数据正确性问题，与是否弹窗无关；只有模态窗（含其 Owner=Application.Current.MainWindow）才必须受 API 标记门控。

建议把 :978-988 整段抽成一个不触碰 WPF 弹窗的 internal 方法，在 `if(showDialog)` **之前无条件调用**；弹窗仍保留在第二个 `if(showDialog)` 内。调整后控制流伪代码：

```csharp
private void FinishTest(SpeedTestResult result, bool showDialog = true)
{
    if (ConsumeApiInitiatedTest()) showDialog = false;   // :959 不变，只决定弹窗

    // ... :960-974 延迟/抖动/丢包/平均速率/TestType/下载上传互斥置空 —— 全部不变 ...

    UrlTestDetails = new ObservableCollection<UrlTestDetail>(result.UrlDetails); // :975 不变

    // ★新增/抽出：持久化 + 最近结果，无条件执行（GUI 与 API 一视同仁）
    PersistResultAndUpdateRecent(result);

    // :990-1002 ok/fail/StatusText 计算 —— 不变
    var ok = result.UrlDetails.Count(d => !d.IsFailed);
    var fail = result.UrlDetails.Count(d => d.IsFailed);
    StatusText = _currentTestMode switch { ... };

    // ★唯一保留门控的部分：模态结果窗（会阻塞 UI 线程）
    if (showDialog)
    {
        var dlg = new Views.TestResultWindow(...){ Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
        TestCompletedNotify?.Invoke(...);   // 门控与否见⑥决策点
    }
}

// 抽出的 internal 方法（WPF 弹窗无关，便于单测）
internal void PersistResultAndUpdateRecent(SpeedTestResult result)
{
    // 落库仍走后台线程，避免 SQLite 写阻塞 UI；保留 try/catch 日志
    _ = Task.Run(() => { try { _dataService.SaveResult(result); }
        catch (Exception ex) { Logger.Log($"SaveResult failed: {ex.Message}"); } });
    _lastMultiNicResults = null;
    _lastResult = result;
    OnPropertyChanged(nameof(HasRecentResult));
    OnPropertyChanged(nameof(RecentDownloadMbps));
    OnPropertyChanged(nameof(RecentUploadMbps));
    OnPropertyChanged(nameof(RecentLatencyMs));
    OnPropertyChanged(nameof(RecentPacketLossDisplay));
    RecentRecords.Insert(0, result);
    while (RecentRecords.Count > 20) RecentRecords.RemoveAt(RecentRecords.Count - 1);
}
```

**异常处理（对应 QA(b)）**：
- 现有 :978 已经是 `try/catch + Logger.Log("SaveResult failed: ...")`，**异常处理本就存在**；真机无该日志不是"静默吞异常"，而是整块被门控跳过、Task 从未启动。解耦后若落库真失败，日志会出现 "SaveResult failed: ..."，符合预期。**不要**去掉 try/catch。
- fire-and-forget 仍用 `_ = Task.Run(...)`，不 await：落库失败不能反杀 UI 线程的状态展示。保留 discard `_` 防 CS4014 告警。

**时序 / 线程（对应 QA(b)）**：
- `FinishTest` 的调用点（:579/:638/:700/:732/:761，均在 `async Task StartXxxTest` 内 `await svc.RunXxxAsync(...)` 之后，**无 `ConfigureAwait(false)`**）在 WPF 命令上下文里会以 UI 线程恢复。因此 `_lastResult = result`、5 个 `OnPropertyChanged`、`RecentRecords.Insert` 都在 UI 线程，绑定安全，**无需额外 Dispatcher**。
- `SaveResult` 走后台 `Task.Run`，与 UI 线程写 `_lastResult` 不冲突（`result` 对象只读、不就地改写）。保持现状。
- `_lastResult` 赋值时机：与落库 Task 并行，但同步段立即赋值 → `/api/status` 的 `recentResult` 在测速结束瞬间即可读，**不依赖 DB flush 完成**。这正是 API 场景需要的。
- `RecentRecords` 去重/上限：现有逻辑是"每次 `Insert(0)` 新结果 + 裁到 20 条"，**不做内容去重**（每次测速一条新记录，本就该累加）。保持不变；多网卡路径 :1073-1075 同此范式。
- 注意一个时序副作用：`real-speedtest.ps1` 在每模式结束后 `Start-Sleep -Seconds 2/3` 才读 history。解耦后 SaveResult 是 fire-and-forget，存在"UI 已返回完成、SQLite 尚未写完"的毫秒级窗口。脚本现有 2–3s 等待通常足够；若偶发读到 total 未+1，建议脚本改为"轮询 total 直到稳定/超时"，而非再拉长固定等待。

### ③ 配套测试（测试类、用例方法名、断言点；基于现有测试工程真实可行性）

**现状摸底（重要，避免凭空设计）**：
- `_dataService` 是**具体类 `DataService`**（`MainViewModel.cs:27` `private readonly DataService _dataService;`），**不是 `IDataService`**；构造函数签名 `MainViewModel(ProfileService, DataService, NetworkInfoService, IServiceProvider, SpeedTestOptions, NetworkMonitorService)`（:334-336）。
- `DataService` 构造函数（`DataService.cs:24-29`）把 DB 路径**写死**为 `%LocalAppData%\NetSpeedTest\NetSpeedTest.db`，`SaveResult`（:146）是 `public void`（**非 virtual**、类非 sealed 但无接口）。
- 测试工程 `NetSpeedTest.Tests` **从未构造过 MainViewModel**（全仓 grep `new MainViewModel` 0 命中）；`AssemblyInfo.cs:4` 已配 `[assembly: InternalsVisibleTo("NetSpeedTest.Tests")]`，故 internal 成员（如 :52 `MarkApiInitiatedTest`、抽出的 `PersistResultAndUpdateRecent`）对测试可见。
- 结论：**不能直接 new MainViewModel 跑单测**（cors 里 LiveCharts/SkiaSharp Series + 网络事件订阅很重，且会写真实用户库）。必须先引入一个极轻的可替换缝。

**推荐可测性缝（随修复一起做，属小步重构）**：
把"落库动作"抽象为一个 internal 委托/字段，默认指向 `_dataService.SaveResult`，测试可替换：
```csharp
internal Action<SpeedTestResult>? PersistHook;   // 默认 => _dataService.SaveResult
```
`PersistResultAndUpdateRecent` 里改为 `_ = Task.Run(() => { try { (PersistHook ??= _dataService.SaveResult)(result); } catch ... })`。这样测试**无需构造完整 VM、不碰真 SQLite、不碰 WPF 弹窗**即可驱动持久化分支。

**新增测试类建议：`NetSpeedTest.Tests/ApiInitiatedPersistenceTests.cs`**（若作者认为必须用真实 VM，则至少把 `PersistResultAndUpdateRecent` 连同其依赖属性拆到一个 WPF-free 的 internal 小类 `ResultHistoryStore`，对它做单测；二选一，见⑥）。

用例与断言点：

1. `ApiInitiated_persistsResult_updatesRecent_doesNotShowDialog`
   - 准备：`vm.MarkApiInitiatedTest()`（internal，已存在）；替换 `PersistHook` 为计数 + 捕获 result 的委托。
   - 动作：对一条构造好的 `SpeedTestResult{ TestType="下载", UrlDetails=... }` 调用解耦后的结束路径（`PersistResultAndUpdateRecent`，或经 internal 化的 `FinishTest` 但在无弹窗分支）。
   - 断言：
     - `PersistHook` **恰好被调用 1 次**（= history 应 +1）；
     - 捕获到的 result 的 `Timestamp/TestType/DownloadMbps` 与传入一致；
     - `HasRecentResult == true`、`_lastResult`（经 internal 暴露属性或 `RecentDownloadMbps` 等 observable）非空；
     - `RecentRecords.Count >= 1` 且首条即该 result；
     - **未触发任何弹窗**（本用例根本不 new TestResultWindow；弹窗行为不在此断言，归手动/GUI）。

2. `GuiPath_stillPersists_andUpdatesRecent_noRegression`
   - 准备：**不**调 `MarkApiInitiatedTest()`（即 showDialog=true 路径）。
   - 断言：`PersistHook` 仍恰好 1 次；`RecentRecords` 仍 Insert 首条；`_lastResult` 仍赋值。
   - 说明：GUI 路径"仍落库、仍更新最近结果"不回归。GUI 路径"仍弹窗"需手动/GUI 验证（单测无法 new TestResultWindow，见⑤）。

3. `SaveResult_throws_isCaughtAndLogged_notPropagated`（若缝允许注入会抛异常的 PersistHook）
   - 准备：`PersistHook = _ => throw new InvalidOperationException("disk full")`。
   - 断言：调用后**不抛异常出界**；可经 internal 日志捕获点或观察 `_lastResult` 仍正常赋值（证明落库失败不影响最近结果更新与 UI）。

> 说明：纯单测覆盖"API 发起后落库+最近结果+不弹窗"中的前两项；"不弹窗"与"GUI 仍弹窗"涉及 `Views.TestResultWindow` + `Application.Current.MainWindow`，**无法在无头 xUnit 中断言**，归入手动/GUI 验证。

### ④ 回归影响面与可能副作用

- 影响范围：仅单网卡 `FinishTest`（下载/上传/双向三模式共用）。多网卡 `FinishMultiNicTest` 本就正确，不动。
- 正向：API 发起的下载/上传/双向结果现在都会落库、进 RecentRecords、出现在 `/api/status.recentResult` 与 `/api/history`。
- 副作用/风险：
  - API 路径现在会多一次 SQLite 写（此前为 0 次）。WAL + busy_timeout=5000（`DataService.cs:39,51`），且落库在后台线程，不阻塞测速；低风险。
  - `_lastResult`/RecentRecords 现在对 API 路径也更新 → `/api/status.recentResult` 从"恒空"变为"有值"，这是预期修复，**但任何依赖 recentResult 恒空的脚本/调用方需要知悉**（属预期行为变更）。
  - `TestCompletedNotify`（系统通知 toast）当前在第二个 `if(showDialog)` 内，API 路径仍不弹 toast；是否随解耦一并移出，见⑥。
  - 不要误把 `UrlTestDetails`(:975)、`StatusText`(:990-1002) 挪进门控——它们本就在门控外，保持现状。

### ⑤ 验证方式

- 可自动化单测：③ 中 3 条（落库被调一次 / 最近结果更新 / 异常被吞不扩散）——前提是先做 ② 的 internal 可测缝。
- 必须手动 / GUI / 跨设备：
  - GUI 手动点"开始测速"→ 仍弹结果窗、仍落库（回归确认弹窗未被误关）。
  - 经 `real-speedtest.ps1`（见 `qa-results/netspeedtest-coreqa/real-speedtest.ps1`）跑 download/upload/full 三模式，观察 `history total before` → `history total after` **每模式 +1**（修复前 97→97，修复后应 97→100），且 `realsummary.json` 中 `recentDl/recentUl` 非空。
  - 日志中不应再出现"API 测速后无新记录"；若 SQLite 异常，应能看到 `SaveResult failed: ...`。

### ⑥ 待作者决策点

1. **BUG-API-SAVE-008 是否从 P2 升 P1？** 它导致"远程白测、历史整体丢失"，且是无人值守 Web 场景的核心数据完整性问题；多网卡路径已正确、唯独单网卡漏改，修复成本极低（移动一个代码块）。建议升 P1，至少与 BUG-BIDI-002 同级优先处理。
2. **`TestCompletedNotify`（系统 toast 通知）是否随解耦一并移出 `if(showDialog)`？** 现状：单网卡 API 路径不弹 toast；多网卡路径（:1104）**无条件**弹 toast。两条路径行为不一致。建议统一为"弹窗才通知"或"都通知"，需作者定夺（无人值守服务器弹 toast 是否合理）。
3. **可测性缝的形态**：是 (A) 加一个 internal `Action<SpeedTestResult> PersistHook`（最小改动，推荐），还是 (B) 正式引入 `IDataService` 接口并改 DI（更彻底但波及启动装配）。本轮按 (A) 设计；若作者倾向 (B)，测试写法随之改为 mock `IDataService`。

**工作量**：S（改码约 0.5 人日，含抽方法；单测缝 + 3 条用例约 0.5 人日）。
**风险**：低（仅移动代码块位置 + 一个 internal 委托缝；不动测速引擎、不动 DB schema）。
**修复后预期结果**：`real-speedtest.ps1` 三模式跑完后 `history total after` = `before + 3`（每模式 +1），`/api/status.recentResult` 在每模式结束后非空（`realsummary.json` 的 recentDl/recentUl 有值），且 API 路径**不弹**模态窗（start 接口仍约 3.6s 内返回、running 正常翻转）。

---

## BUG-BIDI-002 双向测速聚合结果 UrlDetails 恒为空，"0 成功"（S3/P2/L3，测速·双向）

### ① 代码级复核：确认后的根因 + 实际文件/方法/行号

文件：`NetSpeedTest/Services/SpeedTestService.cs`。

根因：`RunFullTestAsync`（双向聚合，方法起 **:1622**）内部为下载、上传各建了一个 `UrlBalancer`（`:1680 dlBalancer`、`:1681 ulBalancer`），整个测速期间通过 `RunOneFullAsync`（:1682-1732）对两者 `ReportSuccess/ReportTimeout/ReportFailure`，数据齐全；但最终 `return new SpeedTestResult{... UrlDetails = new()}`（**:1794**）直接丢空列表，**既没用 `dlBalancer.BuildDetails()` 也没用 `ulBalancer.BuildDetails()`**。

两侧 inner 明细的真实来源（已逐行核对）：

- **下载侧** `RunMultiUrlTestAsync` 返回 :279-295，明细来自逐 worker 收集后按 URL 去重：
  ```csharp
  // :232-254  urlDetails.GroupBy(d => d.Url) 合并同 URL 多轮 → dedupedDetails
  // :294
  UrlDetails = dedupedDetails
  ```
- **上传侧** `RunUploadTestAsync` 返回 :1617，明细来自上传均衡器快照：
  ```csharp
  // :1617
  UrlDetails = urlBalancer.BuildDetails()
  ```
- **双向聚合侧** `RunFullTestAsync` 返回 :1794：
  ```csharp
  // :1794  ← BUG：两个 balancer 都在，却 new() 丢空
  ... BytesDownloaded = dlBytes_, BytesUploaded = ulBytes_, ... UrlDetails = new() };
  ```

`UrlTestDetail` 结构（`NetSpeedTest/Models/UrlTestDetail.cs:6-57`）字段：`Url`、`Host`、`AvgMbps`、`PeakMbps`、`BytesDownloaded`、`DurationSeconds`、`IsFailed`、`IsTrimmed`、`ErrorMessage`、`RateHistory`。**没有方向字段**。

`UrlBalancer.BuildDetails()`（:784-808）语义：对每个 URL 产出一条 `UrlTestDetail`；`IsFailed = h.Assigned && h.Success==0 && (h.Fail>0 || h.Timeouts>0)`，失败原因 `Timeout x{n}` / `Rejected/failed x{n}`；未被分配的 URL **不**标失败（与既有测试 `BuildDetails_does_not_mark_untouched_url_as_failed` 一致）。

`RunFullTestAsync` 如何拿到两侧 inner 结果：它**不**调用 `RunMultiUrlTestAsync`/`RunUploadTestAsync` 两个 inner 方法，而是自己内联跑双均衡器（:1680-1784）。因此"两侧 inner.UrlDetails"在本方法里对应的就是 **`dlBalancer.BuildDetails()` 与 `ulBalancer.BuildDetails()`** 两个现成调用，无需重构出 inner 返回值。

下游计数（`MainViewModel.cs`）：
```csharp
// :990
var ok   = result.UrlDetails.Count(d => !d.IsFailed);
// :991
var fail = result.UrlDetails.Count(d =>  d.IsFailed);
// :998-1000  双向分支
"双向" => fail > 0
    ? $"测速完成 · {ok} 成功 · {fail} 失败/超时"
    : $"测速完成 · {ok} 成功",
```
双向时 `UrlDetails` 为空 → ok=0、fail=0 → 走 else 分支显示 **"测速完成 · 0 成功"**。真机双向实传 4.1GB 却 0 成功，吻合。

与 QA 记录差异：行号（1794 / 279-294 / 1617 / 990 / 998-1000）**全部核对一致**，无漂移。

### ② 精确修复方案（改动逻辑、关键代码片段示意，不落盘）

**(a) 两侧来源**：下载侧 = `dlBalancer.BuildDetails()`（:1680，`useFastestAfterProbe:true`），上传侧 = `ulBalancer.BuildDetails()`（:1681，`useFastestAfterProbe:false`）。两者在 :1794 return 前都已 `await` 完毕（:1786-1788），可安全快照。

**(b) 合并方案**：

1. **先给 `UrlTestDetail` 补一个方向字段**（结构当前无方向字段，两方向同一 URL 无法区分）：
   ```csharp
   // UrlTestDetail.cs 新增（仅内存，不入库——UrlDetails 本就"仅内存，不入库"，见 SpeedTestResult.cs:101-103 注释，无需 DB 迁移）
   public string Direction { get; set; } = "下载";   // "下载" / "上传"
   ```
   默认值 `"下载"` 可让既有下载侧 `dedupedDetails`（:240-250，未显式设置）行为不变。

2. **抽出一个 internal 纯函数做合并**（便于单测，见③），在 :1794 使用：
   ```csharp
   // SpeedTestService 内 internal static
   internal static List<UrlTestDetail> MergeFullUrlDetails(
           IEnumerable<UrlTestDetail> dlDetails, IEnumerable<UrlTestDetail> ulDetails)
   {
       var merged = new List<UrlTestDetail>();
       foreach (var d in dlDetails) { d.Direction = "下载"; merged.Add(d); }
       foreach (var u in ulDetails) { u.Direction = "上传"; merged.Add(u); }
       return merged;
   }
   ```
   :1794 改为：
   ```csharp
   UrlDetails = MergeFullUrlDetails(dlBalancer.BuildDetails(), ulBalancer.BuildDetails())
   ```

3. **去重策略**：**跨方向不去重**。下载 URL 与上传 URL 通常本就是不同端点；即使字符串相同，也是"下载一次"与"上传一次"两个独立动作，应保留两条、靠 `Direction` 区分（否则界面无法区分同一 URL 的下载成败与上传成败）。方向内部：`BuildDetails()` 已对每个 URL 产出一条（均衡器内部已 `Distinct`，:682），无需再按 URL 合并。
4. **失败项标记**：`BuildDetails()` 已把 `IsFailed/ErrorMessage` 设好（:791, :800-805），Concat 原样保留；一侧失败不影响另一侧条目的成败标记。
5. **`IsTrimmed`**：`BuildDetails()` 不产生 trimmed 条目（上传/双向路径无裁剪概念），下载侧 `dedupedDetails` 的 trimmed 逻辑仅用于纯下载模式；双向合并无需特殊处理。

**(c) MainViewModel 成功计数与文案**：
- :990-991 的 `ok = Count(!IsFailed)`、`fail = Count(IsFailed)` **逻辑本身正确，无需改代码**；只要 :1794 返回的 `UrlDetails` 非空且两侧成败标记正确，ok 自然 = 下载成功数 + 上传成功数，fail = 下载失败数 + 上传失败数。
- 双向文案 :998-1000 修复后实测表现：假设下载 26 成功/4 失败、上传侧全成功，则显示"测速完成 · N 成功 · 4 失败/超时"（N=两侧成功之和）。**这正是期望**，与真机"4.1GB 传输"一致。
- 注意：双向文案**不带** `/_{_startUrlCount}_` 分母（对比 :996-997 下载/上传分支有分母）。因为双向下载 URL 数与上传 URL 数是两个集合，分母无法用单一 `_startUrlCount` 表达，保持现状即可；不要顺手改成下载/上传那样的分母形式，否则会误导。

### ③ 配套测试（测试类、用例名、断言点）

在既有 **`NetSpeedTest.Tests/UrlDetailReportingTests.cs`**（已能直接构造 `SpeedTestService.UrlBalancer`，见 :16-17 `NewBalancer`）中新增。**不直接单测 `RunFullTestAsync`**（它需要真实 HTTP/网卡/HttpClient，无头环境跑不了），而是单测上面那个 internal 纯函数 `MergeFullUrlDetails` —— 这是修复的全部新增逻辑，且可在无头环境编译运行。

用例与断言点：

1. `MergeFullUrlDetails_concatenates_download_and_upload_without_cross_dedup`
   - 构造：下载侧 balancer 报告 M 条成功（如 3 个 URL 全成功），上传侧 balancer 报告 N 条成功（如 2 个 URL 全成功）。
   - 动作：`MergeFullUrlDetails(dlBalancer.BuildDetails(), ulBalancer.BuildDetails())`。
   - 断言：`result.Count == M + N`；其中 `Direction=="下载"` 的有 M 条、`Direction=="上传"` 的有 N 条；每条 `IsFailed==false`。

2. `MergeFullUrlDetails_keeps_same_url_on_both_directions_as_two_entries`
   - 构造：下载侧与上传侧**用同一个 URL 字符串**（如 `"https://same.example/upload"` 同时出现在 dl/ul 列表），两侧都成功。
   - 断言：结果中有 **2 条**该 URL（一条 Direction=下载、一条 Direction=上传），不被跨方向合并成 1 条。

3. `MergeFullUrlDetails_preserves_failed_items_from_either_side`
   - 构造：下载侧 1 成功 + 1 `ReportFailure`；上传侧 1 `ReportTimeout`。
   - 断言：结果共 3 条；下载侧失败条 `IsFailed==true` 且 ErrorMessage 含 "Rejected"；上传侧失败条 `IsFailed==true` 且 ErrorMessage 含 "Timeout"；成功条 `IsFailed==false`。
   - 顺带校验 MainViewModel 口径：`Count(!IsFailed)` = 2、`Count(IsFailed)` = 2，对应"成功数/失败数"两侧都被计入。

> 构造方式：完全沿用 `UrlDetailReportingTests` 既有套路（`NewBalancer` → `GetUrlForWorker` → `ReportSuccess/Failure/Timeout` → `BuildDetails()`），零新增基础设施。若作者不愿在 `UrlTestDetail` 上加 `Direction` 字段，则合并函数需改为在合并时用"位置/来源列表"打标，断言相应改为校验来源归属——但加字段是更干净的做法，建议加。

### ④ 回归影响面与可能副作用

- 影响范围：**仅双向（full）模式**的 `SpeedTestResult.UrlDetails`。纯下载（:294 `dedupedDetails`）、纯上传（:1617 `BuildDetails()`）路径一行不动。
- `UrlTestDetail.Direction` 为新增属性、默认 `"下载"`，对既有 UI 绑定（`UrlTestDetails` 网格）向后兼容；若 UI 明细表格当前未显示方向列，双向模式下用户看不出某条是下载还是上传——**建议 UI 明细表格顺带加一列"方向"**（属可选增强，非本 bug 必需）。
- `SpeedTestResult.UrlDetails` 注释明确"仅内存，不入库"（`SpeedTestResult.cs:101-103`），故加字段、合并都**不涉及 SQLite schema 迁移**，不影响历史记录。
- MainViewModel :990 ok/fail 计数逻辑不变；修复后双向 ok 从 0 变为真实值，StatusText 文案随之变化（预期）。

### ⑤ 验证方式

- 可自动化单测：③ 中 3 条（纯函数，无头可跑）。
- 必须手动 / GUI / 真机：
  - GUI 发起一次双向测速 → 结果窗 URL 明细应同时出现下载节点与上传节点（而非空表），状态文案为"测速完成 · N 成功"且 N>0。
  - `real-speedtest.ps1` 跑 `full` 模式：修复前 `realcurve-full.json` 对应 status 为"测速完成·0 成功"；修复后应为"测速完成·<N> 成功[·<F> 失败/超时]"，N 与实际可达节点数相符。
  - 跨设备/多上联环境不在本轮覆盖范围（多网卡双向仍属人工项）。

### ⑥ 待作者决策点

1. **是否给 `UrlTestDetail` 加 `Direction` 字段**（推荐：加，默认"下载"）。若不加，则同 URL 两方向无法区分，合并方案退化为"按 URL 合并、丢失方向信息"，不推荐。
2. **UI 结果明细表格是否同步加"方向"列**（可选增强）。不加不影响计数正确性，只影响双向模式下的可读性。
3. `MergeFullUrlDetails` 放 `SpeedTestService` 内 internal static，还是独立成 internal 静态类——纯组织选择，不影响行为。

**工作量**：S（引擎改 1 行 return + 1 个 internal 合并函数 + 1 个模型字段，约 0.5 人日；3 条单测约 0.5 人日）。
**风险**：低（只补全一个原本就该填充的集合；纯下载/上传路径不动；无 DB schema 变更）。
**修复后预期结果**：`real-speedtest.ps1` 的 `full` 模式状态从"测速完成·0 成功"变为"测速完成·<M+N> 成功[·<F> 失败/超时]"，成功数与实际可达节点数一致；结果窗/远程返回中双向 URL 明细非空，下载/上传节点均可区分。

---

## 附：不作确定缺陷的条件式排查

### 1) 历史旧记录 BytesDownloaded / BytesUploaded / PeakMbps 三列为 0（条件式，非修复方案）

现状核对：
- 赋值链已就位：下载返回 `SpeedTestService.cs:283 PeakMbps=peakAggMbps`、`:290 BytesDownloaded=totalBytes`、`:291 BytesUploaded=0`；上传返回 `:1617`（PeakMbps/BytesUploaded 均赋）；双向返回 `:1794`（`dlBytes_/ulBytes_/nicState.PeakRate` 均赋）。
- 落库：`DataService.cs:166-170` SaveResult 写入 `@bd/@bu/@pk`；读回 `:223-227`。
- 建表 `DataService.cs:69-73` 中 `BytesDownloaded/BytesUploaded INTEGER NOT NULL`、`PeakMbps ... DEFAULT 0`；`MigrateTable`（:110-141）只补 `PeakMbps/WanLatencyMs/AverageTotalMbps/TotalBytes/BatchId/ErrorMessage/TestType`（均带 DEFAULT 0），**不含 BytesDownloaded/BytesUploaded**（这两列在 CREATE TABLE 里就是固有列）。
- 倾向判断：现存"三列为 0"的记录大概率是**旧版本写入的数据**（彼时引擎尚未填充这三列，或迁移 DEFAULT 0 补出来的空值），而非当前 v1.4.3 仍在产生 0。

**如何用一次 GUI 手动落库验证（判定标准）**：
1. 先记录基线：启动 Release exe → GUI 手动跑一次下载测速（不要走 API，因 BUG-API-SAVE-008 期间 API 不落库）。
2. 测速完成后，打开历史页，看**最新一条**记录的 BytesDownloaded/BytesUploaded/PeakMbps 三列。
3. 或直接查库：`%LocalAppData%\NetSpeedTest\NetSpeedTest.db`，执行 `SELECT Timestamp, TestType, BytesDownloaded, BytesUploaded, PeakMbps FROM SpeedTestRecords ORDER BY Id DESC LIMIT 3;`。
4. 判定标准：
   - 若**最新一条**（刚手动落库的）三列**非 0**（BytesDownloaded≈本次下载字节、PeakMbps>0），而**旧记录**为 0 → 证实是历史旧数据问题，**当前代码无 bug**，无需修复。
   - 若**刚落库的新记录**三列**仍为 0** → 当前代码确有问题，按下条排查。

**若验证确有问题，从哪条赋值链排查、如何修**：
- 自上而下：①`SpeedTestService` 返回对象是否真带值（断点/日志看 :290/:1617/:1794 的 `totalBytes/ulBytes/dlBytes_/PeakRate`）；②`MainViewModel.FinishTest` 是否用了这个 result（注意 :949 取消路径的 result 是手工 `UrlDetails=new()` 且可能没填字节——确认正常完成路径 :579 传入的是引擎返回对象）；③`DataService.SaveResult` 的 `@bd/@bu/@pk` 参数（:166-170）与 INSERT 列是否对齐；④读回 `GetRecords`（:223-227）列序号是否漂移（加列后 reader.GetOrdinal 硬编码序号易错位，建议核对是否用 GetOrdinal 而非魔法数字 9/10）。
- 按此链定位是"引擎没给值 / VM 换了对象 / DAL 参数错位 / 读回列错位"中的哪一环，再做最小修复。

### 2) 下载 30 个预设 URL 仅 4 成功

属节点运营/可达性观察，**非代码缺陷**。一句话建议：更新/清理内置预设节点列表（替换失效节点），不写任何代码修复。
