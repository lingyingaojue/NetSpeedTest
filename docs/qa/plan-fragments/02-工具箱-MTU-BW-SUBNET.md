# 02 · 工具箱三缺陷修复方案片段（MTU / 带宽 / 子网）

> 范围：仅设计，不改码。被测逻辑全部位于 `NetSpeedTest\ViewModels\MoreViewModel.cs`（仓库中**不存在** `ToolboxViewModel.cs`）；界面 `NetSpeedTest\Views\MoreWindow.xaml`（实际类名 `MorePage`）；中英文资源 `NetSpeedTest\Languages\Strings.zh-CN.xaml` / `Strings.en-US.xaml`。
> 行号均以本轮 Read/Grep 重新核对（v1.4.3 源码现状）。基线：243 测试 = 239 通过 / 0 失败 / 4 跳过，4 个 `[Fact(Skip)]` 全在 `NetSpeedTest.Tests\ToolboxCalculationTests.cs`。

---

## BUG-MTU-001　MTU 探测 TTL 硬编码为 1，跨网关目标恒失败且结果术语颠倒（S3/P2/L3，工具箱·MTU 探测）

### ① 代码级复核：根因 + 实际文件/方法/行号

- 实现位置：`NetSpeedTest\ViewModels\MoreViewModel.cs`，`StartMtu()`，**实际行 191–214**（QA 摘要写的"约 1232–1307 行"与仓库不符——该文件全文仅 734 行；以实际为准）。
- Ping 类型：`System.Net.NetworkInformation.Ping`（using 在 `MoreViewModel.cs:6`）。
- 关键代码（逐行引用）：

```csharp
// MoreViewModel.cs:198-209
using var ping = new Ping();
int lo = 68, hi = 1500, found = -1;          // :199  二分区间 = ICMP 净荷字节数
while (lo <= hi)
{
    int mid = (lo + hi) / 2;                  // :202
    var opt = new PingOptions(1, dontFragment: true);   // :203  ← TTL 硬编码 1，DF 位=true
    try {
        var reply = await ping.SendPingAsync(MtuHost, 2000, new byte[mid], opt); // :204 净荷=new byte[mid]
        if (reply.Status == IPStatus.Success) { found = mid; lo = mid + 1; }     // 可达→试更大
        else { hi = mid - 1; }                                                    // 不可达→试更小
    }
    catch { hi = mid - 1; }
    sb.AppendLine($"测试 MTU={mid}: ... (当前最大={found})");   // :206
}
sb.AppendLine(found > 0 ?
    $"路径 MTU = {found} bytes (+28 头 = {found + 28} IP MTU)" // :209 ← 术语颠倒
    : "未找到可用 MTU");
```

- **DF 位**：`PingOptions(..., dontFragment: true)`（:203），即 IP DF=1，分片探测本身正确。
- **二分/递增长度逻辑**：标准二分（lo=68..hi=1500，:199/:202），Success 则记 `found=mid` 并放大、否则缩小。68 是 IPv4 最小 MTU（RFC 791），1500 是标准以太网 MTU。
- **净荷↔MTU 换算在哪**：发送净荷 = `new byte[mid].Length = mid`（:204）；IP 总包长 = mid + 20(IP头) + 8(ICMP头) = mid + 28。换算只在结果行 :209 出现，**没有独立纯函数**。
- **根因**：`:203` 把 TTL 写死成 `1`。对跨网关目标（默认 `8.8.8.8`），探测包在第一跳网关就被 TTL 减到 0，回 `TtlExpired`（真机 `mtu-ttl1.txt`：`Reply from 192.168.1.1: TTL expired in transit`），`reply.Status != Success` → 永远走 `hi = mid - 1` → `found` 恒为 -1 → "未找到可用 MTU"。同网段目标 1 跳可达，故开发时"看起来正常"，具迷惑性。真机对照 `mtu-ttl30.txt`：`Reply from 8.8.8.8: bytes=32 time=181ms TTL=107`，TTL 足够即正常到达。
- 对照旁证：同文件路由追踪 `StartTrace()`（:126–154）用 `new PingOptions(ttl, dontFragment:true)` 且 `for ttl=1..30`（:133/:137）——疑似把路由追踪"从 ttl=1 递增"的写法误植到 MTU 探测，导致 TTL 被钉在 1。
- **术语问题**：:209 把**净荷 `found`** 称为"路径 MTU"，把 `found+28` 称为"IP MTU"。标准术语里**路径 MTU = IP 总包长 = 净荷 + 28**，二者正好说反（qa-run.json:275"术语冲突3"佐证）。

