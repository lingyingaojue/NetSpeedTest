using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// BUG-FW-006 回归：防火墙入站规则必须真实探测、按需创建，就绪状态反映 netsh 结果，
/// 不再「只删不建（add 死代码）+ 无条件置 _firewallReady=true」。
/// </summary>
public class FirewallProbeTests
{
    private const string EnglishAllow8080 = """
Rule Name:                            NetSpeedTest Web Server
----------------------------------------------------------------------
Enabled:                              Yes
Direction:                            In
Profiles:                             Domain,Private,Public
Protocol:                             TCP
LocalPort:                            8080
Action:                               Allow
""";

    private const string ChineseAllow8080 = """
规则名称:                              NetSpeedTest Web Server
----------------------------------------------------------------------
已启用:                                是
方向:                                  入
配置文件:                              域,专用,公用
协议:                                  TCP
本地端口:                              8080
操作:                                  允许
""";

    private const string EnglishBlock8080 = """
Rule Name:                            NetSpeedTest Web Server
Protocol:                             TCP
LocalPort:                            8080
Action:                               Block
""";

    private const string ChineseBlock8080 = """
规则名称:                              NetSpeedTest Web Server
协议:                                  TCP
本地端口:                              8080
操作:                                  阻止
""";

    private const string Allow9090 = """
Rule Name:                            NetSpeedTest Web Server
Protocol:                             TCP
LocalPort:                            9090
Action:                               Allow
""";

    [Fact]
    public void Parse_detects_english_allow_rule_for_port()
    {
        Assert.True(WebServerService.ParseFirewallRulePresent(EnglishAllow8080, 8080));
    }

    [Fact]
    public void Parse_detects_localized_chinese_allow_rule_for_port()
    {
        Assert.True(WebServerService.ParseFirewallRulePresent(ChineseAllow8080, 8080));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("No rules match the specified criteria.")]
    [InlineData("没有与指定标准匹配的规则。")]
    [InlineData("some unrelated netsh output")]
    public void Parse_rejects_empty_missing_or_unrelated_output(string? output)
    {
        Assert.False(WebServerService.ParseFirewallRulePresent(output, 8080));
    }

    [Fact]
    public void Parse_rejects_when_local_port_differs()
    {
        Assert.False(WebServerService.ParseFirewallRulePresent(Allow9090, 8080));
    }

    [Fact]
    public void Parse_does_not_match_port_substring()
    {
        // 8080 不得误命中 80800 / 18080。
        var output = "Rule Name: NetSpeedTest Web Server\nLocalPort:                            80800\nAction: Allow";
        Assert.False(WebServerService.ParseFirewallRulePresent(output, 8080));
    }

    [Fact]
    public void Parse_rejects_block_action_english_and_chinese()
    {
        Assert.False(WebServerService.ParseFirewallRulePresent(EnglishBlock8080, 8080));
        Assert.False(WebServerService.ParseFirewallRulePresent(ChineseBlock8080, 8080));
    }

    [Fact]
    public void EnsureFirewallRule_reports_ready_after_successful_add_and_probe()
    {
        string Run(string args) => args.Contains("show rule") ? EnglishAllow8080 : "";

        Assert.True(WebServerService.EnsureFirewallRule(8080, Run));
    }

    [Fact]
    public void EnsureFirewallRule_throws_when_add_denied_instead_of_reporting_ready()
    {
        string Run(string args) => args.Contains("add rule")
            ? throw new InvalidOperationException("请求的操作需要提升(作为管理员运行)。")
            : "";

        Assert.Throws<InvalidOperationException>(() => WebServerService.EnsureFirewallRule(8080, Run));
    }

    [Fact]
    public void EnsureFirewallRule_reports_not_ready_when_probe_finds_nothing()
    {
        string Run(string args) => args.Contains("show rule")
            ? "No rules match the specified criteria."
            : "";

        Assert.False(WebServerService.EnsureFirewallRule(8080, Run));
    }

    [Fact]
    public void EnsureFirewallRule_reports_not_ready_when_probed_port_drifts()
    {
        string Run(string args) => args.Contains("show rule") ? Allow9090 : "";

        Assert.False(WebServerService.EnsureFirewallRule(8080, Run));
    }

    [Fact]
    public void FirewallReady_property_tracks_probed_field_not_hardcoded()
    {
        var service = RuntimeHelpers.GetUninitializedObject(typeof(WebServerService));
        var field = typeof(WebServerService).GetField("_firewallReady", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);

        field!.SetValue(service, false);
        Assert.False(((WebServerService)service).FirewallReady);

        field.SetValue(service, true);
        Assert.True(((WebServerService)service).FirewallReady);
    }
}
