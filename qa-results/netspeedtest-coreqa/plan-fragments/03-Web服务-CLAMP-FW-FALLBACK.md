# 03 · Web 服务三缺陷修复方案片段（CLAMP / 防火墙 / 兜底页）

> 范围：仅设计、不改码。本文所有行号均为静态读码核对（v1.4.3，net8.0-windows）。
> 基线测试：243 方法 = 239 通过 / 0 失败 / 4 跳过。本文不新增任何产品/测试源码，代码片段仅作示意，不落盘。

---

## BUG-CLAMP-003 Web 设置 API 参数钳制范围与 UI 不一致（S3/P2/L2，Web·设置）

### ① 代码级复核：根因 + 实际文件/方法/行号

根因：**参数合法范围被三处独立硬编码，调整时未同步**。后端 Web API、WPF 设置页 ViewModel、XAML 滑块各写各的边界常量。

- 后端设置应用逻辑 `ApplySettingsCore`：`NetSpeedTest\Services\WebServerService.cs:1428-1445`
  - `:1431` `options.TestTimeoutSec = Math.Clamp(testTimeoutSec.GetInt32(), 5, 600);`
  - `:1435` `options.ThreadRampUpMs = Math.Clamp(threadRampUpMs.GetInt32(), 0, 5000);`
- 设置页 ViewModel 保存路径（**QA 记录未提到的第三处**）：`NetSpeedTest\ViewModels\SettingsViewModel.cs`
  - `:202` `TestTimeoutSec = Math.Clamp(TestTimeoutSec, 5, 600);`
  - `:206` `ThreadRampUpMs = Math.Clamp(ThreadRampUpMs, 0, 5000);`
  - 加载路径 `:158` `TestTimeoutSec = options.TestTimeoutSec;`（**不钳制**，直接把已落盘的越界值喂给滑块）。
- UI 滑块：`NetSpeedTest\Views\SettingsWindow.xaml`
  - `:96` `<Slider Minimum="10" Maximum="600" Value="{Binding TestTimeoutSec}"`
  - `:107` `<Slider Minimum="0" Maximum="2000" Value="{Binding ThreadRampUpMs}"`
- 默认值：`NetSpeedTest\Models\SpeedTestOptions.cs:6` `TestTimeoutSec=60`、`:7` `AverageDelaySec=10`、`:10` `ThreadRampUpMs=50`。
- 设置 POST 端点 `HandleSettingsPostAsync`：`WebServerService.cs:1344-1384`，`:1358` 调 `ApplySettingsCore`，`:1375` **无论是否越界一律返回 `200 {ok:true}`**——现状是「静默夹取后落盘」，从不返回 400。

**与 QA 记录的差异（重要）**：QA 描述为「API 钳 5-600 vs UI 滑块 10-600」。实际核对后，**后端 `ApplySettingsCore` 与 ViewModel `SettingsViewModel.cs:202/206` 用的是同一组宽松边界（5-600 / 0-5000）**，不一致的对立面是 XAML 滑块（10-600 / 0-2000）。也就是说这不是「API 宽、UI 窄」单点偏差，而是 **XAML 表现层与数据层（ViewModel+后端）两套边界**。统一常量时必须同时覆盖这三处，不能只改后端。

**全量同类参数对照（逐一核对 XAML 滑块 ↔ ViewModel Clamp ↔ 后端 Clamp）**：

| 参数 | XAML 滑块 (SettingsWindow.xaml) | ViewModel Clamp (SettingsViewModel.cs) | 后端 Clamp (WebServerService.cs) | 是否一致 |
|---|---|---|---|---|
| threadCount | 2–1024 (:78) | 2–1024 (:201) | 2–1024 (:1430) | ✅ |
| **testTimeoutSec** | **10–600 (:96)** | **5–600 (:202)** | **5–600 (:1431)** | ❌ 下限 5 vs 10 |
| averageDelaySec | 1–30 (:118) | 1–30 (:203) | 1–30 (:1432) | ✅ |
| rateWindowSec | 0.5–10 (:129) | 0.5–10 (:204) | 0.5–10 (:1433) | ✅ |
| nicPollIntervalMs | 200–5000 (:155) | 200–5000 (:205) | 200–5000 (:1434) | ✅ |
| **threadRampUpMs** | **0–2000 (:107)** | **0–5000 (:206)** | **0–5000 (:1435)** | ❌ 上限 2000 vs 5000 |
| latencyPollIntervalMs | 500–10000 (:166) | 500–10000 (:207) | 500–10000 (:1436) | ✅ |
| jitterPollIntervalMs | 500–5000 (:177) | 500–5000 (:208) | 500–5000 (:1438) | ✅ |
| packetLossPollIntervalMs | 500–5000 (:210) | 500–5000 (:209) | 500–5000 (:1440) | ✅ |
| compensationThreshold | 0.3–0.8 (:237) | 0.3–0.8 (:211) | 0.3–0.8 (:1442) | ✅ |
| compensationConfirmSec | 1–10 (:250) | 1–10 (:212) | 1–10 (:1443) | ✅ |

