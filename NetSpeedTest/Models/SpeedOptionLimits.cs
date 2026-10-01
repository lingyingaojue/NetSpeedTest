namespace NetSpeedTest.Models;

/// <summary>
/// 测速设置参数取值边界的唯一事实源（BUG-CLAMP-003）。
/// Web 后端 ApplySettingsCore、设置页 SettingsViewModel 的钳制必须共用这一组边界；
/// SettingsWindow.xaml 滑块无法绑定编译期常量、仍写字面量，由 SettingsClampTests 的
/// “XAML ↔ 常量一致性”测试守护，防止三处再次漂移。边界口径统一以设置页滑块（UI）为准。
/// </summary>
internal static class SpeedOptionLimits
{
    public const int ThreadCountMin = 2, ThreadCountMax = 1024;
    public const int TestTimeoutSecMin = 10, TestTimeoutSecMax = 600;
    public const int AverageDelaySecMin = 1, AverageDelaySecMax = 30;
    public const double RateWindowSecMin = 0.5, RateWindowSecMax = 10.0;
    public const int NicPollIntervalMsMin = 200, NicPollIntervalMsMax = 5000;
    public const int ThreadRampUpMsMin = 0, ThreadRampUpMsMax = 2000;
    public const int LatencyPollIntervalMsMin = 500, LatencyPollIntervalMsMax = 10000;
    public const int JitterPollIntervalMsMin = 500, JitterPollIntervalMsMax = 5000;
    public const int PacketLossPollIntervalMsMin = 500, PacketLossPollIntervalMsMax = 5000;
    public const double CompensationThresholdMin = 0.3, CompensationThresholdMax = 0.8;
    public const int CompensationConfirmSecMin = 1, CompensationConfirmSecMax = 10;
}
