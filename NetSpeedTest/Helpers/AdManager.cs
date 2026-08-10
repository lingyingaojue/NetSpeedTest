using Microsoft.Win32;

namespace NetSpeedTest.Helpers;

/// <summary>
/// 广告管理：7 天关闭逻辑的注册表读写（App 与设置页共用）
/// </summary>
public static class AdManager
{
    private const string Key = @"Software\NetSpeedTest";
    private const string ValueName = "AdClosedAt";
    private static readonly TimeSpan CloseDuration = TimeSpan.FromDays(7);

    /// <summary>
    /// 启动时是否应展示广告（距上次关闭 >= 7 天；异常视为应展示）
    /// </summary>
    public static bool ShouldShowAd()
    {
        var closedAt = ReadClosedAt();
        if (closedAt == null) return true;
        return DateTime.Now - closedAt.Value >= CloseDuration;
    }

    /// <summary>
    /// 关闭广告 7 天
    /// </summary>
    public static void CloseAdFor7Days()
    {
        try
        {
            using var rk = Registry.CurrentUser.CreateSubKey(Key);
            rk?.SetValue(ValueName, DateTime.Now.Ticks.ToString());
        }
        catch (Exception ex) { Services.Logger.Log($"CloseAd write failed: {ex.Message}"); }
    }

    /// <summary>
    /// 剩余关闭天数（null = 未关闭或已过期）
    /// </summary>
    public static int? RemainingDays()
    {
        var closedAt = ReadClosedAt();
        if (closedAt == null) return null;
        var remain = CloseDuration - (DateTime.Now - closedAt.Value);
        if (remain <= TimeSpan.Zero) return null;
        return (int)Math.Ceiling(remain.TotalDays);
    }

    private static DateTime? ReadClosedAt()
    {
        try
        {
            using var rk = Registry.CurrentUser.OpenSubKey(Key);
            var val = rk?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(val) || !long.TryParse(val, out var ticks)) return null;
            return new DateTime(ticks, DateTimeKind.Local);
        }
        catch (Exception ex) { Services.Logger.Log($"AdClosedAt read failed: {ex.Message}"); return null; }
    }
}
