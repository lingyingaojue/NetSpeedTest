using System.Windows;

namespace NetSpeedTest.Views;

public partial class AboutWindow : Window
{
    public List<ChangelogEntry> Changelog { get; } = new();

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;

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
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public record ChangelogEntry(string Version, string Date, List<string> Details);
