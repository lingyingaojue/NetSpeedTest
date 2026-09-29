# NetSpeedTest v1.4.3 QA 收口报告（测试报告）

> 本测试报告给出测试结论、执行证据、缺陷定级与发布判断；**详细用例**追踪矩阵与完整 **Bug 单** 见配套文档《测试用例与追踪矩阵.md》。

| 项目 | 内容 |
|---|---|
| 测试对象 | NetSpeedTest（.NET 8 + WPF 网络测速工具箱） |
| 版本 | v1.4.3（程序集 1.4.3.0，csproj / README / CHANGELOG / 关于页四处一致） |
| 测试类型 | 全流程 QA 收口：需求基线 + 风险设计 + 单元测试 + 静态审查 + 真实端到端冒烟 |
| 执行日期 | 2026-09-30 |
| 构建 | Debug、Release 两配置均 **0 错误 / 0 警告** |
| 自动化测试 | **243 个方法：239 通过 / 0 失败 / 4 跳过**（跳过项为 4 个缺陷复现检查点） |
| 端到端冒烟 | tools/smoke-test.ps1 驱动真实 Release 程序：**32 项检查全部 PASS / 0 失败**，运行日志 0 FATAL |
| 业务用例 | 39 个：**通过 25 · 失败 5（均为已确认缺陷）· 阻塞 7（未执行）· 跳过 2** |
| 开放缺陷 | **7 个：P2×4、P3×3，无 P0/P1，无 S1/S2** |
| **发布判断** | **undetermined（证据不足，暂不定终审）——非 no_go；距 conditional_go 仅差一次完整真实测速与缺陷处置** |

---

## 1. 结论摘要

NetSpeedTest v1.4.3 的**构建质量、核心测速算法、远程 Web 安全、服务端生命周期与并发稳定性**均取得了充分且相互印证的证据，全部通过：

- **构建**：Debug / Release 均 0 错误 0 警告，版本号四处一致。
- **单元/逻辑层**：全量 243 个测试方法 239 通过、0 失败、4 跳过；跳过的 4 个是为已确认缺陷预置的复现用例（修复后启用即转为回归测试）。本轮新增 31 个测试方法。
- **端到端真机层（本轮新增）**：用 `tools/smoke-test.ps1` 驱动真实 Release 程序（`--debug`，127.0.0.1:8080）跑完 **32 项检查全部 PASS**，覆盖令牌注入、安全响应头、固定错误体、读 API、输入校验与 SSRF 拦截、测试生命周期（200 不早于 running、重复 409、stop 复位）、20 次快速启停、设置原子持久化与 8 并发写、90 次并发轮询；运行日志 391 行 **0 FATAL、0 未观察异常/并发枚举修改**。
- **缺陷**：静态审查与真实探测共锁定 7 个开放缺陷，最高 S3/P2，**没有 P0/P1 阻断级、没有 S1/S2 严重级**。其中 MTU 探测缺陷已用真实 `ping` 完成 L3 可复现取证。

**为什么仍不是「可发布」**：产品最核心的「真实外网下载/上传/双向测速」还没有**完整跑完一次**去核对最终吞吐数值、平均窗口与 URL 明细（冒烟只验证到发起后真实进入运行态）；跨设备网段白名单、多网卡聚合、CSV/HBCS、GUI 准备阶段取消等人工/跨设备项未做；7 个缺陷尚未处置。因此按「零证据禁终审」原则维持 **undetermined**。由于无任何阻断级缺陷且 P0/P1 机制全绿，**不是 no_go**。

---

## 2. 测试范围与对象

覆盖仓库 `D:\Program Files\DSH\NetSpeedTest` 全部核心功能：

