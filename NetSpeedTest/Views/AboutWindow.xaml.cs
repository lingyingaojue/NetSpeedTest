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

        Changelog.Add(new ChangelogEntry("V1.3.4", "2026-07-27", new()
        {
            "🐛 修复",
            "● 修复 SaveResult JitterMs NOT NULL 约束崩溃（null→0 写、0→null 读）",
            "● 修复外网延迟测速期间不刷新（12 主机批量 3s 超时 → 8.8.8.8 单主机 UDP 轮询）",
            "● 修复抖动延迟不显示（ICMP Ping 满载超时 → 改为 UDP 五层回退）",
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

        Changelog.Add(new ChangelogEntry("V1.3.2", "2026-07-24", new()
        {
            "🐛 修复",
            "● 修复抖动延迟显示异常（竞态 + 赋值顺序 + 取消路径跳过计算）",
            "● 修复弹窗假零值（未测量时显示\"0 ms\"→\"--\"）",
            "● 修复 ThreadCount 恶意 JSON 注入绕过上限",
            "● 修复全速测试下载/上传线程分配不均（改为成对同时启动）",
            "● 修复外网延迟显示异常（绑定属性未同步 / 批内多次回调拉高均 / 最终 LAN 延迟 Token 已取消）",
            "● 修复设置页内容溢出遮挡（加滚动条 + 修正 Grid 行号越界）",
            "● 修复历史记录 DataGrid 列重复（补 AutoGenerateColumns=False）",
            "● 修复 AdaptiveThreadsEnabled 无法通过设置保存",
            "● 修复撤销同意删除版本记录（改删键值而非整键）",
            "● 修复补偿检测仅监控下载方向（补上传方向双向联合检测）",
            "🔧 优化",
            "● 延迟测试新增 UDP 优先层（五层回退：UDP→ICMP→TCP→HTTPS→HTTP）",
            "● DNS 缓存优化（12 主机预解析一次，每批省 12 次 DNS 查询）",
            "● HttpClient 超时兜底（900s 防卡死）",
            "● 单方向测速底部卡自动隐藏无关指标（Visibility 联动）",
            "● URL 动态调度（SelectBestUrl 按实时速度选最优节点）",
            "● 异常处理链路加固（9 处加日志 + IsDBNull 守卫 + SafeParseDate）",
            "● 抖动延迟实时更新（每样本重算 + 代码直写 UI）",
            "● 新增应用图标（exe / 任务栏 / 窗口标题栏 / 关于页）",
            "● 历史 DataGrid 设为只读",
            "🚀 新增",
            "● 系统托盘（右键菜单 / 状态联动 / 气泡通知）",
            "● 键盘快捷键（Enter/Esc/Ctrl+D/U/B）",
            "● 完成弹窗「复制结果」按钮",
            "● 历史记录统计栏 + CSV 导出",
            "● 抖动延迟指标（外网标准差，底部卡 + 弹窗）",
        }));

        Changelog.Add(new ChangelogEntry("V1.3.1", "2026-07-24", new()
        {
            "🐛 修复",
            "● 修复外网延迟显示异常（绑定属性未同步 / 批内多次回调拉高均 / 最终 LAN 延迟 Token 已取消）",
            "● 修复设置页内容溢出遮挡（加滚动条 + 修正 Grid 行号越界）",
            "● 修复历史记录 DataGrid 列重复（补 AutoGenerateColumns=False）",
            "● 修复 AdaptiveThreadsEnabled 无法通过设置保存",
            "● 修复撤销同意删除版本记录（改删键值而非整键）",
            "● 修复补偿检测仅监控下载方向（补上传方向双向联合检测）",
            "🔧 优化",
            "● 新增应用图标（exe / 任务栏 / 窗口标题栏 / 关于页）",
            "● 历史 DataGrid 设为只读",
            "● 错误提示文案修正",
        }));
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public record ChangelogEntry(string Version, string Date, List<string> Details);
