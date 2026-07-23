# NetSpeedTest

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)](https://github.com/lingyingaojue/NetSpeedTest)
[![Release](https://img.shields.io/badge/release-v1.2.0-green)](https://github.com/lingyingaojue/NetSpeedTest/releases)

Windows 桌面端网络测速工具，支持 CDN 节点测速、全网卡并发、配置导入导出、历史记录管理。

基于 **.NET 8 + WPF + MVVM** 架构开发，MIT 开源协议。

![截图](assets/screenshot.png)

## 功能特性

### 测速引擎

- 🚀 **多线程下载测速** — 128 线程并发 HTTP GET，支持多 URL 轮询
- 📤 **多线程上传测速** — 128 线程并发 HTTP POST（64KB 随机数据块），失败自动换 URL
- 🔄 **全速双向测速** — 下载 + 上传同时进行，交替分配线程
- 📊 **NIC 计数器级速率** — 基于系统网卡 `IPv4Statistics` 差分计算，不受 HTTP 开销干扰
- ⏱️ **10 秒稳定后取均值** — 前 30 秒爬坡期排除，取满载后平均速率
- 📈 **3 秒滑动窗口平滑** — 实时速率去毛刺显示
- 🐢 **线程渐变启动** — 可配间隔（默认 200ms），避免瞬时占满带宽
- ⏰ **全局超时控制** — `CancellationTokenSource` 统一管理

### 延迟检测

- 🏠 **内网延迟（LAN）** — ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD 四层回退
- 🌍 **外网延迟（WAN）** — 12 个公网目标并发 Ping（百度、阿里云、腾讯、Google DNS 等），取最低值

### 实时图表

- 📉 **下载/上传速率折线图** — LiveCharts2，每 200ms 采样，保留最新 500 点
- ↔️ **可拖拽分割线** — 自由调整下载/上传图表比例
- 🎯 **按测试模式自动聚焦** — 下载测试 75% 下载图，上传测试反之，结束 50/50

### 结果展示

| 指标 | 数据来源 |
|------|----------|
| 测速时长 | Stopwatch + DispatcherTimer，超时自动冻结 |
| 下载速率 / 上传速率 | NIC 实时下行/上行 Mbps |
| 下载平均 / 上传平均 | NIC 累计平均（10s 后开始计） |
| 总速率 / 总均速 | 下载 + 上传 |
| 总流量 | NIC 总字节差分（已减基线） |
| 内网 / 外网延迟 | 网关 / 公网延迟 ms |
| 每网卡速率条 | 实时显示每个物理网卡速率 |
| 活跃线程计数 | 实时展示当前并发数 |

> 速率 < 1 Mbps 自动显示 Kbps，≥ 1000 Mbps 显示 Gbps。全数值 `InvariantCulture` 格式化。

### 测速配置

- 📦 **8 个内置节点** — 阿里云×2、腾讯云×2、华为云、电信、联通、移动
- ✏️ **自定义配置** — 新建/删除/重命名，每个配置独立管理下载/上传 URL
- 📥 **导入/导出** — JSON 格式（兼容 HBCS `.bin`），跨设备迁移

### 设置面板

| 选项 | 范围 | 默认 |
|------|------|------|
| 并发线程数 | 1–512 | 128 |
| 整体超时 | 10–300 s | 90 |
| 线程启动间隔 | 0–2000 ms | 200 |
| 平均计量延迟 | 1–30 s | 30 |
| 速率平滑窗口 | 0.5–10 s | 3.0 |
| 网卡轮询间隔 | 200–5000 ms | 1000 |

### 历史记录

- 📋 主窗口最近 20 条，测速结束即时插入
- 📄 历史窗口分页（每页 20 条），支持翻页、删除、双击看详情
- 🗄️ SQLite 持久化 `%LocalAppData%\NetSpeedTest\NetSpeedTest.db`

### 其他

- 📜 **首次启动 EULA** — 同意协议方可使用，随时可撤销
- 📝 **更新日志弹窗** — 按版本号检测，首次运行自动弹出
- 💾 **数据导出** — 最新测速结果导出 JSON
- 🎨 **暗色主题** — GitHub Dark 风格，多分辨率适配（默认 1280×700，最小 1000×600）

## 配置文件格式

支持导入 HBCS 格式的配置文件（`.bin` / `.json`）：

```json
{
  "format": "HBCS_SPEED_TEST_CONFIG",
  "version": 1,
  "profiles": [
    {
      "id": 1,
      "name": "示例配置",
      "downloadUrls": ["https://example.com/test_100mb.bin"],
      "uploadUrls": ["https://example.com/test_100mb.bin"]
    }
  ]
}
```

## 技术栈

| 技术 | 用途 |
|------|------|
| .NET 8.0-windows | 运行时 |
| WPF | UI 框架 |
| CommunityToolkit.Mvvm | MVVM 源码生成器 |
| LiveChartsCore.SkiaSharpView.WPF | 实时图表 |
| Microsoft.Data.Sqlite | 本地数据库 |
| Microsoft.Extensions.* | DI 容器、配置绑定、Options 模式 |

## 快速开始

### 环境要求

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 编译运行

```bash
git clone https://github.com/lingyingaojue/NetSpeedTest.git
cd NetSpeedTest
dotnet restore
dotnet build
dotnet run --project NetSpeedTest/NetSpeedTest.csproj
```

## 下载

前往 [Releases](https://github.com/lingyingaojue/NetSpeedTest/releases) 下载已编译版本（`.NET 8` 单文件发布，168 MB）。

## 许可证

[MIT License](LICENSE) © 2026 lingyingaojue
