using System.Windows;

namespace NetSpeedTest.Views;

public partial class AboutWindow : Window
{
    public List<ChangelogEntry> Changelog { get; } = new();

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;

        Changelog.Add(new ChangelogEntry("V1.1.1", "2026-07-22", new()
        {
            "🐛 修复",
            "● 修复 NIC 计数器基线漂移导致的流量异常问题",
            "● 修复测试结束后的历史记录刷新竞态条件",
            "● 修复 EULA 持久化机制的多路径一致性问题",
            "● 修复 8 项安全与健壮性缺陷，整体稳定性提升",
            "🔧 优化",
            "● 适配多分辨率场景（1280×700 默认 / 1000×600 最小）",
            "● 重构测速完成弹窗（左右键值对布局、按测试模式自适应内容）",
            "● 全平台数值格式统一为 InvariantCulture，消除地区化显示偏差",
            "● 修复顶部工具栏四字按钮文字被裁剪的问题",
            "● 新增首次运行更新日志弹窗（主界面加载后自动展示）",
            "🚀 性能",
            "● 全速测试缓冲区改用 ArrayPool 复用，降低 GC 压力",
            "● HTTP 重试链路增加退避延迟，避免密集失败重试",
        }));

        Changelog.Add(new ChangelogEntry("V1.1.0", "2026-07-22", new()
        {
            "✨ 新增",
            "● 上传测速：上行带宽测速支持，完整覆盖网络性能评估",
            "● 图表动态效果：单下载/单上传视图新增过渡动画",
            "● 自动单位切换：根据速率自动匹配 Kbps/Mbps/Gbps",
            "🔧 优化",
            "● 文字渲染效果改进",
            "● 界面布局重新排版，信息层级更清晰",
            "● 设置模块重构，6 项设置全部生效",
            "🐛 修复",
            "● 14 个漏洞修复，程序稳定性大幅提升",
        }));

        Changelog.Add(new ChangelogEntry("V1.0", "2026-07-21", new()
        {
            "● 首个正式版本",
            "● 多线程并发下载测速",
            "● 实时 NIC 速率监控",
            "● 内网/外网延迟检测",
            "● 历史记录与图表展示",
        }));
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public record ChangelogEntry(string Version, string Date, List<string> Details);