### ② 精确修复方案（改动逻辑，不落盘）

1. **TTL 取值**：把 `:203` 的硬编码 `1` 改为足够到达远端的值。建议两步：
   - **先探活**：进入二分前，先用默认 TTL（不显式设 PingOptions，即走系统默认；或显式 `new PingOptions(64, true)`）发一个小包（如 32 字节）确认目标可达。若探活失败/超时，直接提示"目标不可达，请检查地址或网络"，不进入二分。
   - **探测阶段 TTL**：固定用 `64`（Linux/Windows 默认出站 TTL 常见值；路由追踪上限取 30，64 > 30 跳，覆盖绝大多数家用到公网路径）。论证：`mtu-ttl30.txt` 实测 8.8.8.8 回程 TTL=107，说明路径跳数远小于 30；取 64 既高于本工具路由追踪的 30 跳上限，又不会因 TTL 过大引入歧义。
   - 示意（仅设计）：`const int MtuTtl = 64; var opt = new PingOptions(MtuTtl, dontFragment: true);`
2. **探活示意**：`using var probe = new Ping(); var pr = await probe.SendPingAsync(MtuHost, 2000); if (pr.Status != IPStatus.Success) { 输出"目标不可达"; return; }`，再用同一 `ping` 做二分。
3. **文案修正（结果行 :209）**：改为同时呈现两者、术语正确——
   - 建议中文：`路径 MTU = {found + 28} bytes（ICMP 净荷 {found} + 28 字节 IP/ICMP 头）`；失败仍为"未找到可用 MTU（目标不可达或链路不支持该包长）"。
   - 对应英文：`Path MTU = {found + 28} bytes (ICMP payload {found} + 28-byte IP/ICMP header)`；失败 `No usable MTU found (target unreachable or link rejects this size)`。
   - 进度行 :206 的 `测试 MTU={mid}` 建议改称 `测试净荷={mid}`，避免把净荷当 MTU。

> 说明：当前 MTU 相关文案是 **C# 内联字符串**，`Strings.zh-CN.xaml`/`Strings.en-US.xaml` 中**没有**对应 key（两资源里 MTU 仅有 `Tool_Mtu`="MTU 探测"/"MTU Probe"，见 zh:137 / en:137）。因此本缺陷没有现成资源 key 可改——要么改内联字符串，要么（更规范）新增 `Tool_Mtu_PathMTU`、`Tool_Mtu_Payload`、`Tool_Mtu_NotFound` 等 key 并在两套资源同步。详见 ⑥ 待决策。

### ③ 配套测试

- **对应 Skip 用例**：`MtuProbe_UsesTtlLargeEnoughToReachRemoteTarget`（`ToolboxCalculationTests.cs:130-138`）→ **BUG-MTU-001**。
- 该用例本质是**源码文本扫描**（`ReadToolboxSource()` 读 MoreViewModel.cs → `ExtractMethod(source,"StartMtu")` 取方法体），断言：
  - `Assert.DoesNotContain("new PingOptions(1,", startMtu)`（:136）——修复后必须不再出现 `new PingOptions(1,`。
  - `Assert.Contains("PingOptions(", startMtu)`（:137）——仍需保留 PingOptions 构造。
  - **转正方式**：把 `[Fact(Skip=...)]` 的 `Skip=` 去掉即可。需注意：若改成 `new PingOptions(MtuTtl, dontFragment:true)` 或 `new PingOptions(64, ...)`，两条断言都通过；**不要**写成 `new PingOptions(1,...)` 的变体（如默认参数省略导致 TTL=1）。
