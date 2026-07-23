using System.Windows;
using NetSpeedTest.Services;

namespace NetSpeedTest.Views;

public partial class EulaWindow : Window
{
    public bool Agreed { get; private set; }
    public bool Revoked { get; private set; }
    private readonly bool _isFirstLaunch;

    public string EulaText { get; } = @"NetSpeedTest 最终用户许可协议（EULA）
生效日期：2026年7月21日 | 版本 2.0
开发者：凌影傲爵（联系方式：mashuo2010az@163.com）

使用本软件前请仔细阅读本协议。下载、安装或使用本软件即表示您同意受本协议约束。

━━━━━━ 1. 定义 ━━━━━━
• 本软件：指 NetSpeedTest 桌面应用程序（支持 Windows / macOS / Linux）。
• 开发者：指凌影傲爵。
• 用户：指合法安装并使用本软件的个人或实体。

━━━━━━ 2. 许可授予 ━━━━━━
开发者授予用户一项非独占、不可转让、不可再许可、可撤销的个人许可，允许用户在自有设备上安装并使用本软件，仅限个人非商业用途。

━━━━━━ 3. 软件费用 ━━━━━━
本软件目前免费提供。开发者保留未来收费的权利，若发生收费将提前通过官方渠道公告。

━━━━━━ 4. 使用限制 ━━━━━━
用户不得：
• 对本软件进行反向工程、反编译、反汇编或试图获取源代码；
• 复制、修改、分发、出租、租赁、出借、转让或制作衍生作品；
• 将本软件用于任何非法活动或违反适用法律法规的行为；
• 移除、修改或遮挡本软件中的版权、商标或其他所有权声明；
• 使用本软件攻击、干扰或破坏任何网络、服务器或服务。

━━━━━━ 5. 第三方组件声明 ━━━━━━
本软件可能包含按开源许可证授权的第三方组件。相关组件的版权归其各自所有者所有，并遵循相应的许可证条款（如 MIT、Apache 2.0、BSD 等）。

━━━━━━ 6. 知识产权 ━━━━━━
本软件（包括但不限于代码、UI 设计、图标、文档、品牌名称）的所有权利、所有权及知识产权归开发者所有。

━━━━━━ 7. 免责声明 ━━━━━━
本软件按'现状'提供，开发者不做任何明示或暗示的保证。在适用法律允许的最大范围内，开发者不承担任何间接、附带、特殊或后果性损害的责任。

━━━━━━ 8. 责任限制 ━━━━━━
在适用法律允许的最大范围内，开发者对用户因本软件产生的累计赔偿责任总额不超过人民币 100 元。

━━━━━━ 9. 终止 ━━━━━━
若用户违反本协议任何条款，开发者有权立即终止本许可。终止后用户须立即卸载并删除本软件的所有副本。

━━━━━━ 10. 管辖法律 ━━━━━━
本协议受中华人民共和国法律管辖。因本协议引起的争议，提交开发者住所地有管辖权的人民法院诉讼解决。


⚠️ NetSpeedTest 免责声明

使用本软件即表示您已阅读、理解并接受以下全部内容。

1. 软件按'现状'提供，不附带任何形式的明示或暗示保证。
2. 测速结果仅供用户参考，不构成对实际网络性能的任何承诺。
3. 本软件使用的测速节点来源于第三方公开测速服务器合集，开发者不对其可用性、安全性负责。
4. 使用风险自负。开发者不对任何因使用或无法使用本软件所导致的损失承担责任。
5. 您须年满 14 周岁。未满 14 周岁的未成年人不得使用本软件。


🔒 NetSpeedTest 隐私政策
最后更新：2026年7月21日 | 版本 2.0
开发者：凌影傲爵 | 联系方式：mashuo2010az@163.com

本隐私政策说明 NetSpeedTest 如何处理您的个人信息。

━━━━━━ 1. 我们处理的个人信息 ━━━━━━
• IP 地址：就近选择测速节点；计算网络延迟（处理目的）；本地获取后发送至第三方测速服务器
• 测速结果：延迟、下载/上传速度等（本地处理及展示）
• 设备信息：操作系统类型/版本、硬件架构（本地读取，不对外发送）

━━━━━━ 2. 个人信息的共享 ━━━━━━
• 测速时您的 IP 地址会被发送到您选择的第三方测速服务器
• 第三方测速服务器可能位于中华人民共和国境外
• 开发者绝不会出售、出租或交易您的个人信息

━━━━━━ 3. 数据存储 ━━━━━━
• 本地设备：测速记录保存在您的本地设备上
• 开发者服务器：不存储任何用户数据
• 第三方服务器：IP 地址由第三方临时处理

━━━━━━ 4. 您的权利 ━━━━━━
根据《个人信息保护法》，您享有：知情权、同意权、撤回同意权、删除权、查阅/复制权、投诉权。
撤回同意不影响撤回前基于同意已进行的数据处理的合法性。

━━━━━━ 5. 联系我们 ━━━━━━
📧 邮箱：mashuo2010az@163.com";

    public EulaWindow(bool isFirstLaunch = true)
    {
        InitializeComponent();
        DataContext = this;
        _isFirstLaunch = isFirstLaunch;

        if (isFirstLaunch)
        {
            HeaderText.Text = "请仔细阅读以下协议，同意后方可使用本软件";
            AgreeBtn.Visibility = Visibility.Visible;
            DisagreeBtn.Content = "不同意";
            DisagreeBtn.Width = 80;
            CloseBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeaderText.Text = "用户协议与声明（已同意）";
            AgreeBtn.Visibility = Visibility.Collapsed;
            DisagreeBtn.Content = "撤销同意并退出";
            DisagreeBtn.Width = 130;
            CloseBtn.Visibility = Visibility.Visible;
        }
    }

    private void Agree_Click(object sender, RoutedEventArgs e)
    {
        Agreed = true;
        Close();
    }

    private void Disagree_Click(object sender, RoutedEventArgs e)
    {
        if (_isFirstLaunch)
        {
            Agreed = false;
        }
        else
        {
            var result = System.Windows.MessageBox.Show(
                "撤销同意后本软件将退出，下次打开需重新同意协议。确定要撤销吗？",
                "NetSpeedTest", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\NetSpeedTest", false); } catch (Exception ex) { Logger.Log($"EULA revoke failed: {ex.Message}"); }
            Revoked = true;
        }
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
