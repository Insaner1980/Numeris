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

public sealed class CloudflareRumClient
{
    private const string ApiBase = "https://api.cloudflare.com/client/v4";
    private const string GraphqlEndpoint = "https://api.cloudflare.com/client/v4/graphql";

    private const string RollupQuery = """
        query NumerisWebAnalytics(
          $accountTag: String!
          $siteTag: String!
          $since: Time!
          $until: Time!
          $sinceDate: Date!
          $untilDate: Date!
        ) {
          viewer {
            accounts(filter: { accountTag: $accountTag }) {
              daily: rumPageloadEventsAdaptiveGroups(
                limit: 10000
                filter: { siteTag: $siteTag, date_geq: $sinceDate, date_leq: $untilDate, bot: 0 }
                orderBy: [date_ASC]
              ) {
                dimensions { date }
                sum { visits }
                count
              }
              referrers: rumPageloadEventsAdaptiveGroups(
                limit: 10000
                filter: { siteTag: $siteTag, datetime_geq: $since, datetime_leq: $until, bot: 0 }
                orderBy: [sum_visits_DESC]
              ) {
                dimensions { date refererHost }
                sum { visits }
              }
              pages: rumPageloadEventsAdaptiveGroups(
                limit: 10000
                filter: { siteTag: $siteTag, datetime_geq: $since, datetime_leq: $until, bot: 0 }
                orderBy: [count_DESC]
              ) {
                dimensions { date requestPath }
                count
              }
              countries: rumPageloadEventsAdaptiveGroups(
                limit: 10000
                filter: { siteTag: $siteTag, datetime_geq: $since, datetime_leq: $until, bot: 0 }
                orderBy: [sum_visits_DESC]
              ) {
                dimensions { date countryName }
                sum { visits }
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

    public CloudflareRumClient(HttpClient? http = null) => _http = http ?? Http;

    public async Task<List<WebAnalyticsSite>> ListSitesAsync(string apiToken, string accountId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/accounts/{accountId.Trim()}/rum/site_info/list");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(apiToken));

        using var response = await _http.SendAsync(req).ConfigureAwait(false);
        var rawBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        SiteInfoListResponse? body = null;
        try { body = JsonSerializer.Deserialize<SiteInfoListResponse>(rawBody, JsonOptions); } catch (JsonException) { /* A malformed provider response is reported below without exposing its raw body. */ }

        if (!response.IsSuccessStatusCode || body is null || !body.Success)
        {
            var apiMsg = body?.Errors?.Count > 0 ? body.Errors[0].Message : "(no error message)";
            var apiCode = body?.Errors?.Count > 0 ? $" code={body.Errors[0].Code}" : "";
            throw new ApiRequestException("Cloudflare Web Analytics", "rum.site_info.list", response.StatusCode,
                $"{ApiErrorMessage.Sanitize(apiMsg)}{apiCode}. " +
                "RUM site discovery requires 'Account Settings: Read' for this account. " +
                "GraphQL sync uses 'Account Analytics: Read', so you can add site tags manually and sync without discovery.");
        }

        return ParseSites(body);
    }

    private static List<WebAnalyticsSite> ParseSites(SiteInfoListResponse body)
    {
        var result = new List<WebAnalyticsSite>();
        foreach (var site in body.Result ?? new())
        {
            if (string.IsNullOrWhiteSpace(site.SiteTag)) continue;
            var host = site.Rules?
                .Select(r => r.Host)
                .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h))
                ?? (!string.IsNullOrWhiteSpace(site.Host) ? site.Host : site.Ruleset?.ZoneName);
            if (string.IsNullOrWhiteSpace(host)) continue;
            result.Add(new WebAnalyticsSite
            {
                Host = host.Trim().ToLowerInvariant(),
                SiteTag = site.SiteTag.Trim(),
            });
        }
        return result.DistinctBy(s => s.Host).OrderBy(s => s.Host, StringComparer.Ordinal).ToList();
    }

    public async Task<WebAnalyticsRollup> FetchRollupAsync(
        string apiToken,
        string accountId,
        string siteTag,
        string sinceIso, string untilIso,
        string sinceDate, string untilDate)
    {
        var requestBody = new
        {
            query = RollupQuery,
            variables = new
            {
                accountTag = accountId,
                siteTag,
                since = sinceIso,
                until = untilIso,
                sinceDate,
                untilDate,
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
            GraphqlResponse? graphql = null;
            try { graphql = JsonSerializer.Deserialize<GraphqlResponse>(rawBody, JsonOptions); } catch (JsonException) { /* A malformed provider response is reported below without exposing its raw body. */ }
            var msg = graphql?.Errors?.Count > 0 ? graphql.Errors[0].Message : "(no error message)";
            throw new ApiRequestException("Cloudflare Web Analytics", "graphql", response.StatusCode, ApiErrorMessage.Sanitize(msg));
        }

        return ParseRollupResponse(rawBody, sinceDate);
    }

    private static WebAnalyticsRollup ParseRollupResponse(string rawBody, string sinceDate)
    {
        GraphqlResponse? graphql = null;
        try { graphql = JsonSerializer.Deserialize<GraphqlResponse>(rawBody, JsonOptions); } catch (JsonException) { /* A malformed provider response is reported below without exposing its raw body. */ }

        if (graphql is null)
        {
            throw new InvalidOperationException("Could not parse Cloudflare Web Analytics response");
        }

        if (graphql.Errors is { Count: > 0 } errs)
        {
            throw new InvalidOperationException(ApiErrorMessage.Sanitize(errs[0].Message));
        }
        var accounts = graphql.Data?.Viewer?.Accounts;
        if (accounts is not { Count: 1 } || accounts[0] is null)
        {
            throw new InvalidOperationException("Cloudflare did not return one analytics account");
        }
        var account = accounts[0];
        if (account.Daily is null || account.Referrers is null || account.Pages is null || account.Countries is null)
        {
            throw new InvalidOperationException("Cloudflare returned incomplete Web Analytics groups");
        }

        var result = new WebAnalyticsRollup { StartDate = sinceDate };
        foreach (var d in account.Daily)
        {
            result.Daily.Add(new WebAnalyticsDailyRow
            {
                Date = ProviderDate(d.Dimensions?.Date),
                Visits = d.Sum?.Visits ?? 0,
                PageViews = d.Count,
            });
        }
        foreach (var r in account.Referrers)
        {
            result.Referrers.Add(new WebAnalyticsCategoryRow
            {
                Date = ProviderDate(r.Dimensions?.Date),
                Key = r.Dimensions?.RefererHost ?? "",
                Visits = r.Sum?.Visits ?? 0,
            });
        }
        foreach (var p in account.Pages)
        {
            result.Pages.Add(new WebAnalyticsPathRow
            {
                Date = ProviderDate(p.Dimensions?.Date),
                Path = p.Dimensions?.RequestPath ?? "",
                PageViews = p.Count,
            });
        }
        foreach (var c in account.Countries)
        {
            result.Countries.Add(new WebAnalyticsCategoryRow
            {
                Date = ProviderDate(c.Dimensions?.Date),
                Key = c.Dimensions?.CountryName ?? "",
                Visits = c.Sum?.Visits ?? 0,
            });
        }
        return result;
    }

    private static string ProviderDate(string? date)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new InvalidOperationException("Cloudflare returned an invalid Web Analytics date");
        }
        return date;
    }

    private sealed class SiteInfoListResponse
    {
        [System.Text.Json.Serialization.JsonInclude] public bool Success { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<SiteInfo>? Result { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<ApiError>? Errors { get; set; }
    }
    private sealed class ApiError
    {
        public string Message { get; set; } = "";
        [System.Text.Json.Serialization.JsonInclude] public int Code { get; set; }
    }
    private sealed class SiteInfo
    {
        [JsonPropertyName("site_tag")] public string? SiteTag { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public string? Host { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<RumRule>? Rules { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public RumRuleset? Ruleset { get; set; }
    }
    private sealed class RumRule { [System.Text.Json.Serialization.JsonInclude] public string? Host { get; set; } }
    private sealed class RumRuleset { [JsonPropertyName("zone_name")] public string? ZoneName { get; set; } }

    private sealed class GraphqlResponse
    {
        [System.Text.Json.Serialization.JsonInclude] public GraphqlData? Data { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<GraphqlError>? Errors { get; set; }
    }
    private sealed class GraphqlError { public string Message { get; set; } = ""; }
    private sealed class GraphqlData { [System.Text.Json.Serialization.JsonInclude] public GraphqlViewer? Viewer { get; set; } }
    private sealed class GraphqlViewer { [System.Text.Json.Serialization.JsonInclude] public List<GraphqlAccount>? Accounts { get; set; } }
    private sealed class GraphqlAccount
    {
        [System.Text.Json.Serialization.JsonInclude] public List<DailyGroup>? Daily { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<ReferrerGroup>? Referrers { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<PageGroup>? Pages { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public List<CountryGroup>? Countries { get; set; }
    }
    private sealed class DailyGroup
    {
        [System.Text.Json.Serialization.JsonInclude] public DailyDimensions? Dimensions { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public VisitsSum? Sum { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public long Count { get; set; }
    }
    private sealed class DailyDimensions { public string Date { get; set; } = ""; }
    private sealed class VisitsSum { [System.Text.Json.Serialization.JsonInclude] public long Visits { get; set; } }
    private sealed class ReferrerGroup
    {
        [System.Text.Json.Serialization.JsonInclude] public ReferrerDimensions? Dimensions { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public VisitsSum? Sum { get; set; }
    }
    private sealed class ReferrerDimensions
    {
        public string Date { get; set; } = "";
        public string RefererHost { get; set; } = "";
    }
    private sealed class PageGroup
    {
        [System.Text.Json.Serialization.JsonInclude] public PageDimensions? Dimensions { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public long Count { get; set; }
    }
    private sealed class PageDimensions
    {
        public string Date { get; set; } = "";
        public string RequestPath { get; set; } = "";
    }
    private sealed class CountryGroup
    {
        [System.Text.Json.Serialization.JsonInclude] public CountryDimensions? Dimensions { get; set; }
        [System.Text.Json.Serialization.JsonInclude] public VisitsSum? Sum { get; set; }
    }
    private sealed class CountryDimensions
    {
        public string Date { get; set; } = "";
        public string CountryName { get; set; } = "";
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

public sealed class WebAnalyticsSite
{
    public string Host { get; set; } = "";
    public string SiteTag { get; set; } = "";
}

public sealed class WebAnalyticsRollup
{
    public string StartDate { get; set; } = "";
    public List<WebAnalyticsDailyRow> Daily { get; set; } = new();
    public List<WebAnalyticsCategoryRow> Referrers { get; set; } = new();
    public List<WebAnalyticsPathRow> Pages { get; set; } = new();
    public List<WebAnalyticsCategoryRow> Countries { get; set; } = new();
}

public sealed class WebAnalyticsDailyRow
{
    public string Date { get; set; } = "";
    public long Visits { get; set; }
    public long PageViews { get; set; }
}

public sealed class WebAnalyticsCategoryRow
{
    public string Date { get; set; } = "";
    public string Key { get; set; } = "";
    public long Visits { get; set; }
}

public sealed class WebAnalyticsPathRow
{
    public string Date { get; set; } = "";
    public string Path { get; set; } = "";
    public long PageViews { get; set; }
}
