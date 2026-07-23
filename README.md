# NetSpeedTest

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)](https://github.com/lingyingaojue/NetSpeedTest)
[![Release](https://img.shields.io/badge/release-v1.1.1-green)](https://github.com/lingyingaojue/NetSpeedTest/releases)

Windows 桌面端网络测速工具，支持 CDN 节点测速、配置导入导出、历史记录管理。

基于 **.NET 8 + WPF + MVVM** 架构开发，MIT 开源协议。

![截图](assets/screenshot.png)

## 功能特性

- 🚀 **一键测速** — 选择配置和 URL，一键完成下载/上传带宽检测，实时速率波形图
- 📡 **网络质量检测** — Ping 延迟、抖动、丢包率综合评估
- 🌐 **CDN 节点测速** — 支持任意 HTTP 可达地址，利用 CDN 文件进行下载测速，POST 上传无需专用端点
- 🎛️ **配置管理** — 自由创建、编辑、删除测速配置（Profile），每个配置可包含多个下载/上传 URL
- 📂 **HBCS 格式兼容** — 支持导入/导出 HBCS 测速配置文件（v1/v2），与主流安卓测速软件互通
- 📋 **历史记录** — 每次测速结果自动保存到本地 SQLite，支持分页查看、删除
- 🖥️ **网卡识别** — 自动识别物理网卡，展示 IP、网关、协商速率等信息
- 🎨 **深色主题** — 现代化深色 UI，支持 Windows DPI 缩放

## 技术栈

| 技术 | 说明 |
|------|------|
| .NET 8 | 目标框架 |
| WPF | 桌面 UI 框架 |
| CommunityToolkit.Mvvm | MVVM 架构 |
| LiveCharts2 | 实时速率波形图 |
| SQLite | 本地数据持久化 |
| System.Net.Http | HTTP 测速引擎 |

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

### 使用步骤

1. 启动后自动加载本机网卡信息
2. 点击「测速配置管理」导入 `.bin` / `.json` 配置文件，或新建配置
3. 在主界面选择配置和下载 URL
4. 点击「开始测速」
5. 查看实时波形和测速结果卡片

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

> 💡 上传测速原理：HTTP POST 发送数据时，速率在客户端即可计算，不依赖服务器接受文件。因此任意可达 URL 均可用于上传测速。

## 许可证

[MIT License](LICENSE) © 2026 lingyingaojue
