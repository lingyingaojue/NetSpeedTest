<div align="center">

<img src="assets/app-icon.png" width="96" />

# NetSpeedTest

**Windows 桌面端网络测速工具**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)](https://github.com/lingyingaojue/NetSpeedTest)
[![Release](https://img.shields.io/badge/release-v1.3.3-green)](https://github.com/lingyingaojue/NetSpeedTest/releases)
<br>
[![Stars](https://img.shields.io/github/stars/lingyingaojue/NetSpeedTest?color=yellow)](https://github.com/lingyingaojue/NetSpeedTest/stargazers)
[![Downloads](https://img.shields.io/github/downloads/lingyingaojue/NetSpeedTest/total?color=blue)](https://github.com/lingyingaojue/NetSpeedTest/releases)
[![Last Commit](https://img.shields.io/github/last-commit/lingyingaojue/NetSpeedTest)](https://github.com/lingyingaojue/NetSpeedTest/commits)
[![Repo Size](https://img.shields.io/github/repo-size/lingyingaojue/NetSpeedTest)](https://github.com/lingyingaojue/NetSpeedTest)

</div>

---

<p align="center">
  <b>CDN 动态调度</b> &nbsp;·&nbsp;
  <b>自适应线程调度</b> &nbsp;·&nbsp;
  <b>掉速智能补偿</b> &nbsp;·&nbsp;
  <b>抖动延迟监测</b> &nbsp;·&nbsp;
  <b>系统托盘驻留</b>
</p>

---

![Screenshot](assets/screenshot.png)

---

## ✨ 核心亮点

<table>
<tr>
<td width="50%">

### 🚀 智能测速引擎
- **128 线程并发** HTTP GET/POST
- **URL 动态调度** — SelectBestUrl 按实时速度选最优节点
- **自适应线程上限** — 根据 CPU 性能动态调整，低配不反噬
- **掉速紧急补偿** — 检测骤降 → 自动加线程 → 结果修正
- **10s 稳定后取均值**，排除爬坡干扰
- **3s 滑动窗口平滑**，实时速率去毛刺
- **渐变启动**（200ms 可配），避免瞬时占满带宽

### 📊 NIC 级精准计量
- 基于 `IPv4Statistics` 差分计算，不受 HTTP 开销干扰
- 实时上下行 Mbps 显示，自动切换 Kbps / Mbps / Gbps
- **多网卡信息卡** — 9 项详情，彩色分类布局
- 每网卡独立速率条 + 活跃线程计数

</td>
<td width="50%">

### 🎨 专业交互体验
- **LiveCharts2 双折线图**，200ms 采样，500 点窗口
- 下载/上传图表**可拖拽分割线**
- 模式切换 **300ms 平滑过渡动画**
- **测速互斥回调** — 单测时自动隐藏无关指标
- **系统托盘驻留** — 右键菜单 / 状态联动 / 气泡通知
- **键盘快捷键** — Enter 测速 / Esc 停止 / Ctrl+D/U/B 切换
- 完成弹窗 **「复制结果」按钮**
- **暗色主题** — GitHub Dark 风格

### 🌐 全链路延迟检测
- **内网延迟** — UDP → ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD 五层回退
- **外网延迟** — 12 公网目标并发 + 异步动态 DNS 解析，取最低值
- **平均抖动延迟** — 固定 8.8.8.8 单次 ICMP + 滑动窗口标准差，独立主机/间隔可配

### 🗄️ 历史 & 配置管理
- SQLite 持久化，独立页面 + 一键清除 + **统计栏** + CSV 导出
- 8 个内置 CDN 节点，支持自定义配置导入/导出（JSON 兼容 HBCS）

</td>
</tr>
</table>

---

## ⚙️ 可调参数

| 参数 | 范围 | 默认值 |
|:-----|:----:|:------:|
| 并发线程数 | 1 – 512 | **128** |
| 整体超时 | 10 – 600 s | **60 s** |
| 线程启动间隔 | 0 – 2000 ms | **500 ms** |
| 平均计量延迟 | 1 – 30 s | **10 s** |
| 速率平滑窗口 | 0.5 – 10 s | **3.0 s** |
| 网卡轮询间隔 | 200 – 5000 ms | **1000 ms** |
| 抖动探测主机 | — | **8.8.8.8** |
| 抖动采样间隔 | 1 – 60 s | **5 s** |

---

## 🛠 技术架构

| 层级 | 技术 |
|:-----|:-----|
| 运行时 | .NET 8.0‑windows |
| UI 框架 | WPF |
| 架构模式 | MVVM（CommunityToolkit.Mvvm 源码生成器） |
| 实时图表 | LiveChartsCore.SkiaSharpView.WPF |
| 数据持久化 | Microsoft.Data.Sqlite |
| DI & 配置 | Microsoft.Extensions.* |

---

## 🚀 快速开始

```bash
git clone https://github.com/lingyingaojue/NetSpeedTest.git
cd NetSpeedTest
dotnet restore
dotnet build
dotnet run --project NetSpeedTest/NetSpeedTest.csproj
```

> **环境要求**：Windows 10 / 11 · [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## 📥 下载

前往 [Releases](https://github.com/lingyingaojue/NetSpeedTest/releases) 下载已编译版本（.NET 8 单文件发布，~170 MB）。

---

## 📄 许可证

[MIT License](LICENSE) &nbsp;·&nbsp; © 2026 lingyingaojue
