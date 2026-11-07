# NetSpeedTest 仓库视觉规范（NST Design System）

本文件是仓库对外呈现的**单一模板**：README（中/英）、GitHub Pages 官网、应用截图、社区规范文件
都必须遵循这里的色板、排版、章节与文案规则。改版或发版时先读本文件，再改内容。

> 参考模板：**DevToys-app/DevToys**（仓库组织与社区文件）、
> **othneildrew/Best-README-Template**（README 章节顺序）、程序自身的深/浅主题（配色与截图口径）。

---

## 1. 品牌与色板

所有颜色来自程序主题，禁止官网另起一套色板。

| 语义 | 深色（默认） | 浅色 | CSS 变量 |
|:--|:--|:--|:--|
| 页面背景 | `#1C1C1E` | `#F3F3F7` | `--bg` |
| 抬升背景 | `#252528` | `#FCFCFE` | `--bg-elev` |
| 卡片 | `#2C2C30` | `#FFFFFF` | `--bg-card` |
| 描边 | `#3A3A40` | `#E3E3E8` | `--border` |
| 弱描边 | `#2C2C30` | `#F0F0F4` | `--border-soft` |
| 主操作 | `#238636` | `#1F883D` | `--primary` |
| 主操作悬停 | `#1E7A33` | `#166B2E` | `--primary-hover` |
| 强调/链接 | `#4CC2FF` | `#0067B8` | `--accent` |
| 强调柔光 | `rgba(76,194,255,.14)` | `rgba(0,103,184,.10)` | `--accent-soft` |
| 下载曲线 | `#4CC2FF` | `#0067B8` | `--download` |
| 上传曲线 | `#FF7AC3` | `#C2397F` | `--upload` |
| 成功 | `#34C759` | `#1A7F37` | `--success` |
| 警告 | `#FFD60A` | `#B25000` | `--warning` |
| 危险 | `#FF453A` | `#C62828` | `--danger` |
| 正文 | `#F5F5F7` | `#1A1A1E` | `--text` |
| 次要文字 | `#C7C7CC` | `#55555C` | `--text-2` |
| 弱化文字 | `#A0A0A8` | `#5F6B7A` | `--text-3` |

> 主操作用程序 Hero 按钮的绿色，强调用侧边栏高亮的蓝色，上传用图表粉色 —— 官网与截图同源。

徽章统一 `style=flat-square`，固定 7 枚且顺序不变：
`Release(v{ver})` · `License(MIT)` · `Platform(Windows)` · `.NET 8` · `Downloads` · `Stars` · `AI(DeepSeek)`。

## 2. 字体与排版

- 无衬线：`"Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", "PingFang SC", "Noto Sans SC", system-ui, sans-serif`
- 等宽：`"Cascadia Code", "JetBrains Mono", Consolas, monospace`
- 阶梯：Hero `clamp(2.5rem, 5vw, 3.75rem)/1.08`、H2 `clamp(1.75rem, 3vw, 2.25rem)/1.2`、
  H3 `1.25rem/1.4`、正文 `16px/1.7`、说明 `13px/1.6`、kicker `12px` 大写 `letter-spacing:.14em`
- 字重：标题 650，正文 400，强调 600；正文最大宽度 68ch。
- 中文与英文混排时中英文之间留空格（`128 线程`、`.NET 8 单文件发布`）。

## 3. 空间、形状与动效

- 间距 4pt 体系：4 / 8 / 12 / 16 / 24 / 32 / 48 / 64 / 96；容器 1160px。
- 区块上下内边距 96px（≤768px 时 64px）；卡片内边距 24px。
- 圆角：小 10px、中 16px、大 24px；按钮 10px；胶囊 999px。
- 阴影：`0 1px 2px rgba(0,0,0,.24)` / `0 16px 40px rgba(0,0,0,.28)` / `0 28px 70px rgba(0,0,0,.36)`。
- 过渡 180ms `cubic-bezier(.2,.7,.3,1)`；滚动显现 600ms、位移 12px；`prefers-reduced-motion` 下全部关闭。