1. 测速引擎：多 URL 并发下载、上传校验、双向同时测速、自适应并发、多网卡出口、延迟/抖动/丢包/UDP、DNS 预热、取消。
2. 数据：SQLite 历史持久化与分页、CSV 导出、节点增删改、HBCS 导入导出。
3. 内置 Web 服务器与 API：回环/局域网监听、单次令牌鉴权、安全响应头、SSRF/路径穿越防护、状态/网卡/历史/设置接口、测试生命周期、并发可用性、防火墙探测、wwwroot 兜底页。
4. 18 合 1 工具箱（含 MTU 探测、子网/带宽计算等）。
5. 自动更新（GitHub releases、资产选择、SHA256 校验）。
6. 全局异常兜底、并发资源安全等非功能项。

不在本轮范围：安装包代码签名、GitHub Release 发布流水线、广告/赞助内容、WPF 控件像素级 UI 走查与主题/多语言渲染。

---

## 3. 测试环境

| 项 | 值 |
|---|---|
| 操作系统 | Windows（真实桌面，非容器/沙箱） |
| .NET SDK | 8.0.425；目标框架 net8.0-windows（WPF），AnyCPU |
| 构建配置 | Debug、Release |
| 测试框架 | xUnit（`dotnet test`，trx 日志） |
| 测试程序集可见性 | InternalsVisibleTo 已配置，可直接测 internal 与嵌套 internal 类 |
| 端到端 | 真实 Release `NetSpeedTest.exe --debug`，HTTP 127.0.0.1:8080 |
| 数据隔离 | 单元测试经反射切换临时 SQLite 库并清理；冒烟前后备份/恢复本地 appsettings.json、web.json |

---

## 4. 执行总览

| 阶段 | 命令 / 方式 | 结果 | 证据 |
|---|---|---|---|
| 依赖还原 | `dotnet restore` | 成功 | build-release.log |
| Release 构建 | `dotnet build NetSpeedTest.sln -c Release --nologo` | **退出码 0，0 警告 0 错误**（Debug 同为 0/0） | EVD-BUILD-001 |
| 全量单元测试 | `dotnet test -c Release --logger trx` | **243 方法：239 通过 / 0 失败 / 4 跳过** | EVD-TEST-002（qa-final.trx、test-final.log） |
| 静态代码审查 | 通读测速/Web/工具箱/数据/更新等核心源码与 XAML、README | 锁定 7 个缺陷候选并逐一取证 | evidence\ 各快照 |
| MTU 真实复现 | `ping -i 1 8.8.8.8` 对比 `ping -i 30 8.8.8.8` | TTL=1 首跳 TTL expired、TTL=30 正常应答（TTL=107），证实 BUG-MTU-001 | EVD-MTU-PING-003（L3） |
| **端到端冒烟** | `tools\smoke-test.ps1`（真实 Release exe） | **32/32 PASS、0 失败；debug.log 391 行 0 FATAL/异常** | **EVD-SMOKE-E2E-010、EVD-SMOKE-LOG-011（L4）** |

冒烟 32 项按类归并：

| 检查组 | 检查数 | 结果 | 关键观测 |
|---|---|---|---|
| F-01 令牌 / 静态服务 | 4 | 全 PASS | index 注入 64hex 单次令牌、占位符已替换、web.js 带 X-NST-Token 且无内嵌密钥、令牌非全零 |
| FN-06 安全头 / 固定错误体 | 3 | 全 PASS | nosniff + DENY + no-referrer；静态资源带 nosniff；404 固定文案且带安全头 |
| 读 API | 5 | 全 PASS | status/adapters/history/server 正常；lanError 脱敏不泄漏 netsh/路径/异常；bindings=3、lanReady=true |
| 输入校验 / SSRF | 8 | 全 PASS | 畸形 JSON、超大 body、未知路由、环回、云元数据、非 http、非白名单端口、私网 profile 均正确拒绝 |
| 测试生命周期 | 5 | 全 PASS | start 3686ms 进入 running=true 才 200；重复 409；stop 200 后约 250ms 复位；期间 status 9ms |
| 20 次快速启停 | 1 | PASS | start∈{200,409}、stop=200、status<2s；最差 start 3791ms（等待真实 running，符合「不得提前 200」） |
| 设置 F-10 | 3 | 全 PASS | 往返一致、原子落盘合法 JSON 无 .tmp 残留、8 并发 POST 全 200 |
| 负载可用性 | 1 | PASS | 90 次并发 GET /api/status 全部 200 |
| 运行时错误 | 2 | 全 PASS | 391 行日志 0 FATAL、0 Unhandled/Unobserved/Collection was modified |