结论：**仅 testTimeoutSec、threadRampUpMs 两项存在三处不一致**，其余 9 项三处一致。但风险面是结构性的——只要边界仍分散硬编码，下次调任何参数都可能再次失配。

额外逻辑缺陷（QA severity_basis 提到）：`testTimeoutSec=5` 小于 `averageDelaySec` 默认 10s，测速可能在平均结果窗口开启前就结束，产出空平均速率。当前 `ApplySettingsCore` 没有「超时 ≥ 平均窗口」的交叉校验。

### ② 精确修复方案

**1）建立单一事实源（推荐落点）**

在 `NetSpeedTest\Models\SpeedTestOptions.cs` 同目录新增 `SpeedOptionLimits.cs`（或直接作为 `SpeedTestOptions` 的 `internal static partial` 常量类），集中声明全部边界：

```csharp
// 示意，不落盘
internal static class SpeedOptionLimits
{
    public const int ThreadCountMin = 2, ThreadCountMax = 1024;
    public const int TestTimeoutSecMin = 10, TestTimeoutSecMax = 600;     // 以 UI/README 为准，下限 10
    public const int AverageDelaySecMin = 1, AverageDelaySecMax = 30;
    public const double RateWindowSecMin = 0.5, RateWindowSecMax = 10;
    public const int ThreadRampUpMsMin = 0, ThreadRampUpMsMax = 2000;    // 以 UI/README 为准，上限 2000
    // …其余 9 项同样集中
}
```

引用方式：
- 后端 `ApplySettingsCore`（`WebServerService.cs:1430-1445`）把每处字面量替换为 `SpeedOptionLimits.*`。
- ViewModel `SettingsViewModel.cs:201-212` 同理替换。
- XAML（`SettingsWindow.xaml:78/96/107/...`）的 `Minimum/Maximum` **无法直接绑定 C# 常量**。两种处理：
  - 方案 A（推荐）：XAML 仍写字面量，但新增一条「XAML ↔ 常量一致性」单元测试（见③），把漂移变成 CI 红灯；
  - 方案 B：在 ViewModel 暴露 `MinTestTimeoutSec/MaxTestTimeoutSec` 等 `public double` 属性，XAML 改为 `Minimum="{Binding MinTestTimeoutSec}"`，彻底消除 XAML 字面量。改动面更大但根治。

**2）交叉校验（超时 < 平均窗口）**

在 `ApplySettingsCore` 末尾（钳制完成后、落盘前）增加一条语义校验：
```csharp
// 示意
if (options.TestTimeoutSec < options.AverageDelaySec)
    // 触发决策点：见 ⑥ —— 是 clamp 拉高 testTimeoutSec，还是 400 拒绝？
```

**3）超界响应策略（待作者拍板，见⑥）**

现状是静默 clamp。两种改法：
- **方案甲：保持静默夹取（clamp）**——后端把越界值夹到边界后照常 200，与现有行为兼容，不破坏 smoke `settings POST round-trips`（`tools\smoke-test.ps1:277-284`）。
- **方案乙：超界返回 400**——`ApplySettingsCore` 改为收集「哪些字段被显式传入且越界」，`HandleSettingsPostAsync:1375` 在落盘前判断，若有越界字段则 `WriteJsonAsync(ctx, 400, new { error=..., rejected=... })` 且**不落盘**。客户端能立刻知道自己写错了。

### ③ 配套测试

参考 `NetSpeedTest.Tests\WebSecurityTests.cs` 范式——它全部是对 `internal static` 纯函数的直接单测（`IsTokenValid`/`IsStateChangingMethod`/`IsLoopbackRequest`），不起 HttpListener。建议把钳制/校验抽成 internal static 纯函数后照此构造。