## 4. 章节模板（canonical）

`data-section` 与 H2 文案必须与下表一致（README 的中/英 H2 必须包含对应文案）。

| key | 中文 H2 | English H2 | 官网 |
|:--|:--|:--|:--|
| features | 核心特性 | Core Features | ✓ |
| screenshot | 界面预览 | Screenshots | ✓ |
| api | Web 控制台与 REST API | Web Console & REST API | ✓ |
| toolbox | 网络工具箱 | Network Toolbox | ✓ |
| reference | 快捷键与参数 | Shortcuts & Parameters | ✓ |
| data | 数据与配置文件 | Data & Configuration Files | — |
| architecture | 技术架构 | Tech Stack | ✓ |
| quickstart | 快速开始 | Quick Start | — |
| download | 下载 | Download | ✓ |
| changelog | 更新日志 | Changelog | ✓ |
| faq | 常见问题 | FAQ | — |
| contributing | 参与贡献 | Contributing | — |
| credits | 开发方式与 AI 协作 | How This Project Is Built & AI Collaboration | — |
| contact | 反馈与联系 | Feedback & Contact | ✓ |
| license | 许可证 | License | — |

官网导航：特性 `#features` · 预览 `#screenshot` · 工具箱 `#toolbox` · 参数 `#reference` ·
接口 `#api` · 架构 `#architecture` · 更新日志 `#changelog` · 下载 `#download`。

## 5. 六张特性卡（README 与官网同名同内容）

| # | 中文 | English |
|:--|:--|:--|
| 1 | 智能测速引擎 | Smart Speed Engine |
| 2 | 多网卡并行测速 | Multi-NIC Parallel Testing |
| 3 | 全链路延迟探测 | Full-Chain Latency Probing |
| 4 | NIC 级精准计量 | NIC-Level Precision Metering |
| 5 | 专业交互体验 | Professional Interaction |
| 6 | 历史与配置管理 | History & Profile Management |

每张卡固定 5 条要点（中英一一对应），措辞以本表下的内容为准：

1. **智能测速引擎**
   - 128 线程并发 HTTP GET / POST，自适应模式最高 1024
   - URL 轮转调度 + 健康度评分，失败节点自动冷却降权
   - 掉速紧急补偿：自动加线程并修正结果
   - 跳过爬坡取均值 + 3 秒滑动窗口去毛刺
   - 渐变启动，避免瞬间占满带宽
2. **多网卡并行测速**
   - 勾选多张网卡同时测速，各自绑定源 IP
   - 默认优先选中带默认网关的网卡
   - 下载 / 上传曲线支持「合计 / 单网卡」切换
   - 单卡失败不影响其余网卡出结果
   - 虚拟网卡默认过滤，可在设置中放行
3. **全链路延迟探测**
   - 五层回退：UDP → ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD
   - 外网延迟、抖动、内网延迟共用同一探测链路
   - 抖动按滑动窗口标准差实时平滑
   - 丢包率 5 包 / 批轮询，首轮全失败自动 UDP 复核
   - 每张网卡独立呈现延迟、抖动与丢包率
4. **NIC 级精准计量**
   - 基于 IPv4Statistics 差分计算，排除 HTTP 协议开销
   - Kbps / Mbps / Gbps 自动切换
   - LiveCharts2 双折线图，200 ms 采样、500 点滑动窗口
   - 每网卡 9 项详情信息卡 + 活跃线程计数
   - 下载 / 上传图表可拖拽分割线，模式切换带过渡动画
5. **专业交互体验**
   - 自绘标题栏 + 侧边栏内嵌页，测速中禁用页面切换
   - 深 / 浅主题与中英双语即时切换并持久化
   - 系统托盘驻留、状态联动与气泡通知
   - GitHub OTA 在线升级，启动自动检查
   - 快捷键 Enter / Esc / Ctrl+D / Ctrl+U / Ctrl+B