- **新增纯函数/接口单测建议（只提建议，不改码）**：Ping 难以 mock，建议把"净荷↔MTU 换算"和"二分收敛结果"抽成可静态调用的纯函数再单测：
  - 抽取建议：`internal static int PayloadToLinkMtu(int payload) => payload + 28;` 以及把二分判定 `bool Fits(int payload, IPStatus s) => s == IPStatus.Success;` 之类收敛逻辑抽成纯函数。
  - 用例名建议（新增）：`Mtu_ConvertPayloadToLinkMtu_Adds28`（断言 1472→1500、1452→1480、68→96）、`Mtu_BinarySearch_ConvergesToMaxFittingPayload`（喂入一个 fake 的"在某 payload 阈值上下分别 Success/Fail"的委托，断言二分收敛到阈值）。
  - 若要对 Ping 行为做行为级测试，需把 `Ping` 抽到接口（如 `interface IPingProbe { Task<IPStatus> SendDfAsync(string host, int timeoutMs, int payloadBytes, int ttl); }`）并 fake 之——但这属于较大重构，建议列为后续，本轮以"源码扫描用例转正 + 纯函数换算单测"为主。
- **真机/手动**：跨网关目标的真实可达性无法在 CI 自动化，列为手动验证（见 ⑤）。

### ④ 回归影响面与可能副作用

- TTL 由 1 改为 64：对**同网段/网关目标**无副作用（原本 1 跳就通，64 更通）；对跨网关目标从"恒失败"变为"可成功"。
- 探活新增：多一次 RTT（约一个 2000ms 超时窗口上限），对不可达目标会更早给出明确提示而非跑满二分（约 log2(1500-68)≈11 轮 × 50ms+超时），**反而更快失败**。
- 文案 key：若选择新增资源 key，需同步 zh/en 两套；若直接改内联中文字符串，则**英文界面下 MTU 结果仍显示中文**（当前整个 More 工具箱结果文案都是内联中文，与现有行为一致，不新增偏差，但也不修）。
- 不影响主测速、Web 服务器、NAT 检测等其他模块（blast_radius=仅 MTU 工具，qa-run.json:2922）。

### ⑤ 验证方式

- **自动化**：`MtuProbe_UsesTtlLargeEnoughToReachRemoteTarget` 取消 Skip 转正；新增 `PayloadToLinkMtu` 纯函数单测。
- **必须手动·真机·跨网关**：默认目标 `8.8.8.8`（跨网关）跑一次，应在数秒内给出 `路径 MTU = 1500（或 PPPoE 环境 1480）`，而非"未找到可用 MTU"；同网段网关目标回归一次确认仍正常。

### ⑥ 待作者决策点

- TTL 是否在 UI 暴露可配置项？建议**不暴露**（工具箱定位为一键工具，暴露 TTL 增加认知负担；64 为合理默认）。若作者希望高级可调，可加一个默认 64 的输入框——取舍由作者拍板。
- MTU 结果文案是继续内联中文，还是借本次修复抽取为 `Strings.*.xaml` 多语言 key（与项目其它资源统一）？

**工作量**：M（约 2–3 小时，含探活、TTL、文案、单测与真机回归）。
**风险**：低（仅 MTU 工具内逻辑改动，TTL 放大不会引入新的网络副作用）。
**修复后预期结果**：跨网关默认目标 8.8.8.8 能得出正确路径 MTU；结果同时正确显示"链路 MTU=净荷+28"；`MtuProbe_UsesTtlLargeEnoughToReachRemoteTarget` 转绿。

---

