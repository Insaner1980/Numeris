using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Numeris.Services.Api;

public sealed class BingWebmasterClient
{
    private const string Endpoint = "https://ssl.bing.com/webmaster/api.svc/json/";
    private static readonly HttpClient Http = new();

    public async Task<JsonDocument> CallAsync(string apiKey, string method, IReadOnlyDictionary<string, string?> parameters)
    {
        var query = new List<string>
        {
            $"apikey={Uri.EscapeDataString(apiKey.Trim())}",
        };

        foreach (var (key, value) in parameters)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
            }
        }

        var url = $"{Endpoint}{method}?{string.Join("&", query)}";
        using var response = await Http.GetAsync(url).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Bing Webmaster {method} returned HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonDocument.Parse(body);
    }

    public static JsonElement Unwrap(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("d", out var d)) return d;
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                {
                    return property.Value;
                }
            }
        }
        return root;
    }
}
