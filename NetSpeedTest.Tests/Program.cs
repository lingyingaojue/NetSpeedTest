using System.Net.Http;
using NetSpeedTest.Models;
using NetSpeedTest.Services;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS  {name}");
    }
    else
    {
        failures.Add(name);
        Console.WriteLine($"FAIL  {name}");
    }
}

var wlan = new NetworkAdapterInfo
{
    Id = "wlan-id",
    Name = "WLAN",
    IPAddress = "192.168.1.10",
    Gateway = "192.168.1.1",
    IsPhysical = true
};

var ethernet = new NetworkAdapterInfo
{
    Id = "eth-id",
    Name = "Ethernet",
    IPAddress = "10.0.0.2",
    Gateway = "10.0.0.1",
    IsPhysical = true
};

var fallbackClient = new HttpClient();
var boundClient = new HttpClient();
var factoryCalls = 0;

Func<NetworkAdapterInfo, HttpClient?> boundFactory = _ =>
{
    factoryCalls++;
    return boundClient;
};

var provided = SpeedTestService.ResolveEgressClientCore(
    new[] { wlan }, boundClient, hasMultipleActiveAdapters: true, boundFactory, fallbackClient);
Check(ReferenceEquals(provided.Client, boundClient) && provided.IsAdapterBound && !provided.OwnsClient && !provided.BindFailed && factoryCalls == 0,
    "传入 client 时直接复用，不重复创建绑定客户端");

var singleWithAlternatives = SpeedTestService.ResolveEgressClientCore(
    new[] { wlan }, null, hasMultipleActiveAdapters: true, boundFactory, fallbackClient);
Check(ReferenceEquals(singleWithAlternatives.Client, boundClient) && singleWithAlternatives.IsAdapterBound
      && singleWithAlternatives.OwnsClient && !singleWithAlternatives.BindFailed && factoryCalls == 1,
    "单卡勾选且存在其他活动网卡时创建绑定客户端");

factoryCalls = 0;
var singleWithoutAlternatives = SpeedTestService.ResolveEgressClientCore(
    new[] { wlan }, null, hasMultipleActiveAdapters: false, boundFactory, fallbackClient);
Check(ReferenceEquals(singleWithoutAlternatives.Client, fallbackClient) && !singleWithoutAlternatives.IsAdapterBound
      && !singleWithoutAlternatives.OwnsClient && !singleWithoutAlternatives.BindFailed && factoryCalls == 0,
    "系统仅一张活动网卡时保持默认路由兼容性");

var bindFailed = SpeedTestService.ResolveEgressClientCore(
    new[] { wlan }, null, hasMultipleActiveAdapters: true, _ => null, fallbackClient);
Check(ReferenceEquals(bindFailed.Client, fallbackClient) && !bindFailed.IsAdapterBound
      && !bindFailed.OwnsClient && bindFailed.BindFailed,
    "绑定工厂返回 null 时回退默认路由并标记失败");

factoryCalls = 0;
var multiple = SpeedTestService.ResolveEgressClientCore(
    new[] { wlan, ethernet }, null, hasMultipleActiveAdapters: true, boundFactory, fallbackClient);
Check(ReferenceEquals(multiple.Client, fallbackClient) && !multiple.IsAdapterBound
      && !multiple.OwnsClient && !multiple.BindFailed && factoryCalls == 0,
    "多网卡列表不由单卡自动绑定逻辑处理");

var networkInfo = new NetworkInfoService();
var selectedGateway = networkInfo.FindPingableGateway(new[] { wlan });
Check(selectedGateway == wlan.Gateway,
    "显式传入选中网卡时优先使用该网卡网关");

var noGatewayAdapter = new NetworkAdapterInfo
{
    Id = "no-gw",
    Name = "WLAN-NO-GW",
    IPAddress = "192.168.50.2",
    Gateway = null,
    IsPhysical = true
};
var noGateway = networkInfo.FindPingableGateway(new[] { noGatewayAdapter });
Check(noGateway == null,
    "选中网卡无网关时不回退到其他网卡的网关");

fallbackClient.Dispose();
boundClient.Dispose();

if (failures.Count > 0)
{
    Console.WriteLine($"FAILED: {failures.Count}");
    return 1;
}

Console.WriteLine("ALL TESTS PASSED");
return 0;