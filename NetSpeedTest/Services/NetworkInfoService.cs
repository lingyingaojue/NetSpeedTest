using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using NetSpeedTest.Models;

namespace NetSpeedTest.Services;

/// <summary>
/// 网卡信息枚举服务
/// </summary>
public class NetworkInfoService
{
    private readonly ConcurrentDictionary<string, NetworkInterface> _niCache = new();
    private static readonly string[] ExcludeKeywords = { "Virtual", "VMware", "VirtualBox", "Hyper-V", "Bluetooth", "VPN", "Docker", "Loopback", "Tunnel", "Pseudo" };

    /// <summary>
    /// 获取所有已连接的物理网卡
    /// </summary>
    public List<NetworkAdapterInfo> GetPhysicalAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();

        NetworkInterface[] allInterfaces;
        try
        {
            allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return adapters;
        }

        foreach (var ni in allInterfaces)
        {
            try
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var desc = ni.Description ?? string.Empty;
                var name = ni.Name ?? string.Empty;

                if (ExcludeKeywords.Any(k => desc.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                              name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var ipProps = ni.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                var gateway = ipProps.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address?.ToString()
                    ?? ipProps.GatewayAddresses.FirstOrDefault()?.Address?.ToString();

                var adapter = new NetworkAdapterInfo
                {
                    Id = ni.Id,
                    Name = name,
                    Description = desc,
                    IPAddress = ipv4?.Address?.ToString(),
                    SubnetMask = ipv4?.IPv4Mask?.ToString(),
                    Gateway = gateway,
                    MacAddress = ni.GetPhysicalAddress().ToString(),
                    LinkSpeedBps = ni.Speed > 0 ? ni.Speed : null,
                    IsPhysical = true,
                    IsWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                    IsOperational = true
                };

                adapters.Add(adapter);
            }
            catch
            {
                // 跳过单个失败的网卡
            }
        }

        return adapters;
    }

    /// <summary>
    /// 获取指定网卡当前累计收/发字节数（系统级计数器）
    /// </summary>
    public (long Received, long Sent)? GetCurrentBytes(string adapterId)
    {
        try
        {
            if (!_niCache.TryGetValue(adapterId, out var ni))
            {
                ni = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.Id == adapterId);
                if (ni == null) return null;
                _niCache[adapterId] = ni;
            }
            try
            {
                var stats = ni.GetIPv4Statistics();
                return (stats.BytesReceived, stats.BytesSent);
            }
            catch (NetworkInformationException)
            {
                _niCache.TryRemove(adapterId, out _);
                return null;
            }
        }
        catch { return null; }
    }

    /// <summary>
    /// 获取第一个可用网关（排除 IPv6 链路本地地址）
    /// </summary>
    public string? FindPingableGateway()
    {
        // 优先 IPv4 网关
        foreach (var a in GetPhysicalAdapters())
        {
            if (!string.IsNullOrEmpty(a.Gateway) && !a.Gateway.StartsWith("fe80:"))
                return a.Gateway;
        }
        return null;
    }
}
