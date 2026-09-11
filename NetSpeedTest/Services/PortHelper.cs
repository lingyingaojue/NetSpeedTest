using System.Net;
using System.Net.Sockets;

namespace NetSpeedTest.Services;

/// <summary>
/// Web 服务器端口选择工具。
/// </summary>
public static class PortHelper
{
    public const int MinPort = 1024;
    public const int MaxPort = 65535;
    public const int DefaultPort = 8080;

    public static bool IsValidPort(int port) => port >= MinPort && port <= MaxPort;

    public static int ClampPort(int port) => Math.Clamp(port, MinPort, MaxPort);

    /// <summary>
    /// 预筛选端口是否未被普通 TCP 监听占用。HttpListener 启动阶段仍会再次校验。
    /// </summary>
    public static bool IsPortAvailable(int port)
    {
        if (!IsValidPort(port)) return false;

        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
        }
        catch { return false; }

        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
        }
        catch { return false; }

        return true;
    }

    /// <summary>
    /// 生成 Auto 模式候选端口，按优先级排序并去重。
    /// </summary>
    public static IEnumerable<int> GetCandidates(int preferredPort, int maxAttempts = 20)
    {
        var seen = new HashSet<int>();
        preferredPort = ClampPort(preferredPort);

        foreach (var candidate in BuildCandidates(preferredPort, maxAttempts))
        {
            if (IsValidPort(candidate) && seen.Add(candidate))
                yield return candidate;
        }

        var ephemeral = GetEphemeralPort();
        if (IsValidPort(ephemeral) && seen.Add(ephemeral))
            yield return ephemeral;
    }

    private static IEnumerable<int> BuildCandidates(int preferredPort, int maxAttempts)
    {
        yield return preferredPort;

        for (var i = 1; i <= maxAttempts; i++)
        {
            var candidate = preferredPort + i;
            if (candidate <= MaxPort) yield return candidate;
        }

        for (var candidate = MinPort; candidate < preferredPort; candidate++)
            yield return candidate;
    }

    public static int GetEphemeralPort()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        catch
        {
            return DefaultPort;
        }
    }
}