新增测试类建议：`NetSpeedTest.Tests\SettingsClampTests.cs`

- `[Theory] [InlineData] TestTimeoutSec_is_clamped_to_unified_bounds`
  - 界内：`10`、`600`、`45` → 原样返回
  - 下界-1：`9`、`5` → 应为 `10`（统一后下限）
  - 上界+1：`601` → 应为 `600`
  - 负数：`-10` → 应为 `10`
  - 缺失：未传该字段 → 不改动原值
  - 非数字：字符串 `"abc"` → 走 `HandleSettingsPostAsync` 的 `catch(JsonException/InvalidDataException)` → 400
- `[Theory] [InlineData] ThreadRampUpMs_is_clamped_to_unified_bounds`
  - 界内 `0`、`2000`；上界+1 `2001`、`5000` → 应为 `2000`；负数 `-1` → `0`
- `[Fact] XAML_slider_bounds_match_SpeedOptionLimits`
  - 读取 `SettingsWindow.xaml` 源文件（与 `ReadmeDocumentationTests.cs` 同样的文件读取方式），正则抽出 `TestTimeoutSec`/`ThreadRampUpMs` 所在 Slider 的 `Minimum/Maximum`，与 `SpeedOptionLimits` 断言相等。防止 XAML 再次漂移。
- `[Fact] TestTimeoutSec_below_averageDelaySec_is_rejected_or_clamped`
  - 按作者拍板的策略：若 400 则断言 `ApplySettingsCore` 返回「需拒绝」信号；若 clamp 则断言 testTimeoutSec 被抬到 ≥ averageDelaySec。

### ④ 回归影响面与可能副作用

- **旧配置迁移**：用户可能已通过 API 把 `testTimeoutSec=5`、`threadRampUpMs=3000` 落盘到 `%LOCALAPPDATA%\NetSpeedTest\appsettings.json`。统一下限改为 10 / 上限改为 2000 后，下次启动加载该配置时：
  - ViewModel 加载路径 `SettingsViewModel.cs:158` **当前不钳制**，会把 5 直接显示到滑块（thumb 停在 Minimum=10 端点，但文本框仍显示 5）。建议加载时也过一遍统一 clamp，否则 UI 与落盘值短暂不一致。
  - 不建议做配置文件自动改写迁移；启动时在内存钳制即可，下次保存自然收敛。
- 改 400 拒绝后，任何已上线的远程自动化脚本若曾发送越界值会开始收到 400——属于预期内的契约收紧。
- `threadCount` 并发 smoke（`smoke-test.ps1:294-302`）断言最终值在 2-1024，统一常量后该断言继续通过。

### ⑤ 验证方式

- **自动化**：上述 `SettingsClampTests` 全部可在 xUnit 静态跑通（不起服务、不需要管理员）。
- **自动化（端到端）**：`tools\smoke-test.ps1` 的 `settings POST round-trips`（:277）继续应转绿；建议补一条 `POST /api/settings {"testTimeoutSec":5}` 的断言（按策略期望 200-clamped 或 400）。
- 无需第二设备/真机管理员（本缺陷纯逻辑）。

### ⑥ 待作者决策点

- **【决策 1】超界请求：返回 HTTP 400 拒绝 vs 静默夹取（clamp）**。
  - 400 利弊：契约清晰、远程客户端立即感知错误；但破坏向后兼容，且需改 `HandleSettingsPostAsync` 返回码分支。
  - clamp 利弊：兼容现状、实现最小；但客户端不知道自己发了被丢弃的值，静默写入 UI 无法表达的边界正是本缺陷的根源。
  - **推荐**：对「界内语义可接受、仅越界」的值采用 clamp（保持 200），但对「交叉语义非法」（testTimeoutSec < averageDelaySec）采用 400——因为后者夹取无法唯一确定该抬高哪一侧。最终以作者拍板为准。
- **【决策 2】统一下限到底取哪侧**：testTimeoutSec 下限取 10（UI/README）还是保留 5？threadRampUpMs 上限取 2000（UI）还是 5000？本片段默认「以 UI/README 为准」（10–600 / 0–2000），但需作者确认 5000 的 ramp-up 是否本就想留给高级用户。

