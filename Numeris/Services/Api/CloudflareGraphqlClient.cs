using System;
using System.Collections.Generic;
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
        query NumerisDailyTraffic($zoneTag: String!, $since: Date!, $until: Date!) {
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

    public async Task ValidateZoneAsync(string apiToken, string zoneId, string expectedDomain)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/zones/{zoneId.Trim()}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(apiToken));
        using var response = await Http.SendAsync(req).ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<ZoneDetailsResponse>(JsonOptions).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not parse Cloudflare zone response");

        if (!response.IsSuccessStatusCode || !body.Success)
        {
            var msg = body.Errors?.Count > 0 ? body.Errors[0].Message : $"Cloudflare returned HTTP {(int)response.StatusCode}";
            throw new InvalidOperationException(msg);
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
            variables = new { zoneTag = zoneId, since, until },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, GraphqlEndpoint)
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(apiToken));

        using var response = await Http.SendAsync(req).ConfigureAwait(false);
        var graphql = await response.Content.ReadFromJsonAsync<GraphqlResponse>(JsonOptions).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not parse Cloudflare analytics response");

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Cloudflare returned HTTP {(int)response.StatusCode}");
        }
        if (graphql.Errors is { Count: > 0 } errors)
        {
            throw new InvalidOperationException(errors[0].Message);
        }
        var zone = graphql.Data?.Viewer?.Zones?.Count > 0 ? graphql.Data.Viewer.Zones[0] : null;
        if (zone is null)
        {
            throw new InvalidOperationException("Cloudflare did not return analytics for this zone");
        }

        var result = new CloudflareTrafficResult();
        foreach (var group in zone.HttpRequests1dGroups ?? new())
        {
            var date = group.Dimensions?.Date ?? "";
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
        return result;
    }

    private sealed class ZoneDetailsResponse
    {
        public bool Success { get; set; }
        public List<ZoneError>? Errors { get; set; }
        public ZoneDetails? Result { get; set; }
    }

    private sealed class ZoneError { public string Message { get; set; } = ""; }
    private sealed class ZoneDetails { public string Name { get; set; } = ""; }

    private sealed class GraphqlResponse
    {
        public GraphqlData? Data { get; set; }
        public List<GraphqlError>? Errors { get; set; }
    }
    private sealed class GraphqlError { public string Message { get; set; } = ""; }
    private sealed class GraphqlData { public GraphqlViewer? Viewer { get; set; } }
    private sealed class GraphqlViewer { public List<GraphqlZone>? Zones { get; set; } }

    private sealed class GraphqlZone
    {
        public List<HttpRequestsGroup>? HttpRequests1dGroups { get; set; }
    }
    private sealed class HttpRequestsGroup
    {
        public HttpDimensions? Dimensions { get; set; }
        public HttpSums? Sum { get; set; }
        public HttpUniques? Uniq { get; set; }
    }
    private sealed class HttpDimensions { public string Date { get; set; } = ""; }
    private sealed class HttpSums
    {
        public long PageViews { get; set; }
        public long Requests { get; set; }
        public long CachedRequests { get; set; }
        public long CachedBytes { get; set; }
        public long Bytes { get; set; }
        public long Threats { get; set; }
        public List<ResponseStatusEntry>? ResponseStatusMap { get; set; }
    }
    private sealed class ResponseStatusEntry
    {
        public int EdgeResponseStatus { get; set; }
        public long Requests { get; set; }
    }
    private sealed class HttpUniques { public long Uniques { get; set; } }

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
