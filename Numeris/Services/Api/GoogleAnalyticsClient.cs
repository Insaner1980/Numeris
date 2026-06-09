using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Numeris.Services.Api;

public sealed class GoogleAnalyticsClient
{
    public const string Scope = "https://www.googleapis.com/auth/analytics.readonly";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HttpClient Http = new();

    public async Task<List<GoogleAnalyticsApiRow>> RunReportAsync(
        string accessToken,
        string propertyId,
        string startDate,
        string endDate,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<string> metrics,
        int limit)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Google OAuth refresh did not return an access token");
        }
        if (string.IsNullOrWhiteSpace(propertyId))
        {
            throw new InvalidOperationException("Google Analytics property ID is missing");
        }

        var url = $"https://analyticsdata.googleapis.com/v1beta/properties/{propertyId.Trim()}:runReport";
        var requestBody = new RunReportRequest
        {
            DateRanges = new List<DateRange> { new() { StartDate = startDate, EndDate = endDate } },
            Dimensions = dimensions.Select(name => new NamedValue { Name = name }).ToList(),
            Metrics = metrics.Select(name => new NamedValue { Name = name }).ToList(),
            Limit = limit,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());

        using var response = await Http.SendAsync(req).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("Google Analytics Data API", "properties.runReport", response.StatusCode, ApiErrorMessage.FromBody(body));
        }

        var parsed = JsonSerializer.Deserialize<RunReportResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse Google Analytics Data API response");
        return (parsed.Rows ?? new List<Row>()).Select(row => ToApiRow(dimensions, metrics, row)).ToList();
    }

    private static GoogleAnalyticsApiRow ToApiRow(IReadOnlyList<string> dimensions, IReadOnlyList<string> metrics, Row row)
    {
        var result = new GoogleAnalyticsApiRow();
        for (var i = 0; i < dimensions.Count; i++)
        {
            result.Dimensions[dimensions[i]] = row.DimensionValues.ElementAtOrDefault(i)?.Value ?? "";
        }
        for (var i = 0; i < metrics.Count; i++)
        {
            var raw = row.MetricValues.ElementAtOrDefault(i)?.Value ?? "0";
            result.Metrics[metrics[i]] = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0.0;
        }
        return result;
    }

    private sealed class RunReportRequest
    {
        public List<DateRange> DateRanges { get; set; } = new();
        public List<NamedValue> Dimensions { get; set; } = new();
        public List<NamedValue> Metrics { get; set; } = new();
        public int Limit { get; set; }
    }

    private sealed class DateRange
    {
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
    }

    private sealed class NamedValue
    {
        public string Name { get; set; } = "";
    }

    private sealed class RunReportResponse
    {
        public List<Row>? Rows { get; set; }
    }

    private sealed class Row
    {
        public List<ValueHolder> DimensionValues { get; set; } = new();
        public List<ValueHolder> MetricValues { get; set; } = new();
    }

    private sealed class ValueHolder
    {
        public string Value { get; set; } = "";
    }
}

public sealed class GoogleAnalyticsApiRow
{
    public Dictionary<string, string> Dimensions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Metrics { get; } = new(StringComparer.Ordinal);

    public string Dimension(string name) => Dimensions.TryGetValue(name, out var value) ? value : "";
    public long LongMetric(string name) => Metrics.TryGetValue(name, out var value) ? (long)Math.Round(value) : 0L;
    public double DoubleMetric(string name) => Metrics.TryGetValue(name, out var value) ? value : 0.0;
}