**工作量**：M（约 4–6 小时，含常量类、三处替换、XAML 一致性测试、交叉校验）。
**风险**：低。集中常量 + 纯函数单测，不触碰网络/防火墙路径。
**修复后预期结果**：`smoke-test.ps1` 中 `settings POST round-trips`、`8 concurrent settings POSTs` 保持绿；新增「越界值三处范围一致」断言转绿（当前 TC-WEB-061 为 ❌，应转 ✅）。

---

## BUG-FW-006 防火墙入站规则 add 为死代码，只删不建，就绪状态恒 true（S3/P2/L2，Web·防火墙）

### ① 代码级复核：根因 + 实际文件/方法/行号

根因：`EnsureFirewallRule` 内层 try 执行 `delete` 后**无条件 `return`**，导致 `add` 语句不可达；同时调用方在 `EnsureFirewallRule` 返回后无脑把 `_firewallReady=true`。

- `EnsureFirewallRule`：`NetSpeedTest\Services\WebServerService.cs:513-524`
  ```
  515: const string ruleName = "NetSpeedTest Web Server";
  516: try {
  517:   try { RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\""); } catch { }
  519:   return;                       // ← 无条件返回，add 不可达
  521: } catch { }
  523: RunNetsh($"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any");
  ```
  与 QA 记录行号一致（QA 说 519 return、523 add）。
- 调用方 `StartLanListeners`：`WebServerService.cs:451-461`
  - `:453` `EnsureFirewallRule(port);` → `:454` `_firewallReady = true;`
  - 因 `EnsureFirewallRule` 内层把 delete 的异常吞在 `catch{}`（:518）、且随后 `return` 从不抛异常，**`:454` 恒执行**，`:458` 的 `_firewallReady=false` 分支永不触发。
- `RunNetsh`：`WebServerService.cs:526-543`，非管理员执行 `advfirewall firewall add/delete` 时 netsh 退出码非 0 → `:541-542` 抛异常。但该异常在 `:518` 被 `catch{}` 吞掉。
- `FirewallReady` 属性：`:201`；唯一消费者是 `GetServerInfo` `:983` 输出到 `/api/server` 的 `firewallReady` 字段——**无任何 UI/前端据此做拦截**，QA 记录「FirewallReady 无 UI/前端消费者」属实。
- `EnsureUrlAcl`：`:507-511`（`http add urlacl`），通配符监听 `http://+:{port}/` 在 `:464`；非管理员下通配符监听失败会回退到逐网卡监听（`:476-485`）。
- **`IsFirewallRuleNeeded` 不存在**：代码里没有名为 `IsFirewallRuleNeeded` 的方法（Grep 全文件 0 命中）。QA 用「IsFirewallRuleNeeded 恒返回 false」描述就绪检查逻辑，实际对应物就是 `_firewallReady` 恒 true（方向相反：QA 说恒 false，实际代码恒 true——但「假通过」结论一致，见下）。

**与 QA 记录的差异**：
1. QA 标题/描述称「IsFirewallRuleNeeded 恒返回 false」。实际代码**没有这个方法**，且 `_firewallReady` 是**恒 true**（不是 false）。方向相反：不是「恒 false 被当就绪」，而是「无论规则是否真的建成都置 true」。「就绪检查假通过」这一结论成立。
2. delete 在非管理员下同样会失败，但被 `:518 catch{}` 吞掉，所以「每次启动删除同名规则却不重建」只在**恰好有管理员权限**时才真正发生 delete；普通用户下 delete 静默失败、add 也不执行，实际系统规则表保持原样。

### ② 精确修复方案

**1）消除死代码，让就绪状态反映真实探测结果**

把 `EnsureFirewallRule` 从「盲目 delete+add」改为「先探测、再按需建」，并通过返回值告知调用方真实状态：

```csharp
// 示意，不落盘。返回值：规则确实存在且放行了本端口
private bool EnsureFirewallRule(int port)
{
    const string ruleName = "NetSpeedTest Web Server";
    // 1) 探测：netsh advfirewall firewall show rule name="..." 解析输出，或
    //    直接尝试 add（已存在则 netsh 会报已存在/覆盖），按退出码判定。
    // 2) 仅当探测显示规则不存在、且本进程有足够权限时才 add。
    // 3) 无权限时返回 false，不要吞异常后假装成功。
}
```

调用方改为：
```csharp
_firewallReady = EnsureFirewallRule(port);   // WebServerService.cs:453-454
// 若 false：_lanError 给明确文案（需手动放行/需管理员），而非吞掉
```

