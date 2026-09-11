namespace NetSpeedTest.Models;

/// <summary>
/// Web 服务器端口模式。
/// </summary>
public enum WebServerPortMode
{
    /// <summary>自动选择空闲端口。</summary>
    Auto = 0,

    /// <summary>使用用户指定的固定端口。</summary>
    Custom = 1
}
