using System.Globalization;

namespace NetSpeedTest.Helpers;

public static class FormatHelper
{
    public static string FormatRate(double? mbps)
    {
        if (!mbps.HasValue) return "--";
        return FormatRate(mbps.Value);
    }

    public static string FormatRate(double mbps)
    {
        if (double.IsNaN(mbps) || double.IsInfinity(mbps) || mbps < 0) return "--";
        if (mbps >= 999.95) return (mbps / 1000).ToString("F2", CultureInfo.InvariantCulture) + " Gbps";
        if (mbps < 1) return (mbps * 1000).ToString("F0", CultureInfo.InvariantCulture) + " Kbps";
        return mbps.ToString("F1", CultureInfo.InvariantCulture) + " Mbps";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "--";
        if (bytes >= 1_099_511_627_776L) return (bytes / 1_099_511_627_776.0).ToString("F2", CultureInfo.InvariantCulture) + " TB";
        if (bytes >= 1_073_741_824) return (bytes / 1_073_741_824.0).ToString("F2", CultureInfo.InvariantCulture) + " GB";
        if (bytes >= 1_048_576) return (bytes / 1_048_576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";
        if (bytes >= 1_024) return (bytes / 1_024.0).ToString("F1", CultureInfo.InvariantCulture) + " KB";
        return bytes.ToString("F0", CultureInfo.InvariantCulture) + " B";
    }

    public static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "--";
        if (seconds <= 0) return "0.0 s";
        if (seconds < 1) return (seconds * 1000).ToString("F0", CultureInfo.InvariantCulture) + " ms";
        if (seconds < 60) return seconds.ToString("F1", CultureInfo.InvariantCulture) + " s";
        var mins = (int)(seconds / 60);
        var secs = (int)(seconds % 60);
        return mins + " min " + secs + " s";
    }

    public static string FormatLatency(double? ms)
    {
        if (!ms.HasValue) return "--";
        return FormatLatency(ms.Value);
    }

    public static string FormatLatency(double ms)
    {
        if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0) return "--";
        return ms.ToString("F0", CultureInfo.InvariantCulture) + " ms";
    }
}
