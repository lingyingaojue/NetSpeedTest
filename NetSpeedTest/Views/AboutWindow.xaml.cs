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

        Changelog.Add(new ChangelogEntry("V1.4.1", "2026-08-22", new()
        {
            "🚀 新功能",
            "● 关于页改版：开发者/AI 协作/GitHub/官方网站四张信息卡，官网与 GitHub 可点击跳转",
            "● 联系方式点击复制：邮箱、微信、QQ 一键复制到剪贴板",
            "● 复制成功弹窗：点击复制后弹出「已复制」提示，2 秒自动关闭",
            "✨ 优化",
            "● 复制流程更稳定：剪贴板被占用时提示不受影响",
        }));

        Changelog.Add(new ChangelogEntry("V1.4.0", "2026-08-20", new()
        {
            "🚀 新功能",
            "● 多网卡同时测速：支持勾选多张网卡并行测速，每张网卡独立绑定源 IP",
            "● 网卡勾选面板：主界面可选择要同时测速的网卡",
            "● 曲线按网卡切换：下载/上传图表支持合计/单网卡曲线切换",
            "● 测速时长显示：测速界面新增实时测速时长",
            "● 总速度显示：双向测速时显示下载+上传总速度",
            "● 单模式隐藏无效指标：下载隐藏上传，上传隐藏下载",
            "● 完成弹窗每网卡结果：多网卡测速完成后展示每张网卡独立速率/错误信息",
            "● 导出增强：多网卡导出包含聚合结果+每网卡明细+BatchId",
            "● 丢包率实时监测：测速过程实时显示丢包率，结果/历史/CSV/Web API 全链路记录",
            "● Web 服务器网卡网段映射：局域网设备可经本机网卡访问，并自动配置 HTTP.sys ACL 与防火墙规则",
            "● 自适应线程调度：开启后忽略固定线程数，线程数从 1 起步线性加压至严格 1024 上限，掉速时自动补偿统计与速率修正",
            "🐛 修复",
            "● 修复启动时只读属性绑定导致崩溃的问题",
            "● 修复上传测速后最近结果卡片不显示的问题",
            "● 修复多网卡总流量显示跳动问题",
            "● 修复多网卡速度曲线时间轴错乱问题",
            "● 修复多网卡单卡失败导致整体测速失败的问题",
            "● 修复多网卡绑定失败导致程序直接报错的问题",
            "● 修复 SQLite 发布版缺少 runtimes 导致无法启动的问题",
            "● 修复单上传/单下载弹窗仍显示总均速的问题",
            "● 修复复制结果时单模式仍包含总均速的问题",
            "● 修复历史 CSV 导出一次性加载全部记录的问题",
            "✨ 优化",
            "● 默认优先选择有默认网关的网卡",
            "● DNS 解析增加缓存，每次测速前自动清空",
            "● 多网卡线程并发数受总线程数限制",
            "● 历史记录增加 BatchId / ErrorMessage 字段",
            "● 网卡详细信息区域增加滚动条，避免挤压曲线图",
            "● 移除测速页不合理的导出按钮，导出入口调整到完成弹窗和最近结果区域",
            "● 支持发布为可直接运行的单文件 exe",
        }));


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