## BUG-BW-004　带宽换算不拒绝 0/负数，0 显示"∞ 秒"、负数显示负耗时（S4/P3，工具箱·下载耗时估算）

### ① 代码级复核：根因 + 实际文件/方法/行号

- 实现位置：`MoreViewModel.cs`，`CalcBandwidth()`，**实际行 400–413**。
- 关键代码：

```csharp
// MoreViewModel.cs:405-409
if (!double.TryParse(BwMbps, out var mbps)) { BwResult = "请输入有效数字"; return; }   // :405 仅判可解析
var sb = new StringBuilder(); sb.AppendLine($"带宽换算: {mbps} Mbps"); ...
sb.AppendLine($"= {mbps / 8:F2} MB/s"); sb.AppendLine($"= {mbps * 1000 / 8:F1} KB/s"); // :407
sb.AppendLine($"= {mbps / 1000:F4} Gbps"); sb.AppendLine($"= {mbps * 125:F0} KBps");   // :408
sb.AppendLine($"100 MB 文件 ≈ {100 * 8 / mbps / (mbps > 0 ? 1 : 0.001):F1} 秒");      // :409 ← 除零点
```

- **根因**：:405 只做了 `double.TryParse`，没有 `mbps > 0` 正数域校验。
  - `mbps=0`：`100*8/0.0` 在 C# 中 = `double.PositiveInfinity`；后面再 `/0.001` 仍是 Infinity；`:F1` 格式化为 `"∞"`。:409 的 `(mbps > 0 ? 1 : 0.001)` 是个"创可贴"，但**除零在它之前已经发生**（800/0 先得 Infinity），所以挡不住。
  - `mbps=-50`：`800 / -50 / 1 = -16.0`，输出"-16.0 秒"；同时 :407/:408 的换算行也会出现负值（-6.25 MB/s 等）。
- **空值/非数字**：`""`、`"abc"` 走 :405 失败分支，已提示"请输入有效数字"（已有用例 `Bandwidth_NonNumeric_ReportsError`，:99-103 覆盖）。空白 `"   "` 会被 `double.TryParse` 解析成 0 → 落到除零分支，需一并防住。

### ② 精确修复方案（改动逻辑，不落盘）

- 在 :405 解析成功后、构建结果前，加正数域校验：

```csharp
// 设计示意，不落盘
if (!double.TryParse(BwMbps, out var mbps)) { BwResult = "请输入有效数字"; return; }
if (mbps <= 0) { BwResult = "请输入大于 0 的有效带宽数值（Mbps）"; return; }   // 新增：拒绝 0/负/NaN/∞
```

- 建议同时 `IsNaN(mbps) || IsInfinity(mbps)` 一并拒绝（`double.TryParse` 对 `"NaN"`/`"∞"` 会解析成功，需显式挡）。
- **UI 提示方式**：与现有一致，直接把错误字符串写入 `BwResult` 只读结果框（不弹框，工具箱其它错误都是这种风格，如 :379/:448）。
- **是否禁用计算按钮**：`MoreWindow.xaml:228` 的"换算"按钮 `Command="{Binding CalcBandwidthCommand}"` 没有 `IsEnabled` 绑定（不像 Ping/MTU 有 IsBusy 反转）。建议**不禁用按钮**，保持"点了才报错"的轻量风格（与端口/子网工具一致）；是否加 `IDataErrorInfo`/实时禁用属产品取舍（见 ⑥）。
- **字符串资源 key**：当前错误文案是内联中文（"请输入有效数字"），资源文件里**没有**对应 key。建议文案定为 **"请输入大于 0 的有效带宽数值"**（同时满足两个 Skip 断言：含"大于 0"、含"有效"）。若走资源化，新增 `Tool_Bw_MustBePositive`（zh="请输入大于 0 的有效带宽数值（Mbps）"，en="Please enter a bandwidth greater than 0 (Mbps)"）。

### ③ 配套测试

