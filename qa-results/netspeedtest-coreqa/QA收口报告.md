# NetSpeedTest v1.4.3 QA 收口报告（测试报告 · 缺陷修复后终版）

> 本报告给出测试结论、执行证据、缺陷定级、**修复与回归结果**、发布判断；**详细用例**追踪矩阵与完整 **Bug 单** 见配套文档《测试用例与追踪矩阵.md》，逐项修法见《缺陷修复方案.md》。

| 项目 | 内容 |
|---|---|
| 测试对象 | NetSpeedTest（.NET 8 + WPF 网络测速工具箱） |
| 版本 | v1.4.3（程序集 1.4.3.0，四处一致） |
| 测试类型 | 全流程 QA 收口：需求基线 + 风险设计 + 单元测试 + 静态审查 + 端到端冒烟 + 真实联网测速 + **缺陷修复回归** |
| 首轮执行 | 2026-09-30　**修复收口** | 2026-10-01 |
| 构建 | Debug、Release 两配置均 **0 错误 / 0 警告**（build-final.log） |
| 自动化测试 | **297 个方法：297 通过 / 0 失败 / 0 跳过**（修复前为 243 个、含 4 跳过；净增 54 个回归用例，4 个 Skip 全部转正） |
| 端到端冒烟 | smoke-test.ps1 驱动真实 Release 程序（含 requireAdministrator 清单）：**32 项全部 PASS / 0 失败**，debug.log 344 行 0 FATAL（smoke-final.log） |
| 修复后端到端 | final-e2e.ps1：**11/11 PASS**——交叉非法设置 400 且回滚不落盘、合法 200、取消测速记录带字节与峰值、防火墙规则真实存在（final-e2e.log） |
| 真实联网测速 | 修复后真机三模式：**下载峰值 1900.9 / 均值 1711.4 Mbps、上传峰值 262.9 / 均值 221.6 Mbps、双向明细 25 成功（修复前为 0 成功）**，三模式经 API 发起均正常落库 |
| 业务用例 | 41 个：**通过 35 · 阻塞 6（全部为需第二设备/GUI 的手动项）· 失败 0 · 跳过 0** |
| 缺陷处置 | **9 个缺陷全部已修复并回归通过（closed）**：P1×1、P2×5、P3×3，无 P0、无开放 S1/S2/S3 |
| **发布判断** | **conditional_go（有条件放行）**：代码级准出已满足，完成第 8 节手动/跨设备验收或显式接受残余风险后即可对外发布 |

---

## 1. 结论摘要

首轮 QA（2026-09-30）以构建/单测/静态审查/32 项冒烟/真实三模式测速锁定了 8 个正式缺陷；随后按《缺陷修复方案.md》分 7 批完成修复并做了真机回归，收口阶段又查明并修复了第 9 个缺陷（测速中取消导致历史三列为 0）。**截至 2026-10-01，9 个缺陷全部修复、回归通过并关闭。**

- **构建**：Debug/Release 0 错误 0 警告，版本号四处一致。
- **单元/逻辑层**：**297 个测试全部通过、0 失败、0 跳过**（首轮 4 个 `[Fact(Skip)]` 缺陷占位已全部转正；新增双向明细、API 持久化、设置钳制、防火墙探测、取消计数等专项回归）。
- **端到端冒烟**：修复后 requireAdministrator 的 Release exe 在管理员会话可无人值守启动，**32/32 全 PASS、0 FATAL**，设置 round-trips 仍 200、lanReady=True。
- **修复后端到端（final-e2e，11/11）**：
  - 设置交叉非法 `testTimeoutSec<averageDelaySec` 实测返回 **400**（中文错误说明 + rejected 标记），且**回滚不落盘**（GET 仍为原值 45/10）；合法提交返回 **200** 并持久化；
  - 测速进行中**取消**的下载记录（Id143，5.13s）`BytesDownloaded=589,269,888`、`PeakMbps=1261.92`，**不再为 0**；
  - 防火墙入站规则 `NetSpeedTest Web Server` 经 netsh 实测真实存在：Enabled / In / TCP / LocalPort 8080 / Allow。
- **真实联网三模式（批 1 修复后真机）**：下载峰值 1900.9、上传峰值 262.9 Mbps；**双向 URL 明细由修复前「0 成功」变为 25 成功 / 20 失败（maxBytes 4.75GB）**；经 Web API 发起的下载/上传/双向各落库 1 条、recentResult 非空。
- **缺陷清零**：8 个正式缺陷（含复盘升级为 P1 的 API-SAVE）与取消落库缺陷 BUG-CANCEL-009 全部 closed，无 P0、无开放 S1/S2/S3，阻断 Bug 集合为空。