6. **历史与配置管理**
   - SQLite 持久化历史，分页、统计栏与 CSV 导出
   - 8 个内置 CDN 节点，自定义配置导入 / 导出（兼容 HBCS）
   - 设置原子写入，异常中断不丢配置
   - 内置 Web 服务器 + REST API 远程控制
   - 多网卡导出包含聚合结果、每卡明细与 BatchId

英文对照（README.en 与官网 i18n 使用）：

1. 128 threads of concurrent HTTP GET/POST, up to 1024 in adaptive mode
   · URL rotation with health scoring; failing nodes cool down and get down-weighted
   · Emergency drop compensation adds threads and corrects the result
   · Ramp-up excluded from the average, 3 s sliding-window smoothing
   · Gradual startup so the link is never saturated instantly
2. Test several NICs at once, each bound to its own source IP
   · Default-gateway adapters are selected automatically
   · Download/upload charts switch between combined and per-NIC curves
   · One failing adapter never blocks the others
   · Virtual adapters are filtered by default and can be allowed in Settings
3. Five-tier fallback: UDP → ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD
   · Internet latency, jitter and LAN latency share one probe chain
   · Jitter is smoothed with a sliding-window standard deviation
   · Packet loss is probed in 5-packet batches with an automatic UDP recheck
   · Every adapter shows its own latency, jitter and packet loss
4. Differentially derived from IPv4Statistics, free of HTTP overhead
   · Kbps / Mbps / Gbps switch automatically
   · Dual LiveCharts2 line charts, 200 ms sampling, 500-point window
   · Nine-detail info card per adapter plus active thread counts
   · Draggable chart splitter and animated mode transitions
5. Custom title bar with embedded sidebar pages; switching is locked while testing
   · Dark/light themes and Chinese/English switch instantly and persist
   · System-tray residency with state sync and balloon notifications
   · GitHub OTA updates checked automatically at startup
   · Shortcuts: Enter / Esc / Ctrl+D / Ctrl+U / Ctrl+B
6. SQLite history with paging, a statistics bar and CSV export
   · 8 built-in CDN nodes plus profile import/export (HBCS-compatible JSON)
   · Atomic settings writes, so an interrupted save never loses configuration
   · Built-in web server and REST API for remote control
   · Multi-NIC export carries the aggregate result, per-adapter details and BatchId

## 6. 统计数字（Hero 数据条）

| 数字 | 中文说明 | English |
|:--|:--|:--|
| 128 | 默认并发线程 | Default threads |
| 1024 | 自适应线程上限 | Adaptive thread ceiling |
| 18 | 网络诊断工具 | Diagnostic tools |
| 8 | 内置 CDN 节点 | Built-in CDN nodes |

## 7. 事实表（必须与源码一致，测试会校验）

### 7.1 可调参数（README 主文档与官网 `#paramTable` 共用）

范围来自 `Views/SettingsWindow.xaml` 滑块，默认值来自 `NetSpeedTest/appsettings.json`。

