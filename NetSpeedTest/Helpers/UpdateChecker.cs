using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace NetSpeedTest.Helpers;

public enum CheckStatus { NoUpdate, HasUpdate, Failed, NotConfigured }

public record UpdateInfo(string Version, string Body, string DownloadUrl);

/// <summary>
/// GitHub Releases 更新检查（防重入）
/// </summary>
public static class UpdateChecker
{
    private static bool _isChecking;

    public static bool IsChecking => _isChecking;

    public static async Task<(CheckStatus status, UpdateInfo? info)> CheckAsync(IConfiguration config)
    {
        if (_isChecking) return (CheckStatus.Failed, null);
        _isChecking = true;
        try
        {
            var apiUrl = config["UpdateApiUrl"];
            if (string.IsNullOrWhiteSpace(apiUrl))
                return (CheckStatus.NotConfigured, null);

            var current = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString(3);
            var client = ((App)System.Windows.Application.Current).GetService<HttpClient>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.UserAgent.ParseAdd($"NetSpeedTest/{current ?? "1.0.0"}");
            using var resp = await client.SendAsync(req, cts.Token);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString()?.Trim().TrimStart('v', 'V');
            var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.TryGetProperty("browser_download_url", out var du))
                    {
                        downloadUrl = du.GetString();
                        break;
                    }
                }
            }
            downloadUrl ??= root.TryGetProperty("html_url", out var h) ? h.GetString() : null;

            if (string.IsNullOrWhiteSpace(tag) || !Version.TryParse(tag, out var rv)
                || !Version.TryParse(current, out var cv) || downloadUrl == null)
                return (CheckStatus.Failed, null);

            if (rv > cv)
                return (CheckStatus.HasUpdate, new UpdateInfo(tag, body, downloadUrl));

            return (CheckStatus.NoUpdate, null);
        }
        catch (Exception ex)
        {
            Services.Logger.Log($"Update check failed: {ex.Message}");
            return (CheckStatus.Failed, null);
        }
        finally { _isChecking = false; }
    }
}