**2）真实探测的三种可选实现**（按可测试性排序）
- **(a) 解析 `netsh advfirewall firewall show rule name="..."` 输出**：把「输出文本 → 规则是否存在/是否放行 localport」的解析抽成 `internal static bool ParseFirewallRulePresent(string netshOutput)` 纯函数，可单测。
- **(b) PowerShell `Get-NetFirewallRule -DisplayName ... | Get-NetFirewallPortFilter`**：更结构化，但引入 PowerShell 启动开销与执行策略依赖。
- **(c) 端口可达性探测**：从本机主动连一次局域网 IP 的该端口——最接近「真实可达」，但无法区分「防火墙拦截」与「服务没起」，且回环永远通，不适合作为防火墙判定。

推荐 (a)：解析器纯函数化，netsh 调用本身留在 `RunNetsh`。

**3）可自动化 vs 必须手动**
- 可自动化单测：`ParseFirewallRulePresent(典型 netsh show 输出)` 的真/假/空/异常输出分支；`EnsureFirewallRule` 的「探测→按需建」状态机（把 netsh 调用抽象成可注入委托）。
- 必须真机管理员验证：实际 `netsh add` 成功、`_firewallReady=true`、局域网第二台设备真能连上；非管理员下 `_firewallReady=false` 且给指引。这些在 CI/普通 xUnit 里跑不出来。

### ③ 配套测试

新增 `NetSpeedTest.Tests\FirewallProbeTests.cs`：

- `[Theory] [InlineData] ParseFirewallRulePresent_detects_rule_presence`
  - 传入含 `Rule Name(s): NetSpeedTest Web Server` + `Local Port: 8080` + `Action: Allow` 的样例 netsh 输出 → true
  - 传入空串 / `No rules match the specified criteria` → false
  - 传入存在但 `Action: Block` / LocalPort 不匹配 → false
- `[Fact] EnsureFirewallRule_does_not_report_ready_when_denied`
  - 注入一个「模拟无管理员权限、netsh add 退出码非 0」的委托，断言返回 false、且不再返回恒 true。
- `[Fact] FirewallReady_property_matches_probe_result`
  - 用上面两种注入结果驱动 `_firewallReady`，断言 `FirewallReady` 属性与之同步（不再是构造后恒 true）。

### ④ 回归影响面与可能副作用

- 修复后 `_firewallReady` 在非管理员下会变 false → `/api/server` 的 `firewallReady` 字段从 true 变 false。smoke `F-21 /api/server lanError sanitised`（`smoke-test.ps1:175-180`）目前只断言 lanError 不泄露，不断言 firewallReady 真假，故不会因本修复直接红；但若后续有人依赖 firewallReady=true 做前端「已就绪」提示，会变成「未就绪」——这是预期修正。
- **删除规则的清理责任**：现状每次启动先 delete 同名规则。改为「探测后按需建」后，是否还需要在退出/关闭时 delete？需明确：规则是长期驻留还是会话级？若长期驻留，不应每次启动 delete；若会话级，退出时清理由谁做。这影响 UAC 策略选择（见⑥）。
- 通配符监听 `http://+:{port}/`（:464）本身需要 URL ACL 管理员权限，与防火墙规则是两套独立的管理员门槛——修防火墙不等于修通配符监听。

### ⑤ 验证方式

- **自动化**：上述解析器/状态机单测（不起真机）。
- **必须手动·真机管理员**：以管理员身份启动 Release exe，`netsh advfirewall firewall show rule name="NetSpeedTest Web Server"` 确认规则真的被创建、localport 正确；非管理员启动确认 `firewallReady=false` 且 lanError 给出指引。
- **必须手动·第二设备局域网**：修完后用手机/另一台设备连该端口，确认不再被防火墙拦。

### ⑥ 待作者决策点（本缺陷核心）

修复依赖**提权策略**，三种可选：