**为何是 conditional_go 而非直接 go**：单元测试与本机回环冒烟覆盖不到**提权交互、跨设备放行、跨网关 MTU、多网卡物理聚合、GUI 像素/主题、CSV/HBCS 实物文件、安装包签名**——尤其 D8 选择 `app.manifest requireAdministrator` 后**每次启动 exe 都会弹 UAC**，必须在管理员与标准账户、第二台局域网设备上实测。这些登记在第 7、8 节与 manual_handoff，不阻断代码级准出，但对外发布前须完成或显式接受风险。

---

## 2. 测试范围与对象

覆盖仓库全部核心功能：测速引擎（下载/上传/双向、自适应并发、多网卡、UDP、DNS、取消）、数据（SQLite 历史/分页/CSV/节点/HBCS）、内置 Web 服务器与 API（鉴权、安全头、SSRF/穿越防护、生命周期、并发、防火墙、兜底页）、18 合 1 工具箱、自动更新、全局异常与并发安全。

不在范围：安装包代码签名、GitHub Release 发布流水线、广告/赞助内容、WPF 像素级 UI 走查与主题/多语言渲染。

---

## 3. 测试环境

| 项 | 值 |
|---|---|
| 操作系统 | Windows（真实桌面，非容器/沙箱），收口会话为管理员账户 |
| .NET SDK | 8.0.425；net8.0-windows（WPF），AnyCPU |
| 提权清单（D8①，新增） | app.manifest 为 requireAdministrator + supportedOS(Win8/8.1/10-11) + PerMonitorV2；已二进制核验 exe 含 requireAdministrator |
| 框架/工具 | xUnit（dotnet test）；HttpListener Web API；PowerShell 驱动脚本 |
| 端到端/真机 | 真实 Release `NetSpeedTest.exe --debug`，HTTP 127.0.0.1:8080，默认预设节点 |
| 数据隔离 | 单元测试用临时 SQLite 库并清理；冒烟/真机/端到端前后备份并还原本地 appsettings.json、web.json（备份存 config-backup/，不入库）；本轮产生的 22 条临时记录（Id122–143）已在备份数据库后删除，历史恢复 100 条 |

---

## 4. 执行总览

| 阶段 | 方式 | 结果 | 证据 |
|---|---|---|---|
| 构建（修复后） | dotnet build Release（整解决方案） | **0 错误 0 警告** | EVD-FIX-BUILD-015（build-final.log） |
| 全量单元测试（修复后） | dotnet test -c Release | **297：297 通过 / 0 失败 / 0 跳过** | EVD-FIX-UNIT-016（test-final.log） |
| 静态代码审查 | 通读核心源码/XAML/README | 锁定缺陷并逐一取证、修复后复核 | evidence\ 快照 |
| 端到端冒烟（修复后） | tools\smoke-test.ps1 | **32/32 PASS，0 FATAL（344 行）** | EVD-FIX-SMOKE-017（smoke-final.log） |
| **修复后端到端断言** | qa-results\final-e2e.ps1 | **11/11 PASS：交叉 400 回滚 / 取消带字节 / 防火墙规则** | **EVD-FIX-E2E-018（final-e2e.log）** |
| **修复后真机三模式** | qa-results\real-speedtest.ps1（批 1 回归） | **双向明细 25 成功、API 三模式落库、速率真实** | **EVD-FIX-REAL1-019 / EVD-FIX-REALHIST-020** |
| 首轮真实联网测速（缺陷发现） | qa-results\real-speedtest.ps1 | 下载 1829 / 上传 286 / 双向 0 成功，暴露 API-SAVE、印证 BIDI | EVD-REAL-E2E-012（归档对照） |

修复后真机关键数据（批 1 回归）：

| 模式 | 峰值下载 Mbps | 峰值上传 Mbps | 平均下载 | 平均上传 | URL 明细结果 |
|---|---|---|---|---|---|
| 下载 | 1900.9 | — | 1711.4 | — | 正常落库（BytesDownloaded/PeakMbps 有值） |
| 上传 | — | 262.9 | — | 221.6 | 正常落库（BytesUploaded/PeakMbps 有值） |
| 双向 | 下载/上传均起量 | — | — | — | **25 成功 / 20 失败（修复前为 0 成功），maxBytes 4.75GB，下载+上传明细合并并标 Direction** |