| data-param | 中文标签 | English label | 范围 | 默认 |
|:--|:--|:--|:--|:--|
| ThreadCount | 并发线程数 | Concurrent threads | 2 – 1024 | 128 |
| TestTimeoutSec | 测速时长 | Test duration | 10 – 600 s | 60 s |
| ThreadRampUpMs | 线程启动间隔 | Thread ramp-up interval | 0 – 2000 ms | 50 ms |
| AverageDelaySec | 平均计量延迟 | Average measurement delay | 1 – 30 s | 10 s |
| RateWindowSec | 速率平滑窗口 | Rate smoothing window | 0.5 – 10 s | 3.0 s |
| NicPollIntervalMs | 网卡轮询间隔 | NIC poll interval | 200 – 5000 ms | 1000 ms |
| LatencyPollIntervalMs | 延迟轮询间隔 | Latency poll interval | 500 – 10000 ms | 1000 ms |
| JitterPollIntervalMs | 抖动采样间隔 | Jitter sample interval | 500 – 5000 ms | 1000 ms |
| JitterTargetHost | 抖动探测主机 | Jitter target host | — | 8.8.8.8 |
| PacketLossTargetHost | 丢包率监测地址 | Packet loss target host | — | 8.8.8.8 |
| PacketLossPollIntervalMs | 丢包率轮询间隔 | Packet loss poll interval | 500 – 5000 ms | 1000 ms |
| CompensationThreshold | 补偿阈值 | Compensation threshold | 0.3 – 0.8 | 0.5 |
| CompensationConfirmSec | 确认时长 | Compensation confirm window | 1 – 10 s | 3 s |
| AdaptiveStartThreads | 自适应起始线程数 | Adaptive start threads | 1 – 自适应上限 | 2 |
| (derived) | 自适应线程上限 *(派生)* | Adaptive thread ceiling *(derived)* | 128 / 256 / 512 / 1024 阶梯 | 1024 |
| (port) | Web 服务器端口 | Web server port | 1024 – 65535 | 8080 |

> 官网参数表把「自适应起始线程数」的范围写作 `1 – 1024`（派生上限的默认值），中英文一致；README 保留「1 – 自适应上限」的语义写法。其余行三处（README 中英 / 官网）必须完全一致。

### 7.2 网络工具箱（18 项，中文名用于 README/官网，英文名取自 `Strings.en-US.xaml`）

Ping · DNS 查询 · HTTP 请求 · 路由追踪 · 端口测试 · MTU 探测 · DNS 对比 · IP 归属 · 公网 IP ·
SSL 证书 · HTTP Header · 子网计算 · 带宽换算 · 时间戳 · 文本哈希 · Base64 · UUID 生成 · NAT 检测

英文：Ping · DNS Query · HTTP Request · Traceroute · Port Test · MTU Probe · DNS Compare · IP Geo ·
Public IP · SSL Certificate · HTTP Header · Subnet Calculator · Bandwidth · Timestamp · Hash ·
Base64 · UUID · NAT Detection

### 7.3 REST API（README 与官网接口小表共用，共 12 行 / 9 条路由）

`GET /api/status` · `GET /api/adapters` · `POST /api/adapters/select` · `GET /api/profiles` ·
`POST /api/profiles` · `GET /api/history` · `DELETE /api/history` · `GET /api/settings` ·
`POST /api/settings` · `POST /api/test/start` · `POST /api/test/stop` · `GET /api/server`

## 8. 官网 DOM 契约（测试按此校验，实现不得改名）

- `<html lang="zh-CN" data-theme="dark" data-lang="zh">`
- 版本：`<span class="version-chip" id="siteVersion">v1.4.4</span>`；
  hero 徽标文本包含 `v1.4.4`；更新日志 3 个 `<span class="version-tag">v1.4.x</span>`（最新在前）。
- 章节：`<section id="features" data-section="features">`，
  标题 `<h2 class="section-title" data-i18n="section.features.title">核心特性</h2>`。
- 参数表：`<table id="paramTable">`，每行
  `<tr data-param="ThreadCount"><td data-i18n="param.ThreadCount">并发线程数</td><td class="range">2 – 1024</td><td class="default">128</td></tr>`。
- 工具箱：`<div class="tool-grid" id="toolboxGrid" data-toolbox data-count="18">`，
  每项 `<div class="tool-item" data-tool="Ping">`。
- 计数器：`<span class="counter" data-target="128">128</span>`（HTML 内直接写最终值，JS 再从 0 播放动画）。
- i18n：中文文案直接写在 HTML 作为默认语言（无 JS 时可用），英文字典内嵌为
  `<script type="application/json" id="i18n-data">{"en":{...}}</script>`，键为点分小写；
  文字节点用 `data-i18n="key"`，富文本用 `data-i18n-html="key"`，属性用 `data-i18n-aria` / `data-i18n-title`。
  JS 首次切换语言时缓存中文原文，切回中文即还原；`meta.title` / `meta.description` 也走字典。