> 副作用与清理：冒烟会启动真实程序、占用 8080、产生真实外网连接并改写本地 web.json/appsettings.json。执行前已备份、执行后已原样恢复（备份存 `config-backup/`），teardown 结束进程并释放端口，复核无残留。

---

## 5. 分模块验证结论

| 模块 | 结论 | 依据 |
|---|---|---|
| 解决方案构建 / 版本 / 文档 | ✅ 通过 | 两配置 0/0；版本号四处一致；README 声明均有实现对应 |
| 测速引擎（自适应/URL 均衡/上传/网卡/UDP/重定向） | ✅ 逻辑层通过 | 31 个新增测试 + 既有测试全绿；覆盖 P0/P1 机制与边界 |
| 真实外网测速最终结果 | ⛔ 未完整验证 | 冒烟证 start→running 可连真实节点，但未跑完核对最终速率/平均窗口/明细（TC-NETSPEEDTEST-018） |
| Web 令牌鉴权 / 静态服务 | ✅ **端到端通过** | 单元测试 + 冒烟 F-01 真机印证 |
| Web 安全（SSRF/路径穿越/安全头/错误体） | ✅ **端到端通过** | 60+ SSRF 形态单测 + 冒烟 8 项校验/3 项安全头真机印证 |
| Web 生命周期 / 设置持久化 / 并发可用性 | ✅ **端到端通过** | 冒烟：生命周期 5 项、20 次快速启停、8 并发设置、90 并发 status 全 PASS |
| 真实运行稳定性（FATAL/未观察异常） | ✅ **端到端通过（Web/服务端侧）** | 冒烟全流程后 391 行日志 0 FATAL/异常（TC-NETSPEEDTEST-080/081） |
| 历史数据持久化 / 分页 | ✅ 通过 | DataServicePaginationTests 全绿；冒烟 /api/history 正常（total=97） |
| CSV 导出 / HBCS 往返 | ⛔ 未执行 | 需 GUI 人工操作（TC-CSV-051、TC-HBCS-052） |
| 跨设备网段白名单 / 兜底页 | ⛔ 未执行 | 需第二台设备（TC-WEBACL-038、BUG-FALLBACK-007） |
| 18 合 1 工具箱 | ❌ 部分失败 | 子网/带宽等常规计算正确；MTU 探测（BUG-MTU-001）、带宽 0/负（004）、非法掩码（005）确认缺陷 |
| 自动更新 | ✅ 通过 | 版本判定/资产选择/SHA256/失败降级测试全绿 |
| 防火墙自动放行 | ❌ 静态确认缺陷 | add 为死代码、状态恒真（BUG-FW-006）；提权路径未实测 |
| 双向测速 URL 明细 | ❌ 静态确认缺陷 | RunFullTestAsync 聚合 UrlDetails 恒空（BUG-BIDI-002），未真机复核 |

---

## 6. 开放缺陷清单（7 个，均未修复）

