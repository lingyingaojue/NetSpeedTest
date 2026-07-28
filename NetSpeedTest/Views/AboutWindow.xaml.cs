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

        Changelog.Add(new ChangelogEntry("V1.3.5", "2026-07-28", new()
        {
            "🐛 修复",
            "● 修复 DNS 重绑定 SSRF（导入路径绕过内网地址检测，现已解析 IP 后判私有）",
            "● 修复日志系统死代码（Logger._path 字段初始化绕过 setter → 改延迟初始化）",
            "● 修复数据库初始化失败后双重崩溃（throw → Environment.Exit(1)）",
            "● 修复 UDP 探测定时被算作有效延迟值（ct.IsCancellationRequested → winner==receiveTask）",
            "● 修复 SQLite 并发写入冲突（PRAGMA busy_timeout → journal_mode=WAL，数据库级持久化）",
            "● 修复窗口关闭阻止 Windows 关机（增加 HasShutdownStarted 检查）",
            "● 修复 NAT 检测中 STUN DNS 重复解析（每服务器仅解析一次，后续复用 cached IP）",
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

        Changelog.Add(new ChangelogEntry("V1.3.3", "2026-07-26", new()
        {
            "🐛 修复",
            "● 修复弹窗延迟与实时延迟一致问题（弹窗误读 VM 属性而非 result 对象）",
            "● 修复 LongRef 值拷贝导致平均速度始终为 0",
            "● 修复 allRateSamples.Min() 空列表崩溃",
            "● 修复 ProfileService INSERT OR REPLACE 覆盖原始 CreatedAt",
            "● 修复 GetAllRecords int.MaxValue 全量加载 OOM",
            "● 修复下载失败无退避 CPU 空转",
            "● 修复 NIC 监控 fire-and-forget 竞态（改返 Task + await）",
            "● 修复 SettingsWindow InputBackgroundBrush 未定义崩溃",
            "● 修复 FormatLatency(0) 误显示 \"--\"",
            "● 修复 CSV 导出 NaN 字符串 + SSRF 内网阻断",
            "● 修复自适应 PeakEfficiency 只增不减（新增衰减）",
            "● 修复 _stopwatch 跨线程可见性（volatile）",
            "● 修复 EULA 撤销后退托盘不退出",
            "● 修复日志消息颠倒 + 翻页恢复硬编码",
            "● 修复多窗口低 DPI/非全屏文字溢出",
            "🔧 优化",
            "● 延迟/抖动/NIC 速率共用平均计量延迟设置",
            "● 抖动算法重构：固定 8.8.8.8 单次 ICMP + 滑动窗口标准差",
            "● WAN DNS 阻塞修复：删同步预解析，改异步动态解析",
            "● 取消后弹窗显示部分结果并正常入库",
            "● 滚动条美化 + 横向滚轮支持 + F2 精度 + 窗口屏幕自适应",
            "● 补偿恢复阈值 0.8→0.5 + 退出重置 PeakRate",
            "🚀 新增",
            "● 抖动探测独立配置（主机/间隔滑块）",
            "● 全窗口 TextTrimming/TextWrapping/ScrollViewer 溢出防护",
            "● Logger.Log 诊断日志链",
            "📝 修正",
            "● HistoryWindow 列头 \"延迟\"→\"平均延迟\"",
            "● 弹窗标签 \"抖动延迟\"→\"平均抖动延迟\"",
            "● 设置页 \"延迟轮询间隔\"→\"延迟采样间隔\"",
        }));


    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public record ChangelogEntry(string Version, string Date, List<string> Details);
