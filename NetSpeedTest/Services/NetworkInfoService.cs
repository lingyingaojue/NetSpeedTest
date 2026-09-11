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

    /// <summary>
    /// 清空网卡对象缓存，用于网络变化后重新获取 NetworkInterface。
    /// </summary>
    public void InvalidateCache() => _niCache.Clear();
    private static readonly string[] VirtualKeywords =
    {
        "Virtual", "VMware", "VirtualBox", "Hyper-V", "vEthernet", "WSL",
        "Docker", "VPN", "TAP", "TUN", "Tailscale", "ZeroTier", "WireGuard",
        "OpenVPN", "Bluetooth", "Loopback", "Tunnel", "Pseudo", "Npcap",
        "Microsoft KM-TEST"
    };

    /// <summary>
    /// 获取网卡列表。
    /// </summary>
    /// <param name="includeVirtual">是否包含虚拟网卡。</param>
    public List<NetworkAdapterInfo> GetAdapters(bool includeVirtual)
    {
        NetworkInterface[] allInterfaces;
        try
        {
            allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return new List<NetworkAdapterInfo>();
        }

        var adapters = CollectAdapters(allInterfaces, includeVirtual);

        // 没有物理网卡时自动降级，避免列表为空。
        if (adapters.Count == 0 && !includeVirtual)
        {
            adapters = CollectAdapters(allInterfaces, includeVirtual: true);
        }

        return adapters;
    }

    /// <summary>
    /// 获取所有已连接的物理网卡。
    /// </summary>
    public List<NetworkAdapterInfo> GetPhysicalAdapters() => GetAdapters(includeVirtual: false);

    private List<NetworkAdapterInfo> CollectAdapters(NetworkInterface[] allInterfaces, bool includeVirtual)
    {
        var adapters = new List<NetworkAdapterInfo>();
        foreach (var ni in allInterfaces)
        {
            try
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var adapter = CreateAdapter(ni);
                if (adapter == null) continue;
                if (!includeVirtual && adapter.IsVirtual) continue;
                adapters.Add(adapter);
            }
            catch { }
        }
        return adapters;
    }
    private NetworkAdapterInfo? CreateAdapter(NetworkInterface ni)
    {
        try
        {
            var desc = ni.Description ?? string.Empty;
            var name = ni.Name ?? string.Empty;
            var isVirtual = IsVirtualAdapter(ni, name, desc);
            var ipProps = ni.GetIPProperties();
            var ipv4 = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            var gateway = ipProps.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address?.ToString()
                ?? ipProps.GatewayAddresses.FirstOrDefault()?.Address?.ToString();

            var ipv6 = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)?.Address?.ToString();
            var dns = string.Join(", ", ipProps.DnsAddresses.Select(d => d.ToString()).Where(s => !string.IsNullOrEmpty(s)));
            var dhcpServer = ipProps.DhcpServerAddresses.FirstOrDefault()?.ToString();
            int? mtu = null;
            try { mtu = ipProps.GetIPv4Properties()?.Mtu; } catch { }

            var typeName = ni.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => "以太网",
                NetworkInterfaceType.Wireless80211 => "WiFi",
                _ => ni.NetworkInterfaceType.ToString()
            };
            var statusText = ni.OperationalStatus == OperationalStatus.Up ? "已连接" :
                             ni.OperationalStatus == OperationalStatus.Down ? "已断开" : "未知";

            return new NetworkAdapterInfo
            {
                Id = ni.Id,
                Name = name,
                Description = desc,
                IPAddress = ipv4?.Address?.ToString(),
                SubnetMask = ipv4?.IPv4Mask?.ToString(),
                Gateway = gateway,
                MacAddress = ni.GetPhysicalAddress().ToString(),
                LinkSpeedBps = ni.Speed > 0 ? ni.Speed : null,
                IPv6Address = ipv6,
                DnsServers = dns.Length > 0 ? dns : null,
                DhcpServer = dhcpServer,
                Mtu = mtu,
                TypeName = typeName,
                StatusText = statusText,
                IsPhysical = !isVirtual,
                IsVirtual = isVirtual,
                Kind = isVirtual ? AdapterKind.Virtual : AdapterKind.Physical,
                IsWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                IsOperational = ni.OperationalStatus == OperationalStatus.Up
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsVirtualAdapter(NetworkInterface ni, string name, string desc)
    {
        if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
            or NetworkInterfaceType.Tunnel
            or NetworkInterfaceType.Ppp)
            return true;

        var text = $"{name} {desc}";
        return VirtualKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
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