> 数据观察（非代码缺陷，沿用首轮）：下载阶段 30 个预设 URL 仅少数成功，实际带宽由可用节点承载，产品如实报告失败；建议清理/更新节点列表。

---

## 5. 分模块验证结论

| 模块 | 结论 | 依据 |
|---|---|---|
| 构建 / 版本 / 文档 | ✅ 通过 | 两配置 0/0；版本四处一致；README 与实现对应 |
| 测速引擎逻辑（自适应/URL 均衡/上传校验/网卡/UDP/重定向） | ✅ 通过 | 297 单测全绿，覆盖 P0/P1 机制与边界 |
| 真实外网下载/上传/双向速率 | ✅ 通过（真机闭环） | 首轮 + 修复后两度真机，峰值/均值/字节/曲线/平均窗口合理 |
| **双向测速 URL 明细/成功计数（BUG-BIDI-002）** | ✅ **已修复** | MergeFullUrlDetails 合并双向明细；12 单测 + 真机 25 成功 |
| Web 令牌 / 静态 / 安全（SSRF/安全头/穿越/错误体） | ✅ 通过 | 单测 + 冒烟真机双证 |
| Web 生命周期 / 设置持久化 / 并发可用性 | ✅ 通过 | 冒烟生命周期、20 次启停、8 并发写、90 并发轮询全 PASS |
| Web/服务端运行稳定性 | ✅ 通过 | 冒烟 + 端到端全程 0 FATAL/未观察异常 |
| **Web API 发起测速结果持久化（BUG-API-SAVE-008，P1）** | ✅ **已修复** | 持久化移出弹窗门控；3 单测 + 真机三模式各落库 1 条、recentResult 非空 |
| **设置 API 边界统一与交叉校验（BUG-CLAMP-003）** | ✅ **已修复** | SpeedOptionLimits 单一事实源；17 单测 + 端到端 400 回滚/200 |
| **防火墙自动放行（BUG-FW-006）+ D8① 提权** | ✅ **代码已修复（跨设备/UAC 手动）** | add 死代码修活、状态真实；16 单测 + netsh 规则 8080 Allow；跨设备与标准账户提权列入手动 |
| **兜底页令牌（BUG-FALLBACK-007）** | ✅ **代码已修复（跨设备手动）** | 兜底页补 X-NST-Token；WebSecurityTests 累计 27 项；第二设备渲染列入手动 |
| **MTU 跨网关探测（BUG-MTU-001）** | ✅ **代码已修复（跨网关真机手动）** | TTL 固定 64、术语纠正；专项单测转正；8.8.8.8 实测列入手动 |
| **带宽输入校验（BUG-BW-004）/ 子网掩码连续性（BUG-SUBNET-005）** | ✅ **已修复** | ToolboxCalculationTests 全部转正（21 项、0 Skip） |
| **测速中取消结果落库（BUG-CANCEL-009 / D10）** | ✅ **已修复（历史旧数据按用户决定保留）** | ApplyCancelledTrafficCounters；4 单测 + 端到端 Id143 带字节/峰值 |
| 历史分页 / 读 API | ✅ 通过 | 分页单测全绿；冒烟 /api/history 正常 |
| CSV / HBCS | ⛔ 手动待验 | 需 GUI 人工（051/052） |
| 跨设备白名单 / 兜底页渲染 | ⛔ 手动待验 | 需第二台设备（038、040） |
| 多网卡真实绑定聚合 | ⛔ 手动待验 | 需多上联（019） |
| 自动更新 | ✅ 通过 | 版本判定/资产/SHA256/降级单测全绿 |

---

## 6. 缺陷处置清单（9 个，全部已修复 closed）

