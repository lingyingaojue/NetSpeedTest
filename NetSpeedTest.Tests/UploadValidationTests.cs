using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// B1-1 (F-02) 回归测试：上传请求必须校验 HTTP 状态码。
/// 旧实现写作 `using var _ = await http.SendAsync(...)`，丢弃响应导致 4xx/5xx 被当作上传成功，
/// 使失败节点贡献虚假的 Mbps。
/// </summary>
public class UploadValidationTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("netspeedtest-upload-payload");

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task SendUploadAsync_throws_on_non_success_status(int statusCode)
    {
        using var server = new SingleResponseServer(statusCode);
        using var http = new HttpClient();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            SpeedTestService.SendUploadAsync(http, server.Url, Payload, CancellationToken.None));

        Assert.Equal((HttpStatusCode)statusCode, ex.StatusCode);
        Assert.Equal(1, server.RequestCount);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    public async Task SendUploadAsync_succeeds_on_2xx(int statusCode)
    {
        using var server = new SingleResponseServer(statusCode);
        using var http = new HttpClient();

        await SpeedTestService.SendUploadAsync(http, server.Url, Payload, CancellationToken.None);

        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public async Task SendUploadAsync_posts_octet_stream_body()
    {
        using var server = new SingleResponseServer(200);
        using var http = new HttpClient();

        await SpeedTestService.SendUploadAsync(http, server.Url, Payload, CancellationToken.None);

        Assert.Equal("POST", server.LastMethod);
        Assert.Equal(Payload.Length, server.LastBodyLength);
        Assert.Equal("application/octet-stream", server.LastContentType);
    }

    [Fact]
    public async Task SendUploadAsync_propagates_cancellation()
    {
        using var server = new SingleResponseServer(200);
        using var http = new HttpClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SpeedTestService.SendUploadAsync(http, server.Url, Payload, cts.Token));
    }

    /// <summary>回环上只回一个固定状态码的一次性 HTTP 服务。</summary>
    private sealed class SingleResponseServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private int _requestCount;

        public string Url { get; }
        public string? LastMethod { get; private set; }
        public int LastBodyLength { get; private set; }
        public string? LastContentType { get; private set; }
        public int RequestCount => Volatile.Read(ref _requestCount);

        public SingleResponseServer(int statusCode)
        {
            var port = GetFreeTcpPort();
            Url = $"http://127.0.0.1:{port}/upload";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(() => ServeAsync(statusCode));
        }

        private async Task ServeAsync(int statusCode)
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }

                try
                {
                    Interlocked.Increment(ref _requestCount);
                    LastMethod = ctx.Request.HttpMethod;
                    LastContentType = ctx.Request.ContentType;
                    using (var ms = new MemoryStream())
                    {
                        await ctx.Request.InputStream.CopyToAsync(ms);
                        LastBodyLength = (int)ms.Length;
                    }

                    ctx.Response.StatusCode = statusCode;
                    ctx.Response.ContentLength64 = 0;
                    ctx.Response.OutputStream.Close();
                }
                catch
                {
                    try { ctx.Response.Abort(); } catch { }
                }
            }
        }

        private static int GetFreeTcpPort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            _cts.Dispose();
        }
    }
}
