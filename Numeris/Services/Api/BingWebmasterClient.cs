using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Numeris.Services.Api;

public sealed class BingWebmasterClient
{
    private readonly HttpClient _http;

    public BingWebmasterClient(HttpClient? http = null) => _http = http ?? Http;

    private const string DateFormat = "yyyy-MM-dd";

    private const string Endpoint = "https://ssl.bing.com/webmaster/api.svc/json/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

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
        using var response = await _http.GetAsync(url).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("Bing Webmaster", method, response.StatusCode, ApiErrorMessage.FromBody(body));
        }
        return JsonDocument.Parse(body);
    }

    public static JsonElement Unwrap(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("d", out var d)) return d;
            var nested = root.EnumerateObject().FirstOrDefault(property => property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object);
            if (nested.Value.ValueKind != JsonValueKind.Undefined) return nested.Value;
        }
        return root;
    }

    public static string NormalizeDate(string value)
    {
        var match = Regex.Match(value, @"^/Date\((-?\d+)(?:[+-]\d{4})?\)/$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
        if (match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToString(DateFormat, CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new FormatException("Invalid Bing report date.");
            }
        }
        if (DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day.ToString(DateFormat, CultureInfo.InvariantCulture);
        }
        if (value.Contains('T') && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            return timestamp.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture);
        }
        throw new FormatException("Invalid Bing report date.");
    }
}
