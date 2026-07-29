using System.Windows;

namespace NetSpeedTest.Views;

public partial class AboutWindow : Window
{
    public List<ChangelogEntry> Changelog { get; } = new();

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => Helpers.WindowHelper.ClampToScreen(this);

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

        Changelog.Add(new ChangelogEntry("V1.3.4", "2026-07-27", new()
        {
            "🐛 修复",
            "● 修复 SaveResult JitterMs NOT NULL 约束崩溃（null→0 写、0→null 读）",
            "● 修复外网延迟测速期间不刷新（12 主机批量 3s 超时 → 8.8.8.8 单主机 UDP 轮询）",
            "● 修复抖动延迟不显示（ICMP Ping 满载超时 → 改为 TestGatewayLatencyAsync UDP 五层回退）",
            "● 修复测速结束后弹窗延迟过长（移除 3 处冗余 finalLatency 阻塞调用 + 后台任务并行退出）",
            "● 修复上传/双向测速 PeakMbps 始终为 0（PeakRate 跟踪从补偿门控拆出至无条件）",
            "● 修复 GetStatistics 静默吞 DB 异常",
            "🔧 优化",
            "● WAN/抖动/LAN 延迟探测统一为 UDP 优先五层回退",
            "● 延迟刷新频率默认 2000→1000ms，三指标同步",
            "● 后台任务取消后并行 await + UDP 探针 CancellationToken 可打断",
            "● 设置保存改为纯内存生效，不再持久化覆盖打包版默认值",
            "📝 修正",
            "● 设置页 \"延迟轮询间隔\"→\"延迟采样间隔\"",
        }));


    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public record ChangelogEntry(string Version, string Date, List<string> Details);
