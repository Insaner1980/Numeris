using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace Numeris.Services.Api;

public sealed class UptimeClient
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12),
        };
        return client;
    }

    public async Task<UptimeProbeResult> ProbeAsync(string domain)
    {
        var normalized = NormalizeDomain(domain);
        var url = $"https://{normalized}";
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
            sw.Stop();
            var code = (int)response.StatusCode;
            var status = response.IsSuccessStatusCode || ((int)response.StatusCode is >= 300 and < 400) ? "up" : "down";
            return new UptimeProbeResult
            {
                Domain = normalized,
                Status = status,
                StatusCode = code,
                ResponseMs = sw.ElapsedMilliseconds,
                ErrorMessage = null,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new UptimeProbeResult
            {
                Domain = normalized,
                Status = "down",
                StatusCode = null,
                ResponseMs = null,
                ErrorMessage = ex.Message,
            };
        }
    }

    public static string NormalizeDomain(string domain)
    {
        var d = domain.Trim();
        if (d.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) d = d[8..];
        else if (d.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) d = d[7..];
        return d.TrimEnd('/').ToLowerInvariant();
    }
}

public sealed class UptimeProbeResult
{
    public string Domain { get; set; } = "";
    public string Status { get; set; } = "down";
    public int? StatusCode { get; set; }
    public long? ResponseMs { get; set; }
    public string? ErrorMessage { get; set; }
}