- **对应 Skip 用例（BUG-BW-004 占 2 个）**：
  1. `Bandwidth_Zero_ShouldReject_NotShowInfinity`（`ToolboxCalculationTests.cs:107-113`）
     - 输入 `"0"`；断言 `DoesNotContain("∞", r)`、`Contains("大于 0", r)`。
     - 转正：去掉 `Skip=`。修复后应返回含"大于 0"的错误、无"∞"。
  2. `Bandwidth_Negative_ShouldReject`（:115-121）
     - 输入 `"-50"`；断言 `Contains("有效", r)`、`DoesNotContain("-", r.Replace("-50",""))`。
     - 转正：去掉 `Skip=`。注意第二条断言——错误串里不得出现 `-`，且因为提前 return 不再回显"带宽换算: -50 Mbps"头，`r.Replace("-50","")` 后应为纯错误文案。
- **新增边界用例建议**（纯计算逻辑，可直接调 VM 复算）：
  - `Bandwidth_NaN_ShouldReject`：输入 `"NaN"` → 含"有效"/"大于 0"，不崩溃。
  - `Bandwidth_Whitespace_ShouldReject`：输入 `"   "` → 走 0 值分支被拒（不显示 ∞）。
  - `Bandwidth_Positive_StillConverts`：输入 `"100"` 回归，仍得 `= 12.50 MB/s`（已有 `Bandwidth_100Mbps_ConvertsTo12_5_MBps` 覆盖，防校验改坏正数路径）。
- **抽取建议**：把"100 MB 文件耗时 = 文件字节×8 / mbps"抽成 `internal static double EstimateSeconds(double mbps, double fileMb) => fileMb * 8 / mbps;` 纯函数，对 `mbps>0` 才调用；单测直接断言 `EstimateSeconds(100,100)==8`、`EstimateSeconds(0,x)`/负数在调用前已被拦。当前逻辑嵌在 VM 命令里，现有测试靠"设属性→执行命令→读结果字符串"驱动（见 `CalcBandwidth` helper :28-34），转正用例沿用此模式即可，不必先重构。

### ④ 回归影响面与可能副作用

- 仅影响带宽换算工具；正数路径（:407-408 换算行）不动。
- 加 `<=0` 拦截后，原 :409 的 `(mbps>0?1:0.001)` 创可贴可一并删除（已无除零可能）。
- 若文案从"请输入有效数字"扩展为含"大于 0"，英文资源（若资源化）需同步；当前内联中文不影响英文资源文件。

### ⑤ 验证方式

- **自动化**：两个 Skip 用例取消 Skip 转正 + 新增 NaN/空白用例；连同现有 `Bandwidth_*` 正常路径一起跑全绿。
- 无需真机（纯计算）。

### ⑥ 待作者决策点

- 是否把"换算"按钮做成输入非法时实时禁用（需加 `IDataErrorInfo`/`CanExecute`）？建议保持现状（点击报错），与工具轻量风格一致。

**工作量**：S（约 1 小时）。
**风险**：低。
**修复后预期结果**：输入 0/负数/NaN/空白均给出"请输入大于 0 的有效带宽数值"，不再出现"∞ 秒"或负耗时；正数换算结果不变。

---

## BUG-SUBNET-005　子网计算不校验掩码位连续性，非法掩码输出错误 CIDR/网络地址（S4/P3，工具箱·子网计算）

### ① 代码级复核：根因 + 实际文件/方法/行号

- 实现位置：`MoreViewModel.cs`，`CalcSubnet()`，**实际行 374–394**。
- 关键代码：