| 策略 | 用户体验 | 实现复杂度 | 签名/SmartScreen | 删除规则清理责任 | 非管理员场景 |
|---|---|---|---|---|---|
| **① 应用清单 requireAdministrator**（全局始终提权） | 每次启动都弹 UAC，对一个测速工具箱偏重 | 最低：csproj 加 `<ApplicationManifest>`，清单 `<requestedExecutionLevel level="requireAdministrator"/>` | 提权 exe 首次运行 SmartScreen 更敏感，签名未信任时体验差 | 启动即管理员，可正常 add/delete | 不适用——强制管理员，非管理员根本跑不起来 |
| **② 运行时按需提权**（COM elevation moniker / 带参重启 elevated 子进程跑 netsh） | 仅首次配置防火墙时弹一次 UAC，平时不提权 | 中高：需拆分提权子进程、进程间通信、提权失败回退 | 主程序 asInvoker 不背提权名声，提权动作集中在一个小 helper | 提权子进程负责 add；delete 清理也在提权上下文 | 主功能（回环测速）照常，仅防火墙配置需点一次 UAC |
| **③ 不自动改防火墙**（界面给引导文档 / 一键复制 netsh 命令） | 用户手动复制命令到管理员 PowerShell 执行 | 最低：删掉 add/delete 死代码与误导性 ready 状态，UI 加一段说明和「复制命令」按钮 | 无影响 | 由用户自己管理规则，软件不碰 | 最干净：软件永不提权，局域网可用性由用户一次性手动放行 |

- **当前 manifest 现状（已读码确认）**：`NetSpeedTest.csproj`（全文 34 行）**没有 `<ApplicationManifest>` 设置**，项目里也**不存在 app.manifest 文件**（Glob `NetSpeedTest/**/app.manifest` 0 命中）。即当前 requestedExecutionLevel 是编译默认的 **`asInvoker`**——应用以当前用户权限启动，不主动提权。这与「netsh add 在普通用户下失败被吞」的现状一致。
- **推荐**：策略 ③ 或 ②。对一个桌面测速工具箱，策略 ① 的「每次启动都 UAC」体验过重；③ 最透明、最不破坏 asInvoker 现状，代价是局域网开箱即用性下降；② 在「想要开箱即用」与「不想每次提权」之间折中，但实现复杂度与提权子进程的签名/SmartScreen 成本不低。**需作者拍板产品是否承诺「局域网开箱即用」**——若承诺，倾向 ②；若定位为本地工具，③ 足够。

**工作量**：L（约 8–14 小时，取决于选哪种 UAC 策略；仅删死代码+真实探测约 M）。
**风险**：中高（提权策略影响整个应用的启动方式与分发签名）。
**修复后预期结果**：非管理员下 `firewallReady` 真实反映「未放行」而非恒 true；`netsh show rule` 能看到真实规则；smoke `F-21 /api/server` 项不红（它本就不断言 firewallReady）；第二设备局域网可达性从「被拦却报就绪」变为「要么真通、要么明确提示需放行」。

---

## BUG-FALLBACK-007 内嵌兜底网页 fetch 不带 X-NST-Token（S4/P3，Web·兜底页）

### ① 代码级复核：根因 + 实际文件/方法/行号

根因：内嵌兜底 HTML 的 JS 是精简手写版，只注入了 meta token，但 `api()` 封装在发 fetch 时没有像正式前端那样从 meta 读 token 并加 `X-NST-Token` 头。

- 兜底 HTML 字符串常量：`NetSpeedTest\Services\WebServerService.cs:1838` `private const string DefaultIndexHtml = """ ... """;`（止于 :1907）
  - `:1844` `<meta name="nst-token" content="%%NST_TOKEN%%" />`（占位符会被 `InjectSessionToken` 替换）
  - `:1885` `async function api(path, options){ const r = await fetch(path, options); return r.json(); }` —— **不读 meta、不加 X-NST-Token**
  - `:1900` `startTest` POST 仅带 `Content-Type`，`:1902` `stopTest` POST 无头
- 正式前端对照：`NetSpeedTest\wwwroot\assets\web.js`
  - `:244` `var meta = document.querySelector('meta[name="nst-token"]');`
  - `:252` `if (NST_TOKEN) headers["X-NST-Token"] = NST_TOKEN;`
  - `:253` 所有 fetch 统一带该头。兜底页缺的就是这三行。
- 鉴权中间件 `HandleContextAsync`：`WebServerService.cs:844-884`
  - `:851-855` `IsRemoteAllowed` 失败 → **403**（不是 401）
  - `:861` `if (!IsLoopbackRequest(...) && IsStateChangingMethod(method))` 才校验 token，失败 → `:867` **403**
  - `:907-911` `IsStateChangingMethod` = POST/DELETE/PUT/PATCH（**GET 不在内**）
  - `/api/status` GET：`:934-936` 直接 200，**不查 token**
