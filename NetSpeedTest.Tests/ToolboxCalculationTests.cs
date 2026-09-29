using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using NetSpeedTest.ViewModels;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// QA 补充：18 合 1 工具箱中纯计算工具（子网计算 / 带宽换算）的正确性与输入校验。
/// 对应风险 RM-TOOLS-010。
/// 标记 Skip 的用例是已确认缺陷的复现检查点，修复后取消 Skip 即转为回归测试。
/// </summary>
public class ToolboxCalculationTests
{
    private static MoreViewModel CreateVm() => new MoreViewModel(new HttpClient());

    private static string CalcSubnet(string ip, string mask)
    {
        var vm = CreateVm();
        vm.SubnetIp = ip;
        vm.SubnetMask = mask;
        vm.CalcSubnetCommand.Execute(null);
        return vm.SubnetResult;
    }

    private static string CalcBandwidth(string mbps)
    {
        var vm = CreateVm();
        vm.BwMbps = mbps;
        vm.CalcBandwidthCommand.Execute(null);
        return vm.BwResult;
    }

    [Fact]
    public void Subnet_24_ReportsCorrectNetworkBroadcastRangeAndHostCount()
    {
        var r = CalcSubnet("192.168.1.1", "255.255.255.0");

        Assert.Contains("网络地址: 192.168.1.0", r);
        Assert.Contains("广播地址: 192.168.1.255", r);
        Assert.Contains("/24", r);
        Assert.Contains("可用主机: 254", r);
        Assert.Contains("可用范围: 192.168.1.1 ~ 192.168.1.254", r);
    }

    [Fact]
    public void Subnet_30_ReportsTwoUsableHosts()
    {
        var r = CalcSubnet("10.0.0.1", "255.255.255.252");
        Assert.Contains("/30", r);
        Assert.Contains("可用主机: 2", r);
        Assert.Contains("可用范围: 10.0.0.1 ~ 10.0.0.2", r);
    }

    [Fact]
    public void Subnet_32_ReportsSingleHost()
    {
        var r = CalcSubnet("172.16.5.4", "255.255.255.255");
        Assert.Contains("/32", r);
        Assert.Contains("可用主机: 1", r);
        Assert.Contains("单个主机地址", r);
    }

    [Fact]
    public void Subnet_31_ReportsPointToPoint_NoUsableHosts()
    {
        var r = CalcSubnet("172.16.5.4", "255.255.255.254");
        Assert.Contains("/31", r);
        Assert.Contains("可用主机: 0", r);
        Assert.Contains("点对点 /31", r);
    }

    [Fact]
    public void Subnet_InvalidIp_ReportsError()
    {
        var r = CalcSubnet("999.1.1.1", "255.255.255.0");
        Assert.Contains("无效", r);
    }

    [Fact]
    public void Bandwidth_100Mbps_ConvertsTo12_5_MBps()
    {
        var r = CalcBandwidth("100");
        Assert.Contains("= 12.50 MB/s", r);
        Assert.Contains("= 0.1000 Gbps", r);
    }

    [Fact]
    public void Bandwidth_1000Mbps_ConvertsTo125_MBps()
    {
        var r = CalcBandwidth("1000");
        Assert.Contains("= 125.00 MB/s", r);
        Assert.Contains("= 1.0000 Gbps", r);
    }

    [Fact]
    public void Bandwidth_NonNumeric_ReportsError()
    {
        var r = CalcBandwidth("abc");
        Assert.Contains("请输入有效数字", r);
    }

    // ===== 已确认缺陷复现检查点（修复前跳过，避免主测试工程变红；修复后启用）=====

    [Fact(Skip = "BUG-BW-004 复现：带宽为 0 时 double 除零显示“∞ 秒”，应提示输入大于 0 的数值")]
    public void Bandwidth_Zero_ShouldReject_NotShowInfinity()
    {
        var r = CalcBandwidth("0");
        Assert.DoesNotContain("∞", r);
        Assert.Contains("大于 0", r);
    }

    [Fact(Skip = "BUG-BW-004 复现：负带宽未拦截，下载耗时显示为负数")]
    public void Bandwidth_Negative_ShouldReject()
    {
        var r = CalcBandwidth("-50");
        Assert.Contains("有效", r);
        Assert.DoesNotContain("-", r.Replace("-50", ""));
    }

    [Fact(Skip = "BUG-SUBNET-005 复现：非连续子网掩码（如 255.0.255.0）未校验，输出错误的 CIDR/网络地址")]
    public void Subnet_NonContiguousMask_ShouldReportInvalidMask()
    {
        var r = CalcSubnet("192.168.1.1", "255.0.255.0");
        Assert.Contains("掩码", r);
    }

    [Fact(Skip = "BUG-MTU-001 复现：MTU 探测 PingOptions TTL 硬编码为 1，跨网关目标在第一跳 TTL 过期，必然“未找到可用 MTU”")]
    public void MtuProbe_UsesTtlLargeEnoughToReachRemoteTarget()
    {
        var source = ReadToolboxSource();
        var startMtu = ExtractMethod(source, "StartMtu");
        // 合法的路径 MTU 探测必须允许包到达远端（路由追踪最多 30 跳），TTL 不得固定为 1。
        Assert.DoesNotContain("new PingOptions(1,", startMtu);
        Assert.Contains("PingOptions(", startMtu);
    }

    // ===== 辅助 =====

    private static string ExtractMethod(string source, string methodName)
    {
        var marker = "Task " + methodName + "(";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"方法 {methodName} 未找到");
        var brace = source.IndexOf('{', start);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source.Substring(brace, i - brace + 1);
            }
        }
        throw new InvalidOperationException("方法体解析失败");
    }

    private static string ReadToolboxSource()
    {
        var relative = Path.Combine("ViewModels", "MoreViewModel.cs");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            foreach (var candidate in new[] { relative, Path.Combine("NetSpeedTest", relative) })
            {
                var path = Path.Combine(dir.FullName, candidate);
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"MoreViewModel.cs not found above {AppContext.BaseDirectory}");
    }
}
