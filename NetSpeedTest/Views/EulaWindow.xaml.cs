using System.Windows;
using NetSpeedTest.Services;

namespace NetSpeedTest.Views;

public partial class EulaWindow : Window
{
    public bool Agreed { get; private set; }
    public bool Revoked { get; private set; }
    private readonly bool _isFirstLaunch;

    public string EulaText { get; } = @"MIT 开源许可协议
生效日期：2026年7月23日 | 版本 3.0
开发者：凌影傲爵
邮箱：mashuo2010az@163.com
GitHub：https://github.com/lingyingaojue/NetSpeedTest

Copyright (c) 2026 凌影傲爵

特此免费授予任何获得本软件及相关文档文件（以下简称""本软件""）副本的人士
不受限制地处理本软件的权利，包括但不限于使用、复制、修改、合并、发布、
分发、再许可和/或出售本软件副本的权利，以及允许获得本软件的人士
如此行事的权利，但须符合以下条件：

上述版权声明和本许可声明应包含在本软件的所有副本或实质性部分中。

本软件按'现状'提供，不作任何明示或暗示的保证，包括但不限于对适销性、
特定用途适用性和非侵权的保证。在任何情况下，作者或版权持有人均不对
任何索赔、损害或其他责任负责，无论是在合同、侵权或其他方面，由软件
或软件的使用或其他交易引起、由软件引起或与之相关。

━━━━━━ 开源与社区贡献 ━━━━━
本软件源代码已在 GitHub 以 MIT 许可证开源发布。欢迎提交 Issue、
Pull Request 或 Fork 本项目。使用或分发本软件即表示您同意遵守
MIT 许可证的全部条款。

━━━━━━ 第三方组件声明 ━━━━━
本软件包含以下按开源许可证授权的第三方组件：
● CommunityToolkit.Mvvm（MIT）
● LiveChartsCore（MIT）
● Microsoft.Data.Sqlite（MIT）
● Microsoft.Extensions.*（MIT）
● SkiaSharp（MIT）

━━━━━━ 免责声明 ━━━━━
使用本软件即表示您已阅读、理解并接受以下全部内容。
1. 软件按'现状'提供，不附带任何形式的明示或暗示保证。
2. 测速结果仅供用户参考，不构成对实际网络性能的任何承诺。
3. 本软件使用的测速节点来源于第三方公开测速服务器，开发者不对其可用性、安全性负责。
4. 使用风险自负。开发者不对任何因使用或无法使用本软件所导致的损失承担责任。

━━━━━━ 使用条款 ━━━━━
1. 本软件为网络测速工具，测速时会向第三方服务器发送 IP 地址。
   根据《个人信息保护法》，需年满 14 周岁方可使用。
   以上为使用条件，不构成本软件 MIT 许可证的额外限制。

━━━━━━ 隐私政策 ━━━━━
1. 本软件仅在您的设备本地存储测速记录和历史数据，不上传至任何服务器。
2. 测速时您的 IP 地址会被发送到您选择的第三方测速服务器，用于建立网络连接。
3. 第三方测速服务器可能位于中华人民共和国境外。
4. 开发者绝不会出售、出租或交易您的个人信息。
5. 根据《个人信息保护法》，您享有：知情权、同意权、撤回同意权、删除权。
6. 联系方式：mashuo2010az@163.com
   GitHub：https://github.com/lingyingaojue/NetSpeedTest";

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