| Bug ID | 模块 | S/P | 证据等级 | 一句话结论 |
|---|---|---|---|---|
| BUG-MTU-001 | 工具箱·MTU | S3/P2 | **L3 可复现** | PingOptions TTL 硬编码 1，跨网关目标首跳 TTL 过期，默认 8.8.8.8 恒「未找到可用 MTU」；术语颠倒 |
| BUG-BIDI-002 | 测速·双向 | S3/P2 | L2 | RunFullTestAsync 返回 UrlDetails=new() 恒空，双向测速无 URL 明细 |
| BUG-CLAMP-003 | Web·设置 API | S3/P2 | L2 | 仅 2 项钳制范围与 UI/README 不一致：testTimeoutSec（5–600 vs 10–600）、threadRampUpMs（0–5000 vs 0–2000） |
| BUG-FW-006 | Web·防火墙 | S3/P2 | L2 | add 入站规则为死代码（删完即 return），只删不建、FirewallReady 恒真且无消费者 |
| BUG-BW-004 | 工具箱·带宽 | S4/P3 | L2 | 带宽 0 显示「∞ 秒」、负数显示负耗时，未校验正数域 |
| BUG-SUBNET-005 | 工具箱·子网 | S4/P3 | L2 | 不校验掩码位连续性，255.0.255.0 输出错误 CIDR/网络地址 |
| BUG-FALLBACK-007 | Web·兜底页 | S4/P3 | L2 | 内嵌兜底页 fetch 不带 X-NST-Token，wwwroot 缺失时非回环 GET 401/POST 403 |

> 完整复现步骤、根因分析与修复建议见《测试用例与追踪矩阵.md》第三节「Bug 单」。本轮为 QA 评审，**未改动任何产品代码**；4 个缺陷已在测试工程中以 `[Fact(Skip)]` 预置复现用例。

---

## 7. 未验证范围（本轮仍缺的证据）

1. WPF 控件像素级 UI 走查、深色/浅色主题与中英文渲染未做人工全量核对；准备阶段取消（TC-NETSPEEDTEST-020）未在 GUI 人工执行。
2. 真实外网**完整跑完**下载/上传/双向测速的最终吞吐数值、平均窗口、曲线与 URL 明细未实测；冒烟仅证 start→running=true 可连真实节点（约 3.7s 进入运行态），双向明细缺陷 BUG-BIDI-002 未真机复核（TC-NETSPEEDTEST-018）。
3. 第二台设备跨网段访问、ACL 网段白名单拦截未实测（TC-WEBACL-038）；wwwroot 缺失兜底页非回环 401/403（BUG-FALLBACK-007）未在第二设备复现。
4. 多网卡多宽带真实绑定与聚合未在多上联环境实测（TC-NETSPEEDTEST-019），绑定分支以纯逻辑测试覆盖。
5. 防火墙 netsh 提权添加/删除规则未在管理员/非管理员环境分别实测（TC-WEB-082）；冒烟以回环访问，lanReady=true 不构成跨设备防火墙放行证据。
6. CSV 导出、HBCS 往返、18 工具中依赖外网第三方接口（ip-api/ipify/STUN）的人工冒烟（TC-CSV-051、TC-HBCS-052、TC-NETSPEEDTEST-065）未执行。
7. 安装包代码签名、GitHub Release 发布流水线、广告/赞助内容不在本轮范围。

---

## 8. 发布判断与放行条件

**判断：undetermined（证据不足，暂不定终审）。**

- 不判 **go**：核心外网完整测速结果、跨设备/多网卡/GUI 人工项缺决定性运行证据，且 7 个缺陷未处置。
- 不判 **no_go**：所有 P0/P1 机制既有自动化又有真机端到端证据且全部通过，无 S1/S2、无 P0/P1 缺陷，阻断 Bug 集合为空。
- 距离 **conditional_go** 仅差：一次完整真实测速结果核对 + 缺陷处置结论。

转为可发布需满足（条件、责任与回滚已登记）：

1. 完成至少一次跑完的真实外网下载、上传、双向测速，核对最终吞吐、平均窗口/曲线与 URL 明细（TC-NETSPEEDTEST-018；双向同时真机复核 BUG-BIDI-002）。
2. 用第二台设备验证局域网令牌与网段白名单（TC-WEBACL-038），并在 wwwroot 缺失场景验证兜底页（BUG-FALLBACK-007）。
3. 处置 4 个 P2：BUG-MTU-001、BUG-BIDI-002、BUG-CLAMP-003、BUG-FW-006（防火墙项需先定 UAC 提权策略）。
4. 修复后取消 4 个 `[Fact(Skip)]` 复现用例并确保全量测试转绿。
5. 按需补齐多网卡（019）、CSV/HBCS（051/052）、GUI 准备取消（020）。

