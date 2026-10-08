using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Numeris.Services.Api;

public sealed class CruxClient
{
    private const string HistoryEndpoint = "https://chromeuxreport.googleapis.com/v1/records:queryHistoryRecord";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new NullableDoubleJsonConverter() },
    };

    public CruxClient(HttpClient? http = null) => _http = http ?? Http;

    public async Task<CruxHistoryResponse> QueryHistoryAsync(string apiKey, string targetType, string target, string? formFactor)
    {
        var payload = targetType.Equals("url", StringComparison.OrdinalIgnoreCase)
            ? new CruxHistoryRequest { Url = target, FormFactor = formFactor }
            : new CruxHistoryRequest { Origin = target, FormFactor = formFactor };

        var url = $"{HistoryEndpoint}?key={Uri.EscapeDataString(apiKey.Trim())}";
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("CrUX", "queryHistoryRecord", response.StatusCode, ApiErrorMessage.FromBody(body));
        }

        var parsed = JsonSerializer.Deserialize<CruxHistoryResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse CrUX response");
        if (parsed.Record is null) throw new InvalidOperationException("CrUX response contained no record");
        parsed.RawJson = body;
        return parsed;
    }

    private sealed class CruxHistoryRequest
    {
        public string? Origin { get; set; }
        public string? Url { get; set; }
        public string? FormFactor { get; set; }
    }

    private sealed class NullableDoubleJsonConverter : JsonConverter<double?>
    {
        public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.String when reader.GetString()?.Equals("NaN", StringComparison.OrdinalIgnoreCase) == true => null,
                JsonTokenType.String when double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    && double.IsFinite(value) => value,
                JsonTokenType.Null => null,
                _ => throw new JsonException("Expected number, numeric string, null or NaN"),
            };

        public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}

public sealed class CruxHistoryResponse
{
    public CruxRecord? Record { get; set; }
    public string RawJson { get; set; } = "";
}

public sealed class CruxRecord
{
    public CruxKey? Key { get; set; }
    public Dictionary<string, CruxMetric>? Metrics { get; set; }
    public List<CruxCollectionPeriod>? CollectionPeriods { get; set; }
}

public sealed class CruxKey
{
    public string? Origin { get; set; }
    public string? Url { get; set; }
    public string? FormFactor { get; set; }
}

public sealed class CruxMetric
{
    public List<CruxHistogramTimeseries>? HistogramTimeseries { get; set; }
    public CruxPercentilesTimeseries? PercentilesTimeseries { get; set; }
    public string RawJson { get; set; } = "";
}

public sealed class CruxHistogramTimeseries
{
    public double? Start { get; set; }
    public double? End { get; set; }
    public List<double?>? Densities { get; set; }
}

public sealed class CruxPercentilesTimeseries
{
    public List<double?>? P75s { get; set; }
}

public sealed class CruxCollectionPeriod
{
    public CruxDate? FirstDate { get; set; }
    public CruxDate? LastDate { get; set; }
}

public sealed class CruxDate
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }

    public string ToIsoDate() => $"{Year:0000}-{Month:00}-{Day:00}";
}
