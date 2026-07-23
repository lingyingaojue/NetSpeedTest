# NetSpeedTest

Windows 平台网络测速工具，基于 .NET WPF 构建。支持实时监测网络上传/下载速率、延迟检测和丢包率追踪。

## 功能特性

- 🚀 **全速测试** — 多线程并发下载/上传，准确测量带宽上限
- 📊 **实时图表** — 速率曲线实时绘制，直观展示测试过程
- 🏓 **延迟检测** — 多目标 Ping 测试，精准测量网络延迟
- 📉 **丢包率** — 自动统计丢包情况
- 📋 **历史记录** — 测试结果自动保存，支持历史回溯
- 🎛️ **多配置** — 支持自定义测试配置，适配不同场景
- 🖥️ **多分辨率** — 适配 1280×700（默认）/ 1000×600（最小）等多种分辨率
- 🎨 **WPF 原生界面** — 流畅的 Windows 原生体验

## 技术栈

| 技术 | 说明 |
|------|------|
| .NET (WPF) | 桌面 UI 框架 |
| C# | 主要开发语言 |
| XAML | 界面布局 |
| MVVM | 架构模式 |

## 项目结构

```
NetSpeedTest/
├── Converters/     # 数据转换器（速率、延迟、字节显示等）
├── Helpers/        # 工具类
├── Models/         # 数据模型（网络适配器、测速结果等）
├── Resources/      # 样式资源
├── Services/       # 核心服务（测速、数据、日志等）
├── ViewModels/     # 视图模型（MVVM）
├── Views/          # 界面窗口
└── NetSpeedTest.slnx  # 解决方案文件
```

## 构建

1. 安装 [.NET SDK](https://dotnet.microsoft.com/download)
2. 克隆仓库：
   ```bash
   git clone https://github.com/lingyingaojue/NetSpeedTest.git
   ```
3. 构建项目：
   ```bash
   dotnet build NetSpeedTest.slnx
   ```
4. 运行：
   ```bash
   dotnet run --project NetSpeedTest
   ```

## 下载

前往 [Releases](https://github.com/lingyingaojue/NetSpeedTest/releases) 页面下载最新编译版本。

## 许可证

MIT License © 2026 lingyingaojue