**监控与回滚**：冒烟已建立「端到端 32/32、debug.log 0 FATAL」基线，修复后重跑全量 `dotnet test` 与 smoke 比对；若引入回归可 git revert 修复提交回到 v1.4.3 基线，紧急时可暂不启用局域网 Web、不使用 MTU 探测工具（均不影响回环本地测速）。

---

## 9. 本轮新增测试资产

| 测试文件 | 用例数 | 覆盖点 |
|---|---|---|
| UrlBalancerTests.cs | 9 | URL 探索、最快选择、失败/超时冷却、单 URL、明细分类 |
| NicEgressResolverTests.cs | 5 | 出口 HttpClient 解析五分支、绑定失败显式化 |
| ToolboxCalculationTests.cs | 12（8 通过 + 4 Skip） | 子网/带宽常规计算 + MTU/带宽 0负/非法掩码缺陷复现 |
| DataServicePaginationTests.cs | 5 | SQLite 分页/计数/删除/清空/字段往返（临时库，自动清理） |

---

## 10. 证据清单

| 证据 ID | 等级 | 形式 | 位置 |
|---|---|---|---|
| EVD-BUILD-001 | L4 | 构建日志 | build-release.log |
| EVD-TEST-002 | L4 | 测试日志/trx | test-final.log、qa-final.trx（243：239/0/4） |
| EVD-MTU-PING-003 | L3 | 真实 ping 输出 | mtu-ttl1.txt、mtu-ttl30.txt |
| EVD-MTU-SRC-004 / BIDI-008 / CLAMP-005 / FW-006 / FALLBACK-007 / TOOLBOX-009 | L2 | 源码行号快照 | evidence\evidence-bug-*.txt |
| **EVD-SMOKE-E2E-010** | **L4** | **端到端冒烟记录（32/32）** | **smoke-test.log** |
| **EVD-SMOKE-LOG-011** | **L4** | **运行日志零致命错误** | **evidence\smoke-debug.log（391 行 0 FATAL）** |
| 配置备份 | — | 数据清理记录 | config-backup\（appsettings.json、web.json，已恢复） |

---

## 11. 验收检查结果（9 项：通过 5 · 未通过 2 · 阻塞 2）

| 验收 ID | 验收项 | 阻断 | 结果 |
|---|---|---|---|
| AC-BUILD-001 | 可构建且版本/文档一致 | 是 | ✅ 通过 |
| AC-CORE-002 | 测速核心正确性（逻辑层） | 是 | ✅ 通过（真机完整结果另计未验证） |
| AC-SEC-003 | 远程 Web 安全 | 是 | ✅ 通过（单测 + 冒烟真机双证） |
| AC-LIFE-004 | 生命周期/持久化/可用性 | 是 | ✅ **通过（冒烟端到端真机取证）** |
| AC-UPDATE-007 | 更新检查与签名验证 | 否 | ✅ 通过 |
| AC-DATA-005 | 历史与节点数据一致性 | 否 | ⛔ 阻塞（分页自动化通过；CSV/HBCS 待人工） |
| AC-STAB-009 | 真实运行稳定性 | 否 | ⛔ 阻塞（Web/服务端已 0 FATAL；完整外网测速/GUI 取消待补） |
| AC-TOOL-006 | 工具箱计算与输入校验 | 否 | ❌ 未通过（BUG-MTU-001/BUG-BW-004/BUG-SUBNET-005） |
| AC-DEFECT-008 | 缺陷逐项复核定级 | 否 | ❌ 未通过（7 个开放缺陷待处置） |

> 阻断项 AC-BUILD-001 / AC-CORE-002 / AC-SEC-003 / AC-LIFE-004 已全部通过；P0 阻断 Bug 集合为空，与 release_decision.blocking_bug_ids 一致。剩余阻塞集中在「完整真实测速结果」与跨设备/GUI 人工项，故维持 undetermined。