```csharp
// MoreViewModel.cs:379-387
if (!IPAddress.TryParse(SubnetIp, out var ip) || !IPAddress.TryParse(SubnetMask, out var mask))
    { SubnetResult = "IP 地址或掩码格式无效"; return; }                 // :379 仅判格式可解析
var ib = ip.GetAddressBytes(); var mb = mask.GetAddressBytes();
if (ib.Length != 4 || mb.Length != 4) { SubnetResult = "仅支持 IPv4"; return; } // :381
var nb = new byte[4]; var bb = new byte[4]; uint ipv4 = 0, m = 0;
for (int i=0;i<4;i++){ nb[i]=(byte)(ib[i]&mb[i]); bb[i]=(byte)(ib[i]|(byte)(~mb[i]));
                       ipv4=(ipv4<<8)|ib[i]; m=(m<<8)|mb[i]; }                  // :383 网络/广播/掩码打包
uint cidr = 0; uint tm = m; while (tm>0){ if ((tm & 0x80000000)!=0) cidr++; tm<<=1; } // :384 ← 只数前导 1，无连续性校验
uint hosts = cidr>=32 ? 1u : cidr==31 ? 0u : (uint)((1L<<(32-(int)cidr))-2);          // :385
```

- **根因**：:384 仅按"前导连续 1 个数"数出 CIDR，**从不检查掩码二进制是否形如 1...10...0**。`IPAddress.TryParse` 只保证它是个合法 IPv4 地址，不保证它是合法子网掩码。
- **与 QA 记录的重要出入**：QA（qa-run.json:3086）称 255.0.255.0 "算出 CIDR=16"。按 :384 实际是**前导 1 计数**而非全 1 popcount：255.0.255.0 = `0xFF00FF00`，前导 1 只有 **8 个**（首字节 0xFF 后第二个字节是 0x00），故代码实际输出 **`/8`**，网络地址 = `192.168.1.1 & 0xFF00FF00` = `192.0.1.0`，广播 = `192.255.1.255`。结论（非法掩码被放过、结果误导）与 QA 一致，但**"CIDR=16"这个具体数字与实际代码不符，实为 /8**。修复方案不受影响。
- 合法掩码计算路径（/24、/30、/32、/31）已有 4 个正式用例覆盖且正确。

### ② 精确修复方案（改动逻辑，不落盘）

- 在 :383 拿到 `uint m`（网络序 32 位掩码）后、:384 数 CIDR 前，插入**连续性校验**。两种等价写法：
  - **写法 A（推荐，主机位取反判 2 的幂）**：
    ```csharp
    uint h = ~m;                       // 主机位应为 0...01...1
    bool contiguous = (h & (h + 1)) == 0;   // h 全是低位 1 时才成立；/0(h=0xFFFFFFFF,h+1=0)与/32(h=0)均通过
    if (!contiguous) { SubnetResult = "无效的子网掩码：二进制位必须连续（形如 11110000）"; return; }
    ```
  - **写法 B（与任务描述一致：取反+1 为 2 的幂）**：`uint inv = ~m; bool ok = inv == 0 || IsPowerOfTwo(inv + 1);`（/32 时 inv=0 单独放行）。与 A 等价，择一即可。
- 校验通过后再走原 :384/:385 逻辑。
- **错误提示**：必须含"掩码"二字（对齐 Skip 断言 `Assert.Contains("掩码", r)`，见 :127）。建议文案：**"无效的子网掩码：二进制位必须连续（如 255.255.255.0）"**；英文（若资源化）`Invalid subnet mask: bits must be contiguous (e.g. 255.255.255.0)`。
- 当前错误文案均为内联中文，资源文件无对应 key（同 MTU/带宽）。

### ③ 配套测试

- **对应 Skip 用例**：`Subnet_NonContiguousMask_ShouldReportInvalidMask`（`ToolboxCalculationTests.cs:123-128`）→ **BUG-SUBNET-005**。
  - 输入 `192.168.1.1` / `255.0.255.0`；断言 `Contains("掩码", r)`。
  - 转正：去掉 `Skip=`。修复后应返回含"掩码"的错误串，不再输出 /8 网络地址。