- 页脚：`<p class="footer-credit">凌影傲爵 · DeepSeek</p>`（`VersionConsistencyTests` 依赖该 class）。
- 资源全部本地（无 CDN、无外部字体、无外链脚本）；favicon 使用 `assets/site-icon.svg`。

## 9. README 契约（`README.md` / `README.en.md`）

1. 头部居中：`<div align="center">` + 图标 `<img src="assets/app-icon.png" width="96" />` + 双语切换
   （`[English](README.en.md)` / `[中文](README.md)`）+ H1 + 一句话定位 + 7 枚徽章 + 入口行。
2. 徽章必须包含 `release-v{版本}`；更新日志一节必须包含 `当前版本 **v{版本}**`（英文 `Current version: **v{版本}**`）。
3. 工具箱一节必须包含 `内置 **18 种专业网络诊断工具**` 与 `<b>18 合 1 工具箱</b>`
   （英文 `<b>18-in-1 Toolbox</b>`，且 18 个英文工具名全部出现）。
4. 署名一节标题固定 `## 🤖 开发方式与 AI 协作`，正文固定 `**本项目使用 DeepSeek AI 制作。**`，
   表格固定 `| AI 协作 | DeepSeek |`。
5. 目录：只允许 TOC 使用 `- [标题](#锚点)` 形式的行；标题文本必须与某个 H2/H3 完全一致，
   锚点必须符合 GitHub 规则（emoji 被去掉，U+FE0F 保留）。命令：
   `dotnet test --filter "FullyQualifiedName~ReadmeDocumentationTests|FullyQualifiedName~ReadmeBilingualTests"`
6. 中英结构镜像：H2/H3 数量、表格内容行、表格分隔行、列表项、代码围栏、Markdown 图片、
   `<img>` 标签数量必须逐项相等；避免使用 HTML `<table>`（两侧都为 0 也算相等）。
7. 参数表：3 列（标签 / 范围 / 默认），范围写「min – max」两个数字，默认值第一个数字与
   `appsettings.json` 一致；标签必须以上表 7.1 的中/英文开头。
8. API 表：以反引号包裹全部 9 条 `/api/...` 路由，中英集合一致。
9. 编码：UTF-8 无 BOM、单个结尾换行、CRLF（`tools/verify-docs.ps1` 校验）。
10. 所有本地链接与图片必须存在：`assets/app-icon.png`、`assets/screenshot.png`、
    `LICENSE`、`CHANGELOG.md`、`CONTRIBUTING.md`、`SECURITY.md`、`CODE_OF_CONDUCT.md`。

## 10. 文案规范

- 中文为主、英文镜像；术语与程序本地化一致（「下载测速 / 上传测速 / 双向测速」「网卡」「节点」）。
- 数字与单位统一：`128 线程`、`60 s`、`1000 ms`、`Mbps`、`约 170 MB`。
- 承诺可验证的事实，不写无法证伪的形容词；同一事实只在一处定义，其余引用。
- 安全相关表述使用「会话令牌」「同网段访问控制」「SSRF 防护」等固定术语。

## 11. 发版检查清单

1. 改 `NetSpeedTest.csproj` 的 `<Version>`。
2. README 徽章、README 更新日志、CHANGELOG、关于页、官网版本胶囊与更新日志同步。
3. 重拍截图：`pwsh tools/capture-screenshots.ps1`（本机无 pwsh 时用 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File`）。
4. `dotnet test`、`pwsh tools/verify-docs.ps1` 全绿。
5. 推送后核对 GitHub Pages 与仓库页渲染。

> 官网支持 `?lang=zh|en`、`?theme=dark|light`、`?motion=off` 三个查询参数，分别用于语言/主题/关闭动画，
> 截图与自动化测试应带上 `?motion=off` 以获得稳定画面。
