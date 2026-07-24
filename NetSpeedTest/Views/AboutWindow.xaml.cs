using System.Windows;

namespace NetSpeedTest.Views;

public partial class AboutWindow : Window
{
    public List<ChangelogEntry> Changelog { get; } = new();

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;

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

        Changelog.Add(new ChangelogEntry("V1.3.0", "2026-07-23", new()
        {
            "🐛 修复",
            "● 修复信号量槽泄漏（回调异常导致并发量永久下降）",
            "● 修复全速测试自适应线程检测失效（tc 默认值误用）",
            "● 修复全速测试补偿时长剔除缺失",
            "● 修复 UI 与 DB 之间延迟值不一致（历史记录偏差）",
            "● 修复 DB 写入静默失败（增加异常日志兜底）",
            "● 修复 CancelTest 定时器处理器未摘除",
            "🔧 优化",
            "● 图表单测模式切换增加 300ms 平滑过渡动画",
            "● 下载/上传测速互斥回调（不显示对方方向数值）",
            "● 多网卡信息卡（9 项详情 + 彩色分类布局）",
            "● 历史记录独立页面 + 一键清除全部",
            "● 首页底部网络信息卡替代历史表格",
            "🚀 新增",
            "● 掉速紧急补偿（检测 + 自动加线程 + 最终结果修正）",
            "● 自适应线程上限（低配电脑防止多线程反噬降速）",
        }));
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public record ChangelogEntry(string Version, string Date, List<string> Details);