- **非法掩码用例集**（建议新增，喂给 `CalcSubnet` helper :19-26，断言结果含"掩码"或"无效"）：
  - `255.0.255.0`（1 位断档）
  - `255.255.0.255`（后段 1 断档）
  - `0.255.255.255`（前导 0 后又有 1）
  - `255.255.255.128` 是合法的（/25）→ **不应**报错，用于区分。
- **合法掩码回归用例**（若干代表值，断言不报错且网络/广播/主机数正确）：
  - `/0` = `0.0.0.0`、`/8` = `255.0.0.0`、`/16` = `255.255.0.0`、`/24` = `255.255.255.0`、`/25` = `255.255.255.128`、`/30` = `255.255.255.252`、`/31` = `255.255.255.254`、`/32` = `255.255.255.255`。
  - 已有正式用例覆盖 /24(:37)/30(:49)/32(:59)/31(:67)，建议补 /8、/16、/25、/0 代表值。
  - 断言点：CIDR 标注正确、网络地址=`ip&mask`、广播=`ip|~mask`、可用主机数 = 2^(32-cidr)-2（/32=1、/31=0 特例已有）。
- **抽取建议**：把连续性校验抽成 `internal static bool IsValidSubnetMask(uint mask)` 纯函数 + `internal static uint MaskToCidr(uint mask)`，单测直接覆盖非法/合法全集，不必每次都跑 VM 字符串断言。当前测试靠 VM 字符串 `Contains` 断言（脆弱但已定型），转正用例沿用即可。

### ④ 回归影响面与可能副作用

- 仅影响子网计算工具；合法掩码路径（已有 4 个正式用例）必须保持不变。
- 注意边界：`0.0.0.0`（/0）在数学上是合法连续掩码（默认路由），写法 A 会放行；但现有 :385 对 cidr=0 算 `(1L<<32)-2` 会得到约 42.9 亿主机数，是否允许 /0 输入见 ⑥。
- 英文资源（若资源化）需同步；当前内联中文不影响 en-US.xaml。

### ⑤ 验证方式

- **自动化**：`Subnet_NonContiguousMask_ShouldReportInvalidMask` 取消 Skip 转正；新增非法掩码集 + 合法代表值回归；连同现有 4 个 `Subnet_*` 正式用例一起全绿。
- 无需真机（纯计算）。

### ⑥ 待作者决策点

- 是否接受 `/0`（`0.0.0.0`）作为合法输入？建议放行连续性校验，但对 cidr=0 的"可用主机数"显示做特别提示（约 42.9 亿，意义不大），或直接提示"/0 为默认路由，无实际子网意义"。

**工作量**：S–M（约 1.5–2 小时，含校验函数 + 非法/合法用例集）。
**风险**：低。
**修复后预期结果**：255.0.255.0 等非连续掩码被拒并提示"无效的子网掩码"；合法 /0~/32 计算结果不变且正确。

---

## 附：4 个 Skip 用例 → 3 个缺陷对应表

| Skip 用例（ToolboxCalculationTests.cs） | 行号 | 对应缺陷 | 转正后断言要点 |
|---|---|---|---|
| `Bandwidth_Zero_ShouldReject_NotShowInfinity` | 107-113 | **BUG-BW-004** | 输入"0"：结果无"∞"、含"大于 0" |
| `Bandwidth_Negative_ShouldReject` | 115-121 | **BUG-BW-004** | 输入"-50"：含"有效"、无残留负号 |
| `Subnet_NonContiguousMask_ShouldReportInvalidMask` | 123-128 | **BUG-SUBNET-005** | 192.168.1.1/255.0.255.0：含"掩码" |
| `MtuProbe_UsesTtlLargeEnoughToReachRemoteTarget` | 130-138 | **BUG-MTU-001** | 源码扫描：StartMtu 方法体不含 `new PingOptions(1,`、仍含 `PingOptions(` |

> 即：BW-004 占 2 个 Skip，SUBNET-005 占 1 个，MTU-001 占 1 个，合计 4。
