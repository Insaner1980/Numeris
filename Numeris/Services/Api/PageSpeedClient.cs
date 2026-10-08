using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Numeris.Services.Api;

public sealed class PageSpeedClient
{
    private const string Endpoint = "https://pagespeedonline.googleapis.com/pagespeedonline/v5/runPagespeed";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(75) };
    private readonly HttpClient _http;

    public PageSpeedClient(HttpClient? http = null) => _http = http ?? Http;

    public async Task<JsonDocument> RunAsync(string apiKey, string url, string strategy)
    {
        var requestUrl =
            $"{Endpoint}?url={Uri.EscapeDataString(url)}" +
            $"&strategy={Uri.EscapeDataString(strategy.ToUpperInvariant())}" +
            "&category=PERFORMANCE&category=ACCESSIBILITY&category=BEST_PRACTICES&category=SEO" +
            $"&key={Uri.EscapeDataString(apiKey.Trim())}";

        using var response = await _http.GetAsync(requestUrl).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("PageSpeed", "runPagespeed", response.StatusCode, ApiErrorMessage.FromBody(body));
        }
        var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("lighthouseResult", out var result)
            || result.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidOperationException("PageSpeed response contained no Lighthouse result");
        }
        try
        {
            if (result.TryGetProperty("runtimeError", out var runtimeError)
                && runtimeError.ValueKind != JsonValueKind.Null)
            {
                throw new InvalidOperationException("PageSpeed Lighthouse run failed");
            }
            _ = AnalysisTimestamp(document.RootElement);
        }
        catch
        {
            document.Dispose();
            throw;
        }
        return document;
    }

    public static double? Score(JsonElement root, string category)
    {
        if (root.TryGetProperty("lighthouseResult", out var lh)
            && lh.TryGetProperty("categories", out var categories)
            && categories.TryGetProperty(category, out var cat)
            && cat.TryGetProperty("score", out var score)
            && score.ValueKind == JsonValueKind.Number)
        {
            var value = score.GetDouble();
            if (!double.IsFinite(value))
            {
                throw new InvalidOperationException("PageSpeed response contained a non-finite category score");
            }
            return value;
        }
        return null;
    }

    public static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static double? Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    public static string AnalysisTimestamp(JsonElement root)
    {
        var timestamp = root.TryGetProperty("analysisUTCTimestamp", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            throw new InvalidOperationException("PageSpeed response contained no valid analysis timestamp");
        }
        return parsed.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<string> Warnings(JsonElement root)
    {
        var result = new List<string>();
        if (root.TryGetProperty("lighthouseResult", out var lh)
            && lh.TryGetProperty("runWarnings", out var warnings)
            && warnings.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in warnings.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } warning)
                {
                    result.Add(warning);
                }
            }
        }
        return result;
    }
}
