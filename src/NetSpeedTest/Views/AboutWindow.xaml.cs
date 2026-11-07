using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using NetSpeedTest.Services;

namespace NetSpeedTest.Views;

public partial class AboutPage : UserControl
{
    public List<ChangelogEntry> Changelog { get; } = new();

    /// <summary>
    /// 版本徽标文案，取自程序集版本（NetSpeedTest.csproj 的 &lt;Version&gt;）。
    /// </summary>
    public string VersionBadge { get; } = NetSpeedTest.Helpers.AppVersion.Short;

    private DispatcherTimer? _copyToastTimer;

    public AboutPage()
    {
        InitializeComponent();
        DataContext = this;

        var config = ((App)Application.Current).GetService<IConfiguration>();
        var ad = config.GetSection("Advertising");
        SponsorNameText.Text = ad["SponsorName"] ?? "暂无";
        SponsorDetailText.Text = ad["SponsorDetail"] ?? "";

        Changelog.Add(new ChangelogEntry("V1.4.4", "2026-10-01", new()
        {
            "🛠️ 修复",
            "● 远程（网页/手机）发起的测速结果正常保存到历史并更新最近结果，不再「白测」",
            "● 双向测速正确合并下载/上传节点明细与成功数量，不再传输正常却提示「0 成功」",
            "● 测速中途停止的记录补填已产生的流量与峰值速率，三列不再为 0（无流量时仍为 0）",
            "● MTU 探测固定合理 TTL，跨网关外网目标可正常测出路径 MTU，并更正净荷/MTU 文案",
            "● 下载耗时工具拦截带宽 0/负数/空值，不再显示「∞ 秒」或负耗时",
            "● 子网掩码校验连续性，拒绝 255.0.255.0 等非法掩码；/0 放行并特别提示",
            "● 设置参数范围界面与网页统一，互相矛盾的设置（如超时小于延迟）报错并还原，不静默落盘",
            "● 内置网页兜底页补访问令牌，同网段设备写操作不再被拒（403）",
            "● 防火墙启动时真实创建入站规则、状态据实显示，不再只删不建、恒显已放行",
            "⚠️ 变更",
            "● 程序默认请求管理员权限：每次启动弹 UAC，点「是」运行（用于自动放行防火墙），拒绝提权将无法启动",
        }));

        Changelog.Add(new ChangelogEntry("V1.4.3", "2026-09-14", new()
        {
            "🛠️ 修复",
            "● 修复检查更新：Release 全部为预发布时 /releases/latest 返回 404，现自动回退 Release 列表并选择最新版本",
            "● 修复更新资产选择：优先下载 NetSpeedTest*.exe，跳过 source.zip 与校验文件",
            "● 修复并发检查重复请求、关于页检查中状态卡住以及启动日志出现 404 的问题",
            "✨ 优化",
            "● 更新源默认改为 Release 列表；直连失败时自动用系统代理重试一次，不影响测速代理设置",
        }));

        Changelog.Add(new ChangelogEntry("V1.4.2", "2026-09-12", new()
        {
            "✨ 优化",
            "● URL 以及线程分配逻辑：多 URL 调度改为轮转 + 健康度评分，避免线程少的场景下部分节点永远分不到流量",
            "● 多网卡延迟、丢包率显示：网关与外网延迟改用 UDP 五层回退，丢包按批统计，指标不再恒为 0 或 100%",
            "● 程序响应速度：网络状态、设置读写、测速启停均不再阻塞 UI 线程，等待超时按失败返回而不是卡死",
            "● 视觉界面美观度：统一响应头与错误文案，Web 控制台与桌面端排版对齐",
            "🚀 新增",
            "● 网卡独立延迟与丢包率显示：每张网卡分别呈现延迟、外网延迟、抖动与丢包",
            "● Web 端口自动检测避让以及自定义端口：端口被占用时自动顺延，也可指定固定端口",
            "🐛 修复",
            "● 修复网卡、配置等变动不刷新的问题：网络变化后自动重建访问绑定并刷新界面",
            "● 修复上传测速未校验响应状态码，4xx/5xx 被当作上传成功并计入虚假速度的问题",
            "● 修复 ICMP Port Unreachable 被误判为丢包/无延迟，导致丢包恒 100%、网关延迟为 0 的问题",
            "● 修复测速启动未真正生效时仍返回成功，以及准备阶段停止请求被忽略的问题",
            "● 修复 Web API 结果弹窗占住 UI 线程，导致无人值守时接口全部超时的问题",
            "● 修复设置写入非原子、并发保存产生半截配置的问题",
            "● 修复目标地址校验可被重定向绕过（SSRF）以及内部错误信息外泄的问题",
            "● 修复测速目标 URL/端口缺少白名单校验，以及写操作缺少会话令牌校验的问题",
            "● 修复自适应线程控制器的竞态：容量与目标读写作息不一致、速率队列并发枚举崩溃",
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

    private void Website_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://lingyingaojue.github.io/NetSpeedTest/") { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Log($"Open website failed: {ex.Message}"); }
    }

    private void Email_Click(object sender, RoutedEventArgs e)
    {
        CopyContact("mashuo2010az@163.com", "mashuo2010az@163.com");
    }

    private void WeChat_Click(object sender, RoutedEventArgs e)
    {
        CopyContact("Smailboy2010", $"{LocalizationService.Get("About_WeChat")} Smailboy2010");
    }

    private void Qq_Click(object sender, RoutedEventArgs e)
    {
        CopyContact("Smailboy2010", $"{LocalizationService.Get("About_QQ")} Smailboy2010");
    }

    private void CopyContact(string value, string label)
    {
        var message = $"{label} {LocalizationService.Get("About_Copied")}";
        CopyResultText!.Text = message;
        CopyToastText!.Text = $"{message} ✓";
        CopyToast!.IsOpen = true;

        if (_copyToastTimer == null)
        {
            _copyToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _copyToastTimer.Tick += (_, _) =>
            {
                _copyToastTimer.Stop();
                CopyToast.IsOpen = false;
            };
        }
        _copyToastTimer.Stop();
        _copyToastTimer.Start();

        try
        {
            Clipboard.SetText(value);
        }
        catch (Exception ex)
        {
            Logger.Log($"Copy contact failed: {ex.Message}");
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
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
        finally
        {
            CheckUpdateButton.IsEnabled = true;
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
