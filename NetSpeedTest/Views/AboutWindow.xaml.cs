using System.Windows;

namespace NetSpeedTest.Views;

public partial class AboutWindow : Window
{
    public List<ChangelogEntry> Changelog { get; } = new();

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;

        Changelog.Add(new ChangelogEntry("V1.2.0", "2026-07-23", new()
        {
            "✨ 新增",
            "● 历史记录扩展至 11 列（上传平均、外网延迟、总均速、总流量、类型标注）",
            "● 测速完成弹窗重构为左右键值对布局，按测试模式自适应内容",
            "● 线程数设置改为 2ⁿ 滑块（2 / 4 / 8 / 16 / 32 / 64 / 128 / 256 / 512）",
            "● 首次运行版本检测与更新日志弹窗",
            "● 协议更换为 MIT 开源许可",
            "🔧 优化",
            "● 测速初始值统一为 \"--\"，界面清爽无歧义",
            "● \"全速测速\"更名\"双向测速\"",
            "● 默认参数校准（60s 测速时长 / 500ms 线程间隔 / 10s 计量延迟）",
            "● 测速时长上限扩展至 600s",
            "● DPI 适配（1280×700 默认 / 1000×600 最小）",
            "● 按钮文字防裁剪、历史列宽防遮挡",
            "🐛 修复",
            "● 修复 DB 迁移失败导致的翻页崩溃",
            "● 修复 URL 参数空值守卫缺失",
            "● 修复 EULA 错误提示文案",
            "● 修复信号量异常退出、BytesUploaded 负值、Logger 竞态等 15 项缺陷",
        }));

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
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public record ChangelogEntry(string Version, string Date, List<string> Details);