| Bug ID | 模块 | S/P | 修复提交 | 回归测试/证据 | 状态 |
|---|---|---|---|---|---|
| BUG-API-SAVE-008 | Web·结果持久化 | S3/**P1**（复盘升级） | 5b8596a | ApiInitiatedPersistenceTests 3 + 真机三模式落库（EVD-FIX-REAL1-019/020） | ✅ closed |
| BUG-BIDI-002 | 测速·双向 | S3/P2 | 34f5e13 | UrlDetailReportingTests 12 + 真机 25 成功（EVD-FIX-REAL1-019） | ✅ closed |
| BUG-MTU-001 | 工具箱·MTU | S3/P2 | 348da05 | ToolboxCalculationTests 转正（EVD-FIX-UNIT-016） | ✅ closed |
| BUG-CLAMP-003 | Web·设置 API | S3/P2 | c5d8f72 | SettingsClampTests 17 + 端到端 400/200（EVD-FIX-E2E-018） | ✅ closed |
| BUG-FW-006 | Web·防火墙 | S3/P2 | 717d720 | FirewallProbeTests 16 + netsh 规则实测（EVD-FIX-E2E-018） | ✅ closed |
| BUG-CANCEL-009（D10） | 测速·取消落库 | S3/P2 | 8621e9e | CancelledResultCountersTests 4 + 端到端 Id143（EVD-FIX-E2E-018） | ✅ closed |
| BUG-BW-004 | 工具箱·带宽 | S4/P3 | bb93dfd | ToolboxCalculationTests 转正 | ✅ closed |
| BUG-SUBNET-005 | 工具箱·子网 | S4/P3 | bb93dfd | ToolboxCalculationTests 转正 | ✅ closed |
| BUG-FALLBACK-007 | Web·兜底页 | S4/P3 | 94988cf | WebSecurityTests 累计 27 项 | ✅ closed |

**修复批次与提交链（分支 main）**：34f5e13（BIDI）→ 5b8596a（API-SAVE 升 P1）→ 74c7790（批 1 真机证据）→ bb93dfd（BW+SUBNET）→ 348da05（MTU）→ c5d8f72（CLAMP）→ 94988cf（FALLBACK）→ 717d720（FW + app.manifest）→ 8621e9e（D10 取消落库）。每批均按 AGENTS.md 要求编写/更新测试并在交付前跑通、独立提交。

**D10（BUG-CANCEL-009）根因更正**：历史中 BytesDownloaded/BytesUploaded/PeakMbps 三列为 0 **并非旧版本迁移数据**，而是当前仍在的取消活路径 `FinishTestCancelled`——手工 new SpeedTestResult 只填 TotalBytes 与实时速率，漏填三列。库中实证 82 条（2026-09-05 至 09-29，下载 66 / 上传 8 / 双向 8）TotalBytes>0 却三列 0，而同期正常完成记录三列正常，二分坐实。修复后今后取消不再产生三列 0；**历史 82 条经用户 2026-10-01 决定一律不回填、保持现状**（双向 8 条字节无法可靠拆分、82 条峰值无法从均值还原，不伪造）。

---

## 7. 未验证范围（修复后残余，均为手动/跨设备项）

1. **D8① 提权（每次启动弹 UAC）**：需在管理员与标准（非管理员）两种账户分别启动 exe，确认授予/拒绝提权的行为与拒绝后的提示；本机管理员会话仅验证可无人值守启动。
2. **防火墙跨设备放行**：netsh 已实测规则存在（In/TCP/8080/Allow），但第二台局域网设备经 8080 的真实连通与写操作未测（TC-WEB-082 / TC-WEBACL-038）；回环不构成跨设备证据。
3. **兜底页跨设备**：移走 wwwroot 后在第二台同网段设备打开内嵌兜底页并执行写操作（开始/停止/保存设置）不再 403 未测（TC-WEB-040）。勘误：GET 不查令牌、全库无 401 返回点、令牌/网段失败实际返回 403、回环豁免令牌；早期 TC-WEB-036「无令牌 401」表述不准确，拒绝路径以第二设备现场为准。
4. **MTU 跨网关真机**：TTL 固定 64 的逻辑已由单测验证，对 8.8.8.8 等跨网关目标得到路径 MTU（期望 1500/1480）及同网段回归需真机执行（TC-MTU-062）。
5. **多网卡/多宽带**真实绑定、按指定网卡出口、流量聚合与持久化未在多上联环境实测（TC-NETSPEEDTEST-019）。
6. **CSV 导出、HBCS 往返、18 工具外网接口**（ip-api/ipify/STUN）人工冒烟未执行（TC-CSV-051 / TC-HBCS-052 / TC-NETSPEEDTEST-065）。
7. **WPF 像素走查、深/浅色主题、中英文渲染、GUI 手动测速结果窗 URL 明细、准备阶段取消**（TC-NETSPEEDTEST-020）未做 GUI 人工全量核对。
8. 安装包代码签名、GitHub Release 发布流水线、广告/赞助内容不在本轮范围。
9. **D10 历史数据**：82 条旧取消记录按用户决定保持现状、不回填（见第 6 节）。
10. **D10 口径观察（不阻断）**：取消记录 BytesDownloaded（下载进度回调）与 TotalBytes（合计回调）来自不同回调、时序口径不同，取消瞬间可能不相等（Id143 为 589,269,888 与 497,162,497），与正常完成记录既有两口径差异同源，本轮未统一统计口径。

---

## 8. 发布判断与放行条件

**判断：conditional_go（有条件放行）。**

- 代码级准出已满足：9 个缺陷全部修复并回归通过；Release 构建 0/0、**297 单测全过 0 跳过**、smoke 32/32、端到端 11/11、真机三模式落库与双向明细正常；阻断验收（构建/核心/安全/生命周期）4 项全部通过，P0 阻断 Bug 集合为空（与 release_decision.blocking_bug_ids 一致，均为空）。
- 尚不能直接 go：第 7 节手动/跨设备/GUI/签名项未执行，且 requireAdministrator 改变了启动交互（每次弹 UAC）。

**对外发布前需完成（条件、责任人与回滚已登记 manual_handoff）**：

1. 管理员与标准账户分别启动 exe，确认 UAC 提权与拒绝行为符合预期。
2. 第二台同网段设备访问 8080 并执行写操作，确认防火墙放行、网段白名单与令牌生效。
3. 移走 wwwroot 后在第二台设备打开兜底页并执行写操作，确认不再 403。
4. 对 8.8.8.8 等跨网关目标实测路径 MTU（1500/1480）并回归同网段。
5. 多网卡/多宽带真实绑定聚合与持久化；CSV 导出、HBCS 往返、外网工具人工冒烟。
6. GUI 结果窗 URL 明细、准备阶段取消、主题/多语言、安装包代码签名核对。

**监控**：保持 smoke 32/32、0 FATAL；全量测试 297 全绿；API 测速后历史总数恰好 +1、recentResult 非空；双向成功计数与实际传输一致；取消记录三列不再为 0。**回滚**：可按提交链逐个 git revert 回到 v1.4.3 基线；若不希望每次启动弹 UAC，可回退 717d720 的 app.manifest（同时失去自动提权，需用户手动以管理员运行防火墙放行）。

---

## 9. 本轮测试资产（修复后）

| 测试文件/脚本 | 用例数 | 覆盖点 |
|---|---|---|
| UrlDetailReportingTests.cs（新增） | 12 | 双向 URL 明细合并、Direction 标记、跨方向不去重（BIDI） |
| ApiInitiatedPersistenceTests.cs（新增） | 3 | API 发起结果落库与最近结果更新（API-SAVE） |
| ToolboxCalculationTests.cs（补全转正） | 21（0 Skip） | 子网/带宽/MTU 正确路径 + BW/SUBNET/MTU 缺陷回归 |
| SettingsClampTests.cs（新增） | 17 | SpeedOptionLimits 边界一致性、XAML 滑块↔常量正则、交叉非法（CLAMP） |
| WebSecurityTests.cs（增补） | 27 | 鉴权/安全头/兜底页令牌头（FALLBACK） |
| FirewallProbeTests.cs（新增） | 16 | 防火墙状态机、netsh 中英文本地化解析、端口边界（FW） |
| CancelledResultCountersTests.cs（新增） | 4 | 取消结果分方向字节/峰值补填、极早取消不伪造（D10） |
| final-e2e.ps1（新增） | 11 断言 | 交叉 400 回滚、取消带字节、防火墙规则真实存在 |
| smoke-test.ps1 / real-speedtest.ps1 | 32 / 三模式 | 端到端冒烟与真机三模式驱动 |

---

## 10. 证据清单

| 证据 ID | 等级 | 形式 | 位置 |
|---|---|---|---|
| EVD-FIX-BUILD-015 | L4 | 修复后构建日志 | build-final.log（0 错误 0 警告） |
| EVD-FIX-UNIT-016 | L4 | 修复后全量测试 | test-final.log（297 通过 / 0 失败 / 0 跳过） |
| EVD-FIX-SMOKE-017 | L4 | 修复后端到端冒烟 | smoke-final.log（32/32，0 FATAL） |
| EVD-FIX-E2E-018 | L4 | 修复后端到端断言 | final-e2e.log、final-e2e.ps1（11/11） |
| EVD-FIX-REAL1-019 | L3 | 修复后真机三模式 | real-speedtest-batch1.log、smoke-batch1.log |
| EVD-FIX-REALHIST-020 | L3 | 真机落库核对 | realhistory-clean.json（total/latest5） |
| EVD-REAL-E2E-012 等首轮证据 | L2–L4 | 缺陷发现期日志/源码快照 | real-speedtest.log、evidence\evidence-bug-*.txt、mtu-ttl*.txt（归档对照） |
| 配置备份 / 数据库备份 | — | 数据清理与还原 | config-backup\（不入库）；删除临时记录前的 DB 备份存于 QA 工作区（不入库） |

---

## 11. 验收检查结果（9 项：通过 6 · 阻塞 3，阻塞项均非阻断）

| 验收 ID | 验收项 | 阻断 | 结果 |
|---|---|---|---|
| AC-BUILD-001 | 可构建且版本/文档一致 | 是 | ✅ 通过 |
| AC-CORE-002 | 测速核心正确性 | 是 | ✅ 通过（含真机速率、双向明细修复） |
| AC-SEC-003 | 远程 Web 安全 | 是 | ✅ 通过（单测 + 冒烟双证） |
| AC-LIFE-004 | 生命周期/持久化/可用性 | 是 | ✅ 通过（含 API-SAVE、CLAMP 修复与端到端） |
| AC-UPDATE-007 | 更新检查与签名验证 | 否 | ✅ 通过 |
| AC-DEFECT-008 | 缺陷逐项复核定级与修复 | 否 | ✅ 通过（9 个缺陷全部 closed） |
| AC-DATA-005 | 历史与节点数据一致性 | 否 | ⛔ 阻塞（分页/节点/API 落库已通过；CSV/HBCS 待人工 051/052） |
| AC-TOOL-006 | 工具箱计算与输入校验 | 否 | ⛔ 阻塞（MTU/带宽/掩码已修；外网第三方工具 065 待人工冒烟） |
| AC-STAB-009 | 真实运行稳定性 | 否 | ⛔ 阻塞（Web/服务端 0 FATAL；GUI 准备阶段取消 020 待人工） |

> 阻断项 AC-BUILD-001 / AC-CORE-002 / AC-SEC-003 / AC-LIFE-004 全部通过；P0 阻断 Bug 集合为空，与 release_decision.blocking_bug_ids 一致。3 项阻塞均为非阻断、且原因全部是需第二设备/GUI 的手动验收项，故发布判断为 **conditional_go** 而非 no_go。

---

## 12. 本轮披露与勘误（相对首轮评审的更正）

1. 工具箱带宽/子网等校验实际位于 **ViewModels/MoreViewModel.cs**，不存在 ToolboxViewModel.cs。
2. BUG-FW-006 真实根因是 **`_firewallReady` 恒真 + add 防火墙规则为死代码**，不存在 `IsFirewallRuleNeeded` 判定；已按此修复。
3. 鉴权勘误：**GET 不查令牌、全库无 401 返回点、令牌/网段失败实际返回 403、回环豁免令牌**；首轮 TC-WEB-036「无令牌 401」表述不准确，跨设备拒绝路径需第二设备确认。
4. BUG-CLAMP-003 三套边界失配实际仅 **testTimeoutSec（下限 5 vs 10）与 threadRampUpMs（上限 5000 vs 2000）** 两个参数，其余 9 项一致。
5. MTU 提示语为**内联中文字符串、无资源 key**，按 D5 决策内联处理。
6. 子网掩码 **255.0.255.0 经位运算复核为 /16、主机数 65534**，首轮 QA 记录正确（子代理曾误判为 /8，已推翻，未据此修改）。
7. BUG-API-SAVE-008 的 ShowDialog 本体位于 MainViewModel 第 1017 行；持久化被误置于其门控内。
8. D10 根因为取消活路径 FinishTestCancelled（非旧版本迁移），详见第 6 节；历史 82 条按用户决定不回填。
9. D8① 代价：requireAdministrator 使**每次启动 NetSpeedTest.exe 都弹 UAC**，标准账户拒绝提权将无法启动。
10. 测试卫生：本轮 22 条临时记录（Id122–143）已备份后删除、历史恢复 100 条；appsettings.json（64/600/10/50）与 web.json（LastActualPort=8081）已还原用户原值；防火墙规则按 D8① 长期驻留、停止不删。
