using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Numeris.Services.Api;

public sealed class CloudflareGraphqlClient
{
    private const string ApiBase = "https://api.cloudflare.com/client/v4";
    private const string GraphqlEndpoint = "https://api.cloudflare.com/client/v4/graphql";

    private const string DailyTrafficQuery = """
        query NumerisDailyTraffic($zoneTag: String!, $since: Date!, $until: Date!, $adaptiveSince: Time!, $adaptiveUntil: Time!) {
          viewer {
            zones(filter: { zoneTag: $zoneTag }) {
              httpRequests1dGroups(
                limit: 10000
                filter: { date_geq: $since, date_leq: $until }
                orderBy: [date_ASC]
              ) {
                dimensions { date }
                sum {
                  pageViews
                  requests
                  cachedRequests
                  cachedBytes
                  bytes
                  threats
                  responseStatusMap {
                    edgeResponseStatus
                    requests
                  }
                }
                uniq { uniques }
              }
              topCountries: httpRequestsAdaptiveGroups(
                limit: 10000
                orderBy: [count_DESC]
                filter: { datetime_geq: $adaptiveSince, datetime_lt: $adaptiveUntil, requestSource: "eyeball" }
              ) {
                count
                sum { visits }
                dimensions { date clientCountryName }
              }
              topPages: httpRequestsAdaptiveGroups(
                limit: 10000
                orderBy: [count_DESC]
                filter: { datetime_geq: $adaptiveSince, datetime_lt: $adaptiveUntil, requestSource: "eyeball" }
              ) {
                count
                sum { visits }
                dimensions { date clientRequestPath }
              }
            }
          }
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HttpClient Http = new();
    private readonly HttpClient _http;

    public CloudflareGraphqlClient(HttpClient? http = null) => _http = http ?? Http;

    public async Task ValidateZoneAsync(string apiToken, string zoneId, string expectedDomain)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/zones/{zoneId.Trim()}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(apiToken));
        using var response = await _http.SendAsync(req).ConfigureAwait(false);
        var rawBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("Cloudflare", "zone.details", response.StatusCode, ApiErrorMessage.FromBody(rawBody));
        }
        var body = JsonSerializer.Deserialize<ZoneDetailsResponse>(rawBody, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse Cloudflare zone response");

        if (!body.Success)
        {
            var msg = body.Errors?.Count > 0 ? body.Errors[0].Message : $"Cloudflare returned HTTP {(int)response.StatusCode}";
            throw new InvalidOperationException(ApiErrorMessage.Sanitize(msg));
        }
        if (body.Result is null)
        {
            throw new InvalidOperationException("Cloudflare did not return zone details");
        }
        if (!string.Equals(body.Result.Name, expectedDomain, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Token/zone points to {body.Result.Name}, not {expectedDomain}");
        }
    }

    public async Task<CloudflareTrafficResult> FetchDailyTrafficAsync(string apiToken, string zoneId, string since, string until)
    {
        var requestBody = new
        {
            query = DailyTrafficQuery,
            variables = new
            {
                zoneTag = zoneId,
                since,
                until,
                adaptiveSince = ToGraphqlStartTime(AdaptiveStartDate(since, until)),
                adaptiveUntil = ToGraphqlExclusiveEndTime(until),
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, GraphqlEndpoint)
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(apiToken));

        using var response = await _http.SendAsync(req).ConfigureAwait(false);
        var rawBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("Cloudflare", "analytics", response.StatusCode, ApiErrorMessage.FromBody(rawBody));
        }
        return ParseTrafficResponse(rawBody, since, until);
    }

    private static CloudflareTrafficResult ParseTrafficResponse(string rawBody, string since, string until)
    {
        var graphql = JsonSerializer.Deserialize<GraphqlResponse>(rawBody, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse Cloudflare analytics response");
        if (graphql.Errors is { Count: > 0 } errors)
        {
            throw new InvalidOperationException(ApiErrorMessage.Sanitize(errors[0].Message));
        }
        var zones = graphql.Data?.Viewer?.Zones;
        if (zones is not { Count: 1 } || zones[0] is null)
        {
            throw new InvalidOperationException("Cloudflare did not return one analytics zone");
        }
        var zone = zones[0];
        if (zone.HttpRequests1dGroups is null || zone.TopCountries is null || zone.TopPages is null)
        {
            throw new InvalidOperationException("Cloudflare returned incomplete analytics groups");
        }

        var result = new CloudflareTrafficResult();
        result.BreakdownStartDate = AdaptiveStartDate(since, until);
        result.BreakdownDate = until;
        foreach (var group in zone.HttpRequests1dGroups)
        {
            var date = ProviderDate(group.Dimensions?.Date);
            result.Daily.Add(new CloudflareTrafficRow
            {
                Date = date,
                Pageviews = group.Sum?.PageViews ?? 0,
                UniqueVisitors = group.Uniq?.Uniques ?? 0,
                Requests = group.Sum?.Requests ?? 0,
                CachedRequests = group.Sum?.CachedRequests ?? 0,
                CachedBytes = group.Sum?.CachedBytes ?? 0,
                TotalBytes = group.Sum?.Bytes ?? 0,
                Threats = group.Sum?.Threats ?? 0,
            });
            foreach (var s in group.Sum?.ResponseStatusMap ?? new())
            {
                result.StatusCodes.Add(new CloudflareStatusRow
                {
                    Date = date,
                    StatusCode = s.EdgeResponseStatus,
                    Requests = s.Requests,
                });
            }
        }
        foreach (var group in zone.TopCountries)
        {
            var country = group.Dimensions?.ClientCountryName ?? "";
            if (string.IsNullOrWhiteSpace(country))
            {
                continue;
            }

            result.Countries.Add(new CloudflareCountryRow
            {
                Date = ProviderDate(group.Dimensions!.Date),
                Country = country,
                Value = BreakdownValue(group),
            });
        }
        foreach (var group in zone.TopPages)
        {
            var path = group.Dimensions?.ClientRequestPath ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            result.Pages.Add(new CloudflarePageRow
            {
                Date = ProviderDate(group.Dimensions!.Date),
                Path = path,
                Value = BreakdownValue(group),
            });
        }
        return result;
    }

    private sealed class ZoneDetailsResponse
    {
        [System.Text.Json.Serialization.JsonInclude] public bool Success { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<ZoneError>? Errors { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public ZoneDetails? Result { get; set; }
    }

    private sealed class ZoneError { public string Message { get; set; } = ""; }
    private sealed class ZoneDetails { public string Name { get; set; } = ""; }

    private sealed class GraphqlResponse
    {
        [System.Text.Json.Serialization.JsonInclude] public GraphqlData? Data { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<GraphqlError>? Errors { get; set; }
    }
    private sealed class GraphqlError { public string Message { get; set; } = ""; }
    private sealed class GraphqlData { [System.Text.Json.Serialization.JsonInclude] public GraphqlViewer? Viewer { get; set; } }
    private sealed class GraphqlViewer { [System.Text.Json.Serialization.JsonInclude] public List<GraphqlZone>? Zones { get; set; } }

    private sealed class GraphqlZone
    {
        [System.Text.Json.Serialization.JsonInclude] public List<HttpRequestsGroup>? HttpRequests1dGroups { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<HttpRequestsAdaptiveGroup>? TopCountries { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<HttpRequestsAdaptiveGroup>? TopPages { get; set; }
    }
    private sealed class HttpRequestsGroup
    {
        [System.Text.Json.Serialization.JsonInclude] public HttpDimensions? Dimensions { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public HttpSums? Sum { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public HttpUniques? Uniq { get; set; }
    }
    private sealed class HttpDimensions { public string Date { get; set; } = ""; }
    private sealed class HttpSums
    {
        [System.Text.Json.Serialization.JsonInclude] public long PageViews { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public long Requests { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public long CachedRequests { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public long CachedBytes { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public long Bytes { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public long Threats { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<ResponseStatusEntry>? ResponseStatusMap { get; set; }
    }
    private sealed class ResponseStatusEntry
    {
        [System.Text.Json.Serialization.JsonInclude] public int EdgeResponseStatus { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public long Requests { get; set; }
    }
    private sealed class HttpUniques { [System.Text.Json.Serialization.JsonInclude] public long Uniques { get; set; } }

    private sealed class HttpRequestsAdaptiveGroup
    {
        [System.Text.Json.Serialization.JsonInclude] public long Count { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public HttpAdaptiveSums? Sum { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public HttpAdaptiveDimensions? Dimensions { get; set; }
    }
    private sealed class HttpAdaptiveSums { [System.Text.Json.Serialization.JsonInclude] public long Visits { get; set; } }
    private sealed class HttpAdaptiveDimensions
    {
        public string Date { get; set; } = "";
        public string ClientCountryName { get; set; } = "";
        public string ClientRequestPath { get; set; } = "";
    }

    private static long BreakdownValue(HttpRequestsAdaptiveGroup group)
        => group.Sum?.Visits > 0 ? group.Sum.Visits : group.Count;

    private static string ProviderDate(string? date)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new InvalidOperationException("Cloudflare returned an invalid analytics date");
        }
        return date;
    }

    private static string ToGraphqlStartTime(string date)
        => ParseDate(date).ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string AdaptiveStartDate(string since, string until)
    {
        // Adaptive breakdowns allow 30-day queries; daily totals support a longer history.
        var requested = ParseDate(since);
        var earliest = ParseDate(until).AddDays(-29);
        return (requested < earliest ? earliest : requested).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string ToGraphqlExclusiveEndTime(string date)
        => ParseDate(date).AddDays(1).ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string date)
    {
        if (DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Invalid Cloudflare analytics date: {date}", nameof(date));
    }

    private static string NormalizeBearerToken(string token)
    {
        token = token.Trim();
        const string bearerPrefix = "Bearer ";
        return token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? token[bearerPrefix.Length..].Trim()
            : token;
    }
}

public sealed class CloudflareTrafficResult
{
    public List<CloudflareTrafficRow> Daily { get; set; } = new();
    public List<CloudflareStatusRow> StatusCodes { get; set; } = new();
    public List<CloudflareCountryRow> Countries { get; set; } = new();
    public List<CloudflarePageRow> Pages { get; set; } = new();
    public string BreakdownDate { get; set; } = "";
    public string BreakdownStartDate { get; set; } = "";
}

public sealed class CloudflareTrafficRow
{
    public string Date { get; set; } = "";
    public long Pageviews { get; set; }
    public long UniqueVisitors { get; set; }
    public long Requests { get; set; }
    public long CachedRequests { get; set; }
    public long CachedBytes { get; set; }
    public long TotalBytes { get; set; }
    public long Threats { get; set; }
}

public sealed class CloudflareStatusRow
{
    public string Date { get; set; } = "";
    public int StatusCode { get; set; }
    public long Requests { get; set; }
}

public sealed class CloudflareCountryRow
{
    public string Date { get; set; } = "";
    public string Country { get; set; } = "";
    public long Value { get; set; }
}

public sealed class CloudflarePageRow
{
    public string Date { get; set; } = "";
    public string Path { get; set; } = "";
    public long Value { get; set; }
}