- 令牌注入：`InjectSessionToken` `:1767-1775`，占位符 `%%NST_TOKEN%%`（`:48`）。
- 兜底 HTML 何时被真正使用：`EnsureWwwRoot` `:1788-1799` 在磁盘 `wwwroot/index.html` 缺失时写入 `DefaultIndexHtml`（:1797）；`ServeStaticAsync` `:1720-1762` 优先读磁盘文件，否则 `TryReadEmbeddedFile`（:1734）读嵌入资源 `NetSpeedTest.wwwroot.index.html`（csproj `:30` 把整个 wwwroot 嵌入了 exe）。

**与 QA 记录的重要差异（必须向作者澄清）**：
QA 记录称「非回环 GET /api/status、adapters、history 返回 401」。但读码后：
1. **GET 请求根本不校验令牌**（`:861` 仅对 POST/DELETE/PUT/PATCH 生效）。`/api/status`、`/api/adapters`、`/api/history`(GET) 在同网段（`IsRemoteAllowed` 通过）时，**不带 token 也能 200 返回数据**。
2. **全代码库没有任何一处返回 401**——401 文案虽定义在 `:143`，但 Grep `WriteJsonAsync(ctx, 401, ...)` 0 命中；令牌失败（`:867`）和网段失败（`:853`）都返回 **403**。
3. 因此兜底页在局域网的真实故障是：**GET 数据其实能加载（同网段时），真正失败的是 `startTest`/`stopTest`/`settings`/`history DELETE` 这些写操作（不带 token → 403）**。若访问方与本机不同网段，则连 GET 都被 `IsRemoteAllowed` 挡成 403，但那是网段白名单问题、不是 token 问题。
4. 结论：QA「页面打开但数据全空、操作全失败」中「操作全失败（写操作 403）」准确；「数据全空（GET 401）」与当前代码不符——除非测试环境跨网段。此点需作者/QA 在第二设备复现时确认到底落在哪种情形。

### ② 精确修复方案（二选一，含推荐）

**方案①（QA workaround 的本意，推荐先做）：补兜底页的令牌注入，而非新增健康端点。**
在 `DefaultIndexHtml` 的 `api()` 里对齐 `web.js:244-253`：
```javascript
// 示意，不落盘
const NST_TOKEN = document.querySelector('meta[name="nst-token"]')?.content || '';
async function api(path, options){
  const headers = Object.assign({}, options?.headers || {});
  if (NST_TOKEN) headers['X-NST-Token'] = NST_TOKEN;
  const r = await fetch(path, Object.assign({}, options, { headers }));
  return r.json();
}
```
这样兜底页与正式前端行为一致，**不新增任何免鉴权攻击面**，安全零新增成本。这是最小、最对路的修法。

**方案②：新增免鉴权健康端点 `/api/health`。**
在 `HandleApiAsync`（`:930-967`）加 `case "/api/health":`，仅返回 `{ status:"ok", version: Helpers.AppVersion.Short }`，不返回任何测速数据、绑定、令牌。供兜底页或运维探活使用。
- 安全面评估：该端点绕过 `:861` 的写操作校验（它本身是 GET，本来就不查 token），但仍受 `:851 IsRemoteAllowed` 网段约束——**不对公网开放，只在同网段/回环可见**。不泄露局域网拓扑、版本号属低敏感信息。SSRF 风险低（它是被动 GET，不发起出站请求）。
- 但注意：`/api/health` 解决不了兜底页「写操作 403」的问题——它只是让探活更干净，**不能替代方案①**。

**推荐**：以**方案① 为主**（补齐兜底页 token 头，真正修掉写操作 403）；若作者另有「外部探活」需求再叠加方案②，不要用②替代①。

### ③ 配套测试

沿用 `WebSecurityTests.cs` 的纯函数/常量断言范式：

新增/扩展 `WebSecurityTests.cs`：
- `[Fact] FallbackHtml_sends_X_NST_Token_on_all_api_calls`
  - 取 `WebServerService.DefaultIndexHtml`（把它从 `private const` 提为 `internal const` 以便测试，参考 `TokenPlaceholder` 已是 `internal const` 的做法）。
  - 断言字符串中存在 `querySelector('meta[name="nst-token"]')` 且存在 `X-NST-Token`；
  - 断言兜底页 `api()` 函数体里包含 `headers['X-NST-Token']` 合并逻辑（防止以后再被精简掉）。
