using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using NetSpeedTest.Services;

namespace NetSpeedTest.Views;

public partial class AboutPage : UserControl
{
    public List<ChangelogEntry> Changelog { get; } = new();

    public AboutPage()
    {
        InitializeComponent();
        DataContext = this;

        var config = ((App)Application.Current).GetService<IConfiguration>();
        var ad = config.GetSection("Advertising");
        SponsorNameText.Text = ad["SponsorName"] ?? "暂无";
        SponsorDetailText.Text = ad["SponsorDetail"] ?? "";

        Changelog.Add(new ChangelogEntry("V1.3.7", "2026-08-10", new()
        {
            "🚀 重大更新",
            "● 界面全面重构：历史/配置/设置/更多功能/用户协议/关于 改为右侧内嵌页面（不再弹窗）",
            "● 自绘标题栏（Windows 风格：最小化/最大化/关闭按钮）",
            "● 侧边栏导航（测速/历史/配置/设置/更多/协议/关于），点击\"测速\"返回主视图",
            "● 测速中侧边栏页面按钮自动禁用",
            "● 新增启动广告弹窗（赞助商广告，横板布局，图片自适应）",
            "● 设置页新增「广告」分类：关闭广告 7 天 + 剩余天数显示",
            "● 新增 GitHub OTA 在线升级（启动自动检查 + 关于页手动检查，发现新版本弹窗引导下载）",
            "● 关于页全新改版（Hero 区/信息卡/GitHub·邮箱可点击/更新日志分类着色/广告位）",
            "● 用户协议升级 4.0（隐私条款完善、未成年人条款、跨境单独同意、赞助商广告声明）",
            "🐛 修复",
            "● 修复空闲/测试双状态切换失效（BoolInverter 无法转换 Visibility → DataTrigger 修复）",
            "● 修复导入配置内网 URL 误过滤（DNS 解析失败不再拦截）",
            "● 修复历史记录按钮文字截断与对比度（深色文字 + 加宽按钮）",
            "● 修复快捷键在文本框内误触发测速",
            "● 修复 SQLite 迁移失败（reader 未关闭执行 ALTER）",
            "● 修复全测速下载 404 页面按成功计字节",
            "● 修复 IPv4-mapped IPv6 绕过私网拦截",
            "● 修复上传进度字节恒为 0",
            "● 修复检查更新版本号误报\"已是最新\"",
            "● 修复设置保存异常导致测速状态卡死",
            "🔧 优化",
            "● 文字高对比度配色（正文/次要/弱化全部达标 WCAG AA）",
            "● 选中项对比度修复（亮蓝底 + 深色文字）",
            "● 上传测速线程启动间隔 500ms→50ms（避免超时前线程未就绪）",
            "● 广告图片加载失败记录日志",
            "● 启动自动检查更新延迟 3 秒避免与广告窗冲突",
        }));

        Changelog.Add(new ChangelogEntry("V1.3.6", "2026-07-29", new()
        {
            "🐛 修复",
            "● 修复首次启动同意 EULA 后主窗口不显示（EulaWindow 误为主窗口 → 显式指定 MainWindow）",
            "● 修复托盘\"退出\"后进程变僵尸（OnExplicitShutdown → OnMainWindowClose + ForceClose 统一路径）",
            "● 修复 STUN 事务 ID 永为 0（GetItems<byte> 从全零数组选元素 → RandomNumberGenerator.Fill）",
        }));

        Changelog.Add(new ChangelogEntry("V1.3.5", "2026-07-28", new()
        {
            "🐛 修复",
            "● 修复 DNS 重绑定 SSRF 漏洞（导入路径绕过 IsPrivateHost → IsPrivateUrl 逐条过滤 + DNS 解析判 IP）",
            "● 修复日志系统死代码（_path 字段初始化绕过 setter → Log() 内延迟初始化）",
            "● 修复数据库初始化失败后双重崩溃（throw → Environment.Exit(1)）",
            "● 修复 UDP 探测定时被算作有效延迟（ct.IsCancellationRequested → winner==receiveTask）",
            "● 修复 SQLite 并发写入冲突（PRAGMA busy_timeout → journal_mode=WAL）",
            "● 修复窗口关闭阻止 Windows 关机（增加 HasShutdownStarted 检查）",
            "● 修复 NAT 检测中 STUN DNS 重复解析（每服务器仅一次，cached IP 复用）",
            "● 修复 _cts.Dispose() 后 Cancel() 抛 ObjectDisposedException（先 Cancel 再 Dispose）",
            "● 修复注册表日志消息反错（EULA ↔ Version 文案互换）",
            "🔧 优化",
            "● 多 URL 下载调度器改为探索-利用两阶段（每个 URL 至少被测一次后改用最快节点）",
            "● 完成状态消息区分测速模式（下载保留 X/Y 成功计数，上传/双向显示\"测速完成\"）",
            "● 双向测速 URL 计数改为实际值（dl+ul 而非 Max）",
            "🚀 新增",
            "● 更多功能窗口（Ping/DNS/HTTP/路由追踪/端口测试/MTU/NAT 检测等 18 个网络工具）",
            "● 测速准备弹窗（DNS 预解析 + HTTP 握手预热 + 线性进度条动画，准备中关窗停止）",
            "● NAT 检测支持自定义 STUN 服务器",
            "● 设置页改为左导航三分类布局",
        }));


    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mw)
            mw.ClosePage();
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/lingyingaojue/NetSpeedTest") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Log($"Open GitHub failed: {ex.Message}"); }
    }

    private void Email_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("mailto:mashuo2010az@163.com") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Log($"Open email failed: {ex.Message}"); }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (Helpers.UpdateChecker.IsChecking)
        {
            UpdateResultText.Text = "检查中...";
            return;
        }
        try
        {
            UpdateResultText.Text = "检查中...";
            var config = ((App)Application.Current).GetService<IConfiguration>();
            var (status, info) = await Helpers.UpdateChecker.CheckAsync(config);
            switch (status)
            {
                case Helpers.CheckStatus.NoUpdate:
                    UpdateResultText.Text = "已是最新版本";
                    break;
                case Helpers.CheckStatus.HasUpdate when info != null:
                    UpdateResultText.Text = $"发现新版本 {info.Version}";
                    ShowUpdateWindow(info);
                    break;
                case Helpers.CheckStatus.NotConfigured:
                    UpdateResultText.Text = "未配置更新源";
                    break;
                default:
                    UpdateResultText.Text = "检查更新失败";
                    break;
            }
        }
        catch (Exception ex)
        {
            UpdateResultText.Text = "检查更新失败";
            Logger.Log($"Check update error: {ex.Message}");
        }
    }

    private static void ShowUpdateWindow(NetSpeedTest.Helpers.UpdateInfo info)
    {
        if (Application.Current.Windows.OfType<Views.UpdateWindow>().Any()) return;
        var win = new Views.UpdateWindow(info.Version, info.Body, info.DownloadUrl)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is MainWindow)
        };
        win.Show();
    }
}

public record ChangelogEntry(string Version, string Date, List<string> Details);
