using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Numeris.Services.Api;

public sealed class CruxClient
{
    private const string HistoryEndpoint = "https://chromeuxreport.googleapis.com/v1/records:queryHistoryRecord";
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<CruxHistoryResponse> QueryHistoryAsync(string apiKey, string targetType, string target, string? formFactor)
    {
        var payload = targetType.Equals("url", StringComparison.OrdinalIgnoreCase)
            ? new CruxHistoryRequest { Url = target, FormFactor = formFactor }
            : new CruxHistoryRequest { Origin = target, FormFactor = formFactor };

        var url = $"{HistoryEndpoint}?key={Uri.EscapeDataString(apiKey.Trim())}";
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(url, content).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"CrUX returned HTTP {(int)response.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<CruxHistoryResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse CrUX response");
        parsed.RawJson = body;
        return parsed;
    }

    private sealed class CruxHistoryRequest
    {
        public string? Origin { get; set; }
        public string? Url { get; set; }
        public string? FormFactor { get; set; }
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
    public CruxCollectionPeriods? CollectionPeriods { get; set; }
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

public sealed class CruxCollectionPeriods
{
    public List<CruxCollectionPeriod>? CollectionPeriods { get; set; }
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