- `[Fact] FallbackHtml_has_no_unauthenticated_fetch_to_sensitive_apis`
  - 若选方案②：断言 `/api/health` 仅返回 version/status、不包含 `bindings`/`history`/`token` 字段；
  - 断言兜底页所有 `/api/*` fetch 都走那个带 token 的 `api()` 封装，不存在裸 `fetch('/api/...')`。
- 现有 `InjectSessionToken_*` 用例（`WebSecurityTests.cs:92-112`）继续覆盖 meta 占位符替换，不需改。

### ④ 回归影响面与可能副作用

- 方案① 只动内嵌 HTML 字符串，不改服务端鉴权逻辑，对回环（免 token）和正式前端零影响。
- 方案② 若新增 `/api/health`：它是 GET、不查 token，但仍受网段白名单约束——**不会**变成公网免鉴权口；需在代码评审时确认它没有意外泄露 `lanError`/bindings 等敏感字段（对照 `GetServerInfo:984-985` 已对 lanError 脱敏）。
- 兜底页本身只在「磁盘 wwwroot 与嵌入资源都读不到 index.html」时才成为响应；由于 csproj 把 wwwroot 嵌入了 exe（:30），正常 Release 包几乎不会触发——本缺陷属边缘兜底，优先级 P3 合理。

### ⑤ 验证方式

- **自动化**：上述对 `DefaultIndexHtml` 字符串的断言（纯静态，xUnit 可跑）。
- **自动化（端到端·回环）**：`smoke-test.ps1` 的 `F-01 placeholder replaced by per-run token`（:117-121）、`static web.js ... sends header`（:122-127）继续绿；但 smoke 全程在 `127.0.0.1`（回环免 token），**无法复现本缺陷**。
- **必须手动·第二设备局域网**：移走/重命名 `wwwroot`，从手机/第二台设备（与本机同网段）打开兜底页，确认：① GET 状态/网卡/历史能加载；② 点「下载测速」按钮不再 403（修复前写操作 403）。跨网段场景另记一条。

### ⑥ 待作者决策点

- **【决策】产品是否承诺「远程/局域网留存控制」能力**：若承诺，兜底页必须与正式前端功能对齐（方案①）；若局域网仅为可选高级功能、兜底页只是「本机回环能看一眼」，则当前写操作 403 可接受、只需在兜底页加一句「局域网操作需在正式前端带令牌使用」的静态说明（即任务里的备选②：兜底页不依赖 fetch、内联访问说明）。
- **待澄清**：QA 记录的「GET 401」与代码「GET 不查 token、无 401 返回点」不一致，需在第二设备实测时确认现场到底是 403(网段) 还是 403(token)，以定位真正触发路径。此点无法从代码单独定论。

**工作量**：S（约 1–2 小时，方案①；含单测）。
**风险**：低。仅内嵌前端字符串，不动服务端鉴权。
**修复后预期结果**：第二设备局域网访问兜底页时，写操作（启动/停止测速）从 403 变为 200；`WebSecurityTests` 新增兜底页 token 头断言转绿；回环 smoke 全部保持绿。

---

## 附：与 QA 记录不符 / 无法从代码确定事项汇总

1. **CLAMP-003**：不一致的是「XAML 滑块(10-600/0-2000) vs ViewModel+后端(5-600/0-5000)」，并非 QA 简化的「API vs UI」两端；统一常量需改三处。
2. **FW-006**：代码中**不存在** `IsFirewallRuleNeeded` 方法；`_firewallReady` 是**恒 true**（非 QA 所说的恒 false），但「就绪假通过」结论成立。
3. **FALLBACK-007**：GET `/api/status` 等**不校验令牌**，且全代码库**无任何 401 返回点**；写操作失败返回的是 403。QA「GET 401」与代码不符，真实触发路径需第二设备实测确认。
4. **manifest**：当前为默认 `asInvoker`（csproj 无 ApplicationManifest、无 app.manifest 文件），未提权。
5. **无法从代码确定**：产品是否承诺局域网开箱即用/远程留存（影响 FW 的 UAC 策略与 FALLBACK 的兜底页定位）；README 参数表的实际措辞（本次未在根目录 README 检索到 testTimeoutSec 字样，仅 QA 报告引用，需作者确认 README 现写的是哪组边界）。
