using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace NetSpeedTest.ViewModels;

public partial class MoreViewModel : ObservableObject
{
    private readonly HttpClient _httpClient;

    [ObservableProperty] private int _selectedTab;

    public List<string> Tabs { get; } = [
        "Ping 工具", "DNS 查询", "HTTP 测速",
        "路由追踪", "端口测试", "MTU 探测", "DNS 对比",
        "IP 归属", "公网 IP", "SSL 证书", "HTTP Header",
        "子网计算", "带宽换算", "时间戳", "文本哈希", "Base64", "UUID 生成", "NAT 检测"
    ];

    public MoreViewModel(HttpClient httpClient) { _httpClient = httpClient; }

    // ========== 0: Ping ==========
    [ObservableProperty] private string _pingHost = "8.8.8.8";
    [ObservableProperty] private int _pingCount = 4;
    [ObservableProperty] private string _pingResult = "";
    [ObservableProperty] private bool _isPinging;

    [RelayCommand]
    private async Task StartPing()
    {
        if (IsPinging) return; IsPinging = true; PingResult = "";
        try
        {
            using var ping = new Ping(); var sb = new StringBuilder();
            sb.AppendLine($"Ping {PingHost} ({PingCount} 次)");
            sb.AppendLine(new string('-', 40));
            var times = new List<long>(); int sent = 0, received = 0;
            for (int i = 0; i < PingCount; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(PingHost, 3000); sent++;
                    if (reply.Status == IPStatus.Success)
                    { received++; times.Add(reply.RoundtripTime); sb.AppendLine($"来自 {reply.Address}: 时间={reply.RoundtripTime}ms TTL={reply.Options?.Ttl}"); }
                    else { sb.AppendLine($"来自 {reply.Address}: {reply.Status}"); }
                }
                catch (Exception ex) { sb.AppendLine($"发送失败: {ex.Message}"); }
                PingResult = sb.ToString();
                if (i < PingCount - 1) await Task.Delay(500);
            }
            sb.AppendLine(new string('-', 40));
            if (received > 0)
            { sb.AppendLine($"已发送={sent} 已接收={received} 丢失={sent - received} ({100 - received * 100 / sent}% 丢包)"); sb.AppendLine($"最短={times.Min()}ms 最长={times.Max()}ms 平均={times.Average():F1}ms"); }
            else { sb.AppendLine($"已发送={sent} 已接收=0 (100% 丢包)"); }
            PingResult = sb.ToString();
        }
        catch (Exception ex) { PingResult = $"Ping 失败: {ex.Message}\n\n提示：部分系统需要管理员权限才能发送 ICMP 包"; }
        finally { IsPinging = false; }
    }

    // ========== 1: DNS 查询 ==========
    [ObservableProperty] private string _dnsHost = "github.com";
    [ObservableProperty] private string _dnsResult = "";
    [ObservableProperty] private bool _isResolving;

    [RelayCommand]
    private async Task StartDns()
    {
        if (IsResolving) return; IsResolving = true; DnsResult = "";
        try
        {
            var sb = new StringBuilder(); sb.AppendLine($"DNS 查询: {DnsHost}"); sb.AppendLine(new string('-', 40));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { var a = await Dns.GetHostAddressesAsync(DnsHost); sw.Stop(); sb.AppendLine($"耗时: {sw.ElapsedMilliseconds}ms"); sb.AppendLine($"结果 ({a.Length} 条):"); foreach (var addr in a) sb.AppendLine($"  {addr} ({addr.AddressFamily})"); }
            catch (Exception ex) { sw.Stop(); sb.AppendLine($"耗时: {sw.ElapsedMilliseconds}ms"); sb.AppendLine($"解析失败: {ex.Message}"); }
            DnsResult = sb.ToString();
        }
        catch (Exception ex) { DnsResult = $"查询失败: {ex.Message}"; }
        finally { IsResolving = false; }
    }

    // ========== 2: HTTP 测速 ==========
    [ObservableProperty] private string _httpUrl = "https://www.google.com";
    [ObservableProperty] private string _httpResult = "";
    [ObservableProperty] private bool _isHttpTesting;

    [RelayCommand]
    private async Task StartHttp()
    {
        if (IsHttpTesting) return; IsHttpTesting = true; HttpResult = "";
        try
        {
            var sb = new StringBuilder(); sb.AppendLine($"HTTP 测速: {HttpUrl}"); sb.AppendLine(new string('-', 40));
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var req = new HttpRequestMessage(HttpMethod.Head, HttpUrl); var sw = System.Diagnostics.Stopwatch.StartNew();
                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token); sw.Stop();
                sb.AppendLine($"状态码: {(int)resp.StatusCode} {resp.ReasonPhrase}"); sb.AppendLine($"响应时间: {sw.ElapsedMilliseconds}ms"); sb.AppendLine($"服务器: {resp.Headers.Server}"); sb.AppendLine($"Content-Type: {resp.Content.Headers.ContentType}");
                if (resp.Content.Headers.ContentLength.HasValue) sb.AppendLine($"Content-Length: {resp.Content.Headers.ContentLength} bytes");
            }
            catch (TaskCanceledException) { sb.AppendLine("请求超时 (10s)"); }
            catch (Exception ex) { sb.AppendLine($"请求失败: {ex.Message}"); }
            HttpResult = sb.ToString();
        }
        catch (Exception ex) { HttpResult = $"测速失败: {ex.Message}"; }
        finally { IsHttpTesting = false; }
    }

    // ========== 3: 路由追踪 ==========
    [ObservableProperty] private string _traceHost = "8.8.8.8";
    [ObservableProperty] private string _traceResult = "";
    [ObservableProperty] private bool _isTracing;

    [RelayCommand]
    private async Task StartTrace()
    {
        if (IsTracing) return; IsTracing = true; TraceResult = "";
        try
        {
            var sb = new StringBuilder(); sb.AppendLine($"路由追踪到 {TraceHost} (最多 30 跳)"); sb.AppendLine(new string('-', 40));
            using var ping = new Ping();
            for (int ttl = 1; ttl <= 30; ttl++)
            {
                try
                {
                    var opt = new PingOptions(ttl, dontFragment: true);
                    var reply = await ping.SendPingAsync(TraceHost, 3000, new byte[32], opt);
                    sb.Append($"{ttl,2}  ");
                    if (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired)
                        sb.AppendLine($"{reply.Address}  {reply.RoundtripTime}ms");
                    else if (reply.Status == IPStatus.TimedOut)
                        sb.AppendLine("* 请求超时");
                    else
                        sb.AppendLine($"* {reply.Status}");
                    if (reply.Status == IPStatus.Success) { sb.AppendLine(new string('-', 40)); sb.AppendLine("已到达目标"); break; }
                }
                catch (Exception ex) { sb.AppendLine($"{ttl,2}  * 错误: {ex.Message}"); }
                TraceResult = sb.ToString(); await Task.Delay(50);
            }
        }
        catch (Exception ex) { TraceResult = $"路由追踪失败: {ex.Message}"; }
        finally { IsTracing = false; }
    }

    // ========== 4: 端口测试 ==========
    [ObservableProperty] private string _portHost = "github.com";
    [ObservableProperty] private string _port = "443";
    [ObservableProperty] private string _portResult = "";
    [ObservableProperty] private bool _isPortTesting;

    [RelayCommand]
    private async Task StartPort()
    {
        if (IsPortTesting) return; IsPortTesting = true; PortResult = "";
        try
        {
            if (!int.TryParse(Port, out var pn) || pn < 1 || pn > 65535) { PortResult = "端口号无效 (1-65535)"; return; }
            var sb = new StringBuilder(); sb.AppendLine($"TCP 连接测试: {PortHost}:{pn}"); sb.AppendLine(new string('-', 40));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(PortHost, pn, cts.Token); sw.Stop();
                sb.AppendLine($"结果: 端口开放 (耗时 {sw.ElapsedMilliseconds}ms)");
            }
            catch (OperationCanceledException) { sb.AppendLine("结果: 连接超时"); }
            catch (Exception ex) { sw.Stop(); sb.AppendLine($"结果: 无法连接 ({ex.Message})"); }
            PortResult = sb.ToString();
        }
        catch (Exception ex) { PortResult = $"测试失败: {ex.Message}"; }
        finally { IsPortTesting = false; }
    }

    // ========== 5: MTU 探测 ==========
    [ObservableProperty] private string _mtuHost = "8.8.8.8";
    [ObservableProperty] private string _mtuResult = "";
    [ObservableProperty] private bool _isMtuProbing;

    [RelayCommand]
    private async Task StartMtu()
    {
        if (IsMtuProbing) return; IsMtuProbing = true; MtuResult = "";
        try
        {
            var sb = new StringBuilder(); sb.AppendLine($"MTU 探测: {MtuHost}"); sb.AppendLine(new string('-', 40));
            using var ping = new Ping();
            int lo = 68, hi = 1500, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var opt = new PingOptions(1, dontFragment: true);
                try { var reply = await ping.SendPingAsync(MtuHost, 2000, new byte[mid], opt); if (reply.Status == IPStatus.Success) { found = mid; lo = mid + 1; } else { hi = mid - 1; } }
                catch { hi = mid - 1; }
                sb.AppendLine($"测试 MTU={mid}: {(found == mid ? "通过" : "失败")} (当前最大={found})");
                MtuResult = sb.ToString(); await Task.Delay(50);
            }
            sb.AppendLine(new string('-', 40)); sb.AppendLine(found > 0 ? $"路径 MTU = {found} bytes (+28 头 = {found + 28} IP MTU)" : "未找到可用 MTU");
            MtuResult = sb.ToString();
        }
        catch (Exception ex) { MtuResult = $"MTU 探测失败: {ex.Message}"; }
        finally { IsMtuProbing = false; }
    }

    // ========== 6: DNS 对比 ==========
    [ObservableProperty] private string _dnsCompHost = "github.com";
    [ObservableProperty] private string _dnsCompResult = "";
    [ObservableProperty] private bool _isDnsComparing;

    [RelayCommand]
    private async Task StartDnsComp()
    {
        if (IsDnsComparing) return; IsDnsComparing = true; DnsCompResult = "";
        try
        {
            string[] servers = ["8.8.8.8", "114.114.114.114", "1.1.1.1", "223.5.5.5"];
            var sb = new StringBuilder(); sb.AppendLine($"DNS 对比: {DnsCompHost}"); sb.AppendLine(new string('-', 40));
            using var ping = new Ping();
            foreach (var s in servers)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var reply = await ping.SendPingAsync(s, 2000); sw.Stop();
                    sb.AppendLine($"{s,-16} ▶ {reply.RoundtripTime,5}ms  {(reply.Status == IPStatus.Success ? "✓" : reply.Status.ToString())}");
                }
                catch (Exception ex) { sb.AppendLine($"{s,-16} ▶ 失败 ({ex.Message})"); }
                DnsCompResult = sb.ToString(); await Task.Delay(100);
            }
            sb.AppendLine(new string('-', 40)); sb.AppendLine("提示：延迟反映你到 DNS 服务器的网络状况，非 DNS 查询速度。");
            DnsCompResult = sb.ToString();
        }
        catch (Exception ex) { DnsCompResult = $"对比失败: {ex.Message}"; }
        finally { IsDnsComparing = false; }
    }

    // ========== 7: IP 归属 ==========
    [ObservableProperty] private string _geoIp = "8.8.8.8";
    [ObservableProperty] private string _geoResult = "";
    [ObservableProperty] private bool _isGeoQuerying;

    [RelayCommand]
    private async Task StartGeo()
    {
        if (IsGeoQuerying) return; IsGeoQuerying = true; GeoResult = "";
        try
        {
            var url = $"http://ip-api.com/json/{GeoIp}?lang=zh-CN&fields=country,regionName,city,isp,org,as,timezone";
            var json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var sb = new StringBuilder(); sb.AppendLine($"IP 归属查询: {GeoIp}"); sb.AppendLine(new string('-', 40));
            AppendJson(sb, r, "国家", "country"); AppendJson(sb, r, "地区", "regionName");
            AppendJson(sb, r, "城市", "city"); AppendJson(sb, r, "ISP", "isp");
            AppendJson(sb, r, "组织", "org"); AppendJson(sb, r, "AS号", "as");
            AppendJson(sb, r, "时区", "timezone");
            sb.AppendLine(new string('-', 40)); sb.AppendLine("数据来源: ip-api.com (仅供个人参考)");
            GeoResult = sb.ToString();
        }
        catch (Exception ex) { GeoResult = $"查询失败: {ex.Message}"; }
        finally { IsGeoQuerying = false; }
    }
    private static void AppendJson(StringBuilder sb, JsonElement r, string label, string key)
    { if (r.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null) sb.AppendLine($"{label}: {v.GetString()}"); else sb.AppendLine($"{label}: —"); }

    // ========== 8: 公网 IP ==========
    [ObservableProperty] private string _publicIpResult = "";
    [ObservableProperty] private bool _isCheckingPublicIp;

    [RelayCommand]
    private async Task CheckPublicIp()
    {
        if (IsCheckingPublicIp) return; IsCheckingPublicIp = true; PublicIpResult = "";
        try
        {
            var sb = new StringBuilder(); sb.AppendLine("查询公网 IP"); sb.AppendLine(new string('-', 40));
            string[] apis = ["https://api.ipify.org", "https://myip.ipip.net", "https://ifconfig.me/ip"];
            foreach (var api in apis)
            {
                try { using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var ip = await _httpClient.GetStringAsync(api, cts.Token); sb.AppendLine($"{new Uri(api).Host}: {ip.Trim()}"); }
                catch (Exception ex) { sb.AppendLine($"{new Uri(api).Host}: 失败 ({ex.Message})"); }
                PublicIpResult = sb.ToString(); await Task.Delay(50);
            }
            sb.AppendLine(new string('-', 40)); sb.AppendLine("多源验证，减少 CDN/代理干扰");
            PublicIpResult = sb.ToString();
        }
        catch (Exception ex) { PublicIpResult = $"查询失败: {ex.Message}"; }
        finally { IsCheckingPublicIp = false; }
    }

    // ========== 9: SSL 证书 ==========
    [ObservableProperty] private string _sslHost = "www.google.com";
    [ObservableProperty] private string _sslResult = "";
    [ObservableProperty] private bool _isSslChecking;

    [RelayCommand]
    private async Task CheckSsl()
    {
        if (IsSslChecking) return; IsSslChecking = true; SslResult = "";
        try
        {
            var sb = new StringBuilder(); sb.AppendLine($"SSL 证书: {SslHost}"); sb.AppendLine(new string('-', 40));
            X509Certificate2? cert = null;
            using var handler = new SocketsHttpHandler();
            handler.SslOptions.RemoteCertificateValidationCallback = (_, c, _, _) => { cert = new X509Certificate2(c!.GetRawCertData()); return true; };
            using var temp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            try
            {
                var url = SslHost.StartsWith("https://") ? SslHost : $"https://{SslHost}";
                using var _ = await temp.GetAsync(url);
                if (cert != null)
                {
                    sb.AppendLine($"颁发者: {cert.Issuer}"); sb.AppendLine($"主题: {cert.Subject}");
                    sb.AppendLine($"生效: {cert.NotBefore:yyyy-MM-dd HH:mm}"); sb.AppendLine($"到期: {cert.NotAfter:yyyy-MM-dd HH:mm}");
                    var daysLeft = (cert.NotAfter - DateTime.Now).Days;
                    sb.AppendLine($"剩余: {daysLeft} 天 {(daysLeft < 30 ? "⚠ 即将到期" : "✓")}");
                    sb.AppendLine($"序列号: {cert.SerialNumber}"); sb.AppendLine($"算法: {cert.SignatureAlgorithm.FriendlyName}");
                }
            }
            catch (Exception ex) { sb.AppendLine($"连接失败: {ex.Message}"); }
            cert?.Dispose(); SslResult = sb.ToString();
        }
        catch (Exception ex) { SslResult = $"证书查询失败: {ex.Message}"; }
        finally { IsSslChecking = false; }
    }

    // ========== 10: HTTP Header ==========
    [ObservableProperty] private string _headerUrl = "https://www.google.com";
    [ObservableProperty] private string _headerResult = "";
    [ObservableProperty] private bool _isHeaderChecking;

    [RelayCommand]
    private async Task CheckHeaders()
    {
        if (IsHeaderChecking) return; IsHeaderChecking = true; HeaderResult = "";
        try
        {
            var sb = new StringBuilder(); sb.AppendLine($"HTTP Header: {HeaderUrl}"); sb.AppendLine(new string('-', 40));
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var req = new HttpRequestMessage(HttpMethod.Head, HeaderUrl);
                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                sb.AppendLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}  (HTTP {resp.Version})");
                sb.AppendLine(new string('-', 40));
                foreach (var h in resp.Headers) { foreach (var v in h.Value) sb.AppendLine($"{h.Key}: {v}"); }
                foreach (var h in resp.Content.Headers) { foreach (var v in h.Value) sb.AppendLine($"{h.Key}: {v}"); }
                sb.AppendLine(new string('-', 40)); sb.AppendLine($"共 {resp.Headers.Count() + resp.Content.Headers.Count()} 个响应头");
            }
            catch (Exception ex) { sb.AppendLine($"请求失败: {ex.Message}"); }
            HeaderResult = sb.ToString();
        }
        catch (Exception ex) { HeaderResult = $"查询失败: {ex.Message}"; }
        finally { IsHeaderChecking = false; }
    }

    // ========== 11: 子网计算 ==========
    [ObservableProperty] private string _subnetIp = "192.168.1.1";
    [ObservableProperty] private string _subnetMask = "255.255.255.0";
    [ObservableProperty] private string _subnetResult = "";

    [RelayCommand]
    private void CalcSubnet()
    {
        try
        {
            if (!IPAddress.TryParse(SubnetIp, out var ip) || !IPAddress.TryParse(SubnetMask, out var mask)) { SubnetResult = "IP 地址或掩码格式无效"; return; }
            var ib = ip.GetAddressBytes(); var mb = mask.GetAddressBytes();
            if (ib.Length != 4) { SubnetResult = "仅支持 IPv4"; return; }
            var nb = new byte[4]; var bb = new byte[4]; uint ipv4 = 0, m = 0;
            for (int i = 0; i < 4; i++) { nb[i] = (byte)(ib[i] & mb[i]); bb[i] = (byte)(ib[i] | (byte)(~mb[i])); ipv4 = (ipv4 << 8) | ib[i]; m = (m << 8) | mb[i]; }
            uint cidr = 0; uint tm = m; while (tm > 0) { if ((tm & 0x80000000) != 0) cidr++; tm <<= 1; }
            uint hosts = m != 0xFFFFFFFF ? (uint)((1 << (32 - (int)cidr)) - 2) : 1;
            var sb = new StringBuilder(); sb.AppendLine($"子网计算: {SubnetIp}/{cidr}"); sb.AppendLine(new string('-', 40));
            sb.AppendLine($"网络地址: {new IPAddress(nb)}"); sb.AppendLine($"广播地址: {new IPAddress(bb)}"); sb.AppendLine($"子网掩码: {mask} (/{cidr})"); sb.AppendLine($"可用主机: {hosts}"); sb.AppendLine($"IP 范围: {new IPAddress(nb)} ~ {new IPAddress(bb)}"); sb.AppendLine(new string('-', 40));
            if (cidr == 32) sb.AppendLine("单个主机地址 (子网掩码 /32)");
            else { var f = new byte[4]; var l = new byte[4]; Array.Copy(nb, f, 4); Array.Copy(bb, l, 4); if (hosts > 0) { f[3]++; l[3]--; } sb.AppendLine($"可用范围: {new IPAddress(f)} ~ {new IPAddress(l)}"); }
            SubnetResult = sb.ToString();
        }
        catch (Exception ex) { SubnetResult = $"计算失败: {ex.Message}"; }
    }

    // ========== 12: 带宽换算 ==========
    [ObservableProperty] private string _bwMbps = "100";
    [ObservableProperty] private string _bwResult = "";

    [RelayCommand]
    private void CalcBandwidth()
    {
        try
        {
            if (!double.TryParse(BwMbps, out var mbps)) { BwResult = "请输入有效数字"; return; }
            var sb = new StringBuilder(); sb.AppendLine($"带宽换算: {mbps} Mbps"); sb.AppendLine(new string('-', 40));
            sb.AppendLine($"= {mbps / 8:F2} MB/s"); sb.AppendLine($"= {mbps * 1000 / 8:F1} KB/s");
            sb.AppendLine($"= {mbps / 1000:F4} Gbps"); sb.AppendLine($"= {mbps * 125:F0} KBps");
            sb.AppendLine(new string('-', 40)); sb.AppendLine($"100 MB 文件 ≈ {100 * 8 / mbps / (mbps > 0 ? 1 : 0.001):F1} 秒");
            BwResult = sb.ToString();
        }
        catch (Exception ex) { BwResult = $"换算失败: {ex.Message}"; }
    }

    // ========== 13: 时间戳 ==========
    [ObservableProperty] private string _tsInput = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
    [ObservableProperty] private string _tsResult = "";

    [RelayCommand]
    private void ConvertTimestamp()
    {
        try
        {
            var sb = new StringBuilder();
            if (long.TryParse(TsInput, out var ts))
            {
                DateTimeOffset dto;
                if (ts > 10000000000000) dto = DateTimeOffset.FromUnixTimeMilliseconds(ts);
                else dto = DateTimeOffset.FromUnixTimeSeconds(ts);
                sb.AppendLine($"时间戳 → 日期: {dto:yyyy-MM-dd HH:mm:ss.fff}"); sb.AppendLine($"时区: {dto:zzzz}");
                sb.AppendLine($"秒级: {dto.ToUnixTimeSeconds()}"); sb.AppendLine($"毫秒: {dto.ToUnixTimeMilliseconds()}");
            }
            else { sb.AppendLine($"当前: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}"); sb.AppendLine($"秒级: {DateTimeOffset.Now.ToUnixTimeSeconds()}"); sb.AppendLine($"毫秒: {DateTimeOffset.Now.ToUnixTimeMilliseconds()}"); }
            TsResult = sb.ToString();
        }
        catch (Exception ex) { TsResult = $"转换失败: {ex.Message}"; }
    }

    // ========== 14: 文本哈希 ==========
    [ObservableProperty] private string _hashInput = "";
    [ObservableProperty] private string _hashResult = "";

    [RelayCommand]
    private void CalcHash()
    {
        try
        {
            if (string.IsNullOrEmpty(HashInput)) { HashResult = "请输入文本"; return; }
            var bytes = Encoding.UTF8.GetBytes(HashInput);
            var sb = new StringBuilder(); sb.AppendLine($"文本哈希 (长度={HashInput.Length})"); sb.AppendLine(new string('-', 40));
            sb.AppendLine($"MD5:    {BytesToHex(MD5.HashData(bytes))}");
            sb.AppendLine($"SHA1:   {BytesToHex(SHA1.HashData(bytes))}");
            sb.AppendLine($"SHA256: {BytesToHex(SHA256.HashData(bytes))}");
            HashResult = sb.ToString();
        }
        catch (Exception ex) { HashResult = $"哈希失败: {ex.Message}"; }
    }
    private static string BytesToHex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    // ========== 15: Base64 ==========
    [ObservableProperty] private string _b64Input = "";
    [ObservableProperty] private string _b64Result = "";

    [RelayCommand]
    private void EncodeBase64()
    {
        try
        {
            if (string.IsNullOrEmpty(B64Input)) { B64Result = "请输入文本"; return; }
            var sb = new StringBuilder();
            sb.AppendLine($"Base64 编码:"); sb.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(B64Input)));
            sb.AppendLine(); sb.AppendLine($"Base64 解码:");
            try { sb.AppendLine(Encoding.UTF8.GetString(Convert.FromBase64String(B64Input))); }
            catch { sb.AppendLine("(无法解码为 UTF-8 文本)"); }
            B64Result = sb.ToString();
        }
        catch (Exception ex) { B64Result = $"转换失败: {ex.Message}"; }
    }

    // ========== 16: UUID 生成 ==========
    [ObservableProperty] private int _uuidCount = 3;
    [ObservableProperty] private string _uuidResult = "";

    [RelayCommand]
    private void GenUuid()
    {
        try
        {
            var n = Math.Clamp(UuidCount, 1, 50);
            var sb = new StringBuilder(); sb.AppendLine($"UUID 生成 (×{n})");
            for (int i = 0; i < n; i++) sb.AppendLine(Guid.NewGuid().ToString("D"));
            UuidResult = sb.ToString();
        }
        catch (Exception ex) { UuidResult = $"生成失败: {ex.Message}"; }
    }

    // ========== 17: NAT 检测 ==========
    [ObservableProperty] private string _natResult = "";
    [ObservableProperty] private bool _isNatDetecting;
    [ObservableProperty] private string _customStunServer = "";

    private const uint StunMagicCookie = 0x2112A442;
    private static readonly (string server, string label)[] NatStunServers = [
        ("stun.l.google.com:19302", "Google"),
        ("stun.miwifi.com:3478", "小米")
    ];

    [RelayCommand]
    private async Task DetectNat()
    {
        if (IsNatDetecting) return; IsNatDetecting = true; NatResult = "";
        using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var ct = overallCts.Token;
        var sb = new StringBuilder(); var sbLock = new object();
        void Append(string s) { lock (sbLock) { sb.AppendLine(s); NatResult = sb.ToString(); } }
        try
        {
            Append("══════ NAT 类型检测 ══════");
            Append("");

            var localIps = await Dns.GetHostAddressesAsync(Dns.GetHostName(), ct);
            var localIp = localIps.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (localIp == null) { NatResult = "未检测到 IPv4 地址"; return; }
            Append($"本机 IPv4: {localIp}");

            Append("--- 公网 IP 验证 ---");
            var stunServers = GetStunServers();
            var dnsCache = new Dictionary<string, IPAddress>();
            foreach (var s in stunServers)
            {
                try
                {
                    var parts = s.server.Split(':');
                    var addrs = await Dns.GetHostAddressesAsync(parts[0], ct);
                    var ip = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (ip != null) dnsCache[s.server] = ip;
                }
                catch { }
            }
            Append($"STUN 服务器: {string.Join(", ", stunServers.Select(s => s.label))}");
            string? publicIpHttp = null;
            var httpTask = Task.Run(async () => { try { using var c = new CancellationTokenSource(TimeSpan.FromSeconds(4)); publicIpHttp = (await _httpClient.GetStringAsync("https://api.ipify.org", c.Token)).Trim(); } catch { } });

            var stunResults = await Task.WhenAll(stunServers.Select(async s => {
                try { return await StunQueryAsync(s.server, null, ct: ct, preResolved: dnsCache.GetValueOrDefault(s.server)); } catch { return null; }
            }));
            await httpTask;

            string? stunIp = null; int stunPort = 0;
            foreach (var r in stunResults) { if (r != null) { stunIp = r.Value.ip; stunPort = r.Value.port; break; } }
            Append($"ipify.org:  {(publicIpHttp ?? "—")}");
            Append($"STUN 公网:  {(stunIp != null ? $"{stunIp}:{stunPort}" : "—")}");
            if (publicIpHttp != null && stunIp != null) Append(publicIpHttp == stunIp ? "✓ 两方一致" : "⚠ IP 地址不一致");
            Append("");
            if (stunIp == null) { Append("STUN 全部不可达，检测终止"); return; }

            Append("--- 端口保留 ---");
            int fixedPort = 50901;
            (string, int)? fixedMap = null;
            try { fixedMap = await StunQueryAsync(stunServers[0].server, fixedPort, ct: ct, preResolved: dnsCache.GetValueOrDefault(stunServers[0].server)); } catch { }
            if (fixedMap != null)
            {
                Append($"本地固定端口 {fixedPort} → 映射 {fixedMap.Value.Item2}");
                Append(fixedMap.Value.Item2 == fixedPort ? "✓ 端口保留" : $"✗ 端口被改 (△{fixedMap.Value.Item2 - fixedPort})");
            }
            else Append("⚠ 端口保留测试失败");
            Append("");

            Append("--- 映射行为 ---");
            var mappingBag = new ConcurrentBag<(string label, string ip, int port)>();
            await Task.WhenAll(stunServers.Select(async s => {
                try { var r = await StunQueryAsync(s.server, null, ct: ct, preResolved: dnsCache.GetValueOrDefault(s.server)); if (r != null) mappingBag.Add((s.label, r.Value.ip, r.Value.port)); else Append($"{s.label,-10} ▶ 超时"); }
                catch (OperationCanceledException) { Append($"{s.label,-10} ▶ 超时"); }
                catch { Append($"{s.label,-10} ▶ 不可达"); }
            }));
            var entries = mappingBag.OrderBy(e => Array.IndexOf(stunServers, stunServers.First(s => s.label == e.label))).ToList();
            var successIps = entries.Select(e => e.ip).Distinct().ToList();
            var successPorts = entries.Select(e => e.port).Distinct().ToList();
            Append("");
            Append($"公网 IP 数: {successIps.Count} ({(successIps.Count == 1 ? "始终一致 ✓" : "多次变化 ⚠")})");
            Append($"映射端口数: {successPorts.Count} ({(successPorts.Count == 1 ? "始终一致 ✓" : $"共 {successPorts.Count} 种")})");
            if (successPorts.Count == 1) Append("映射行为: 锥形（跨服务器映射一致）");
            else Append("映射行为: 对称型（跨服务器映射不同）");
            Append("");

            Append("--- 过滤行为 ---");
            Append("⚠ CHANGE-REQUEST 已被 RFC 5389 废弃，现代 STUN 不支持全锥/受限区分");
            Append("");

            Append("--- Hairpin 自返 ---");
            try
            {
                using var hpUdp = new UdpClient();
                var hpReq = BuildStunRequest();
                await hpUdp.SendAsync(hpReq, hpReq.Length, new IPEndPoint(IPAddress.Parse(stunIp), stunPort));
                var hpRecvTask = hpUdp.ReceiveAsync();
                var hpTimeout = Task.Delay(3000, ct);
                var done = await Task.WhenAny(hpRecvTask, hpTimeout);
                if (done == hpRecvTask) Append("✓ 支持 Hairpin（自返地址可达）");
                else Append("✗ 不支持 Hairpin");
            }
            catch { Append("⚠ Hairpin 测试异常"); }
            Append("");

            Append("--- STUN TCP ---");
            try
            {
                using var tcpCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var tcpLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, tcpCts.Token);
                var tcpResult = await StunTcpAsync("stun.l.google.com", 19302, tcpLinked.Token);
                Append($"TCP 公网 IP: {tcpResult.ip}:{tcpResult.port}");
                Append(tcpResult.ip == stunIp ? "✓ TCP/UDP 映射一致" : "⚠ TCP/UDP 映射不同");
            }
            catch { Append("⚠ STUN TCP 不可用"); }
            Append("");

            Append("══════ 综合结论 ══════");
            var isNat = !localIp.Equals(IPAddress.Parse(stunIp));
            Append($"NAT 状态: {(isNat ? "位于 NAT 后面" : "公网直连（无 NAT）")}");
            if (isNat) Append(successPorts.Count == 1 ? "NAT 类型: 锥形（映射稳定）" : "NAT 类型: 对称型");
            var ispPart = publicIpHttp != null ? $" | 公网 IP: {publicIpHttp}" : "";
            Append($"映射 IP: {stunIp}:{stunPort}{ispPart}");
            Append("═══════════════════════");
        }
        catch (OperationCanceledException) { Append("检测超时（25s 限制）"); }
        catch (Exception ex) { Append($"检测异常: {ex.Message}"); }
        finally { IsNatDetecting = false; }
    }

    private static async Task<(string ip, int port)?> StunQueryAsync(string server, int? localPort, int timeoutMs = 2000, int retries = 2, CancellationToken ct = default, IPAddress? preResolved = null)
    {
        var parts = server.Split(':'); var host = parts[0]; var port = int.Parse(parts[1]);
        IPAddress ip;
        if (preResolved != null)
        {
            ip = preResolved;
        }
        else
        {
            try
            {
                using var dnsCts = new CancellationTokenSource(3000);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, dnsCts.Token);
                var addrs = await Dns.GetHostAddressesAsync(host, linked.Token);
                ip = addrs.First(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            catch { return null; }
        }
        var ep = new IPEndPoint(ip, port);
        var req = BuildStunRequest();

        for (int i = 0; i < retries; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var udp = localPort.HasValue ? new UdpClient(localPort.Value) : new UdpClient();
            udp.Client.ReceiveTimeout = timeoutMs;
            await udp.SendAsync(req, req.Length, ep);
            try
            {
                var result = await udp.ReceiveAsync();
                return ParseStunResponse(result.Buffer);
            }
            catch { }
            if (i < retries - 1) await Task.Delay(200, ct);
        }
        return null;
    }

    private static byte[] BuildStunRequest()
    {
        var req = new byte[20];
        req[0] = 0; req[1] = 1;
        uint mc = StunMagicCookie;
        req[4] = (byte)(mc >> 24); req[5] = (byte)(mc >> 16);
        req[6] = (byte)(mc >> 8); req[7] = (byte)mc;
        var rng = new byte[12]; System.Security.Cryptography.RandomNumberGenerator.Fill(rng);
        Array.Copy(rng, 0, req, 8, 12);
        return req;
    }

    private static (string ip, int port)? ParseStunResponse(byte[] buf)
    {
        if (buf.Length < 20 || buf[0] != 1 || buf[1] != 1) return null;
        int off = 20, len = (buf[2] << 8) | buf[3], end = Math.Min(off + len, buf.Length);
        while (off + 4 <= end)
        {
            int t = (buf[off] << 8) | buf[off + 1], al = (buf[off + 2] << 8) | buf[off + 3], vo = off + 4;
            if (t == 0x0020 && vo + 8 <= end)
            {
                int mp = ((buf[vo + 2] << 8) | buf[vo + 3]) ^ 0x2112;
                uint mi = ((uint)buf[vo + 4] << 24) | ((uint)buf[vo + 5] << 16) | ((uint)buf[vo + 6] << 8) | buf[vo + 7];
                mi ^= StunMagicCookie;
                return ($"{mi >> 24}.{(mi >> 16) & 0xFF}.{(mi >> 8) & 0xFF}.{mi & 0xFF}", mp);
            }
            off += 4 + al + (al % 4 != 0 ? 4 - (al % 4) : 0);
        }
        return null;
    }

    private static async Task<(string ip, int port)> StunTcpAsync(string host, int port, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);
        var stream = tcp.GetStream();
        var req = BuildStunRequest();
        var lenBytes = new byte[2]; lenBytes[0] = (byte)(req.Length >> 8); lenBytes[1] = (byte)(req.Length & 0xFF);
        await stream.WriteAsync(lenBytes, ct);
        await stream.WriteAsync(req, ct);
        await stream.FlushAsync(ct);

        var hdr = new byte[2];
        await stream.ReadExactlyAsync(hdr, 0, 2, ct);
        int bodyLen = (hdr[0] << 8) | hdr[1];
        var body = new byte[bodyLen];
        await stream.ReadExactlyAsync(body, 0, bodyLen, ct);
        var full = new byte[20 + bodyLen];
        Array.Copy(hdr, 0, full, 2, 2); // skip STUN type placeholder
        full[0] = 1; full[1] = 1; // assume binding success
        full[2] = (byte)(bodyLen >> 8); full[3] = (byte)(bodyLen & 0xFF);
        Array.Copy(body, 0, full, 20, bodyLen);
        var r = ParseStunResponse(full);
        if (r == null) throw new Exception("解析失败");
        return r.Value;
    }

    private (string server, string label)[] GetStunServers()
    {
        if (string.IsNullOrWhiteSpace(CustomStunServer)) return NatStunServers;
        var parts = CustomStunServer.Trim().Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 3478;
        return [(host + ":" + port, "自定义")];
    }
}
