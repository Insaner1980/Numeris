using System;
using System.Collections.Generic;
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
        query PulseWebAnalytics(
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
                limit: 100
                filter: { siteTag: $siteTag, datetime_geq: $since, datetime_leq: $until, bot: 0 }
                orderBy: [sum_visits_DESC]
              ) {
                dimensions { refererHost }
                sum { visits }
              }
              pages: rumPageloadEventsAdaptiveGroups(
                limit: 100
                filter: { siteTag: $siteTag, datetime_geq: $since, datetime_leq: $until, bot: 0 }
                orderBy: [count_DESC]
              ) {
                dimensions { requestPath }
                count
              }
              countries: rumPageloadEventsAdaptiveGroups(
                limit: 100
                filter: { siteTag: $siteTag, datetime_geq: $since, datetime_leq: $until, bot: 0 }
                orderBy: [sum_visits_DESC]
              ) {
                dimensions { countryName }
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

    public async Task<List<WebAnalyticsSite>> ListSitesAsync(string apiToken, string accountId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/accounts/{accountId.Trim()}/rum/site_info/list");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());

        using var response = await Http.SendAsync(req).ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<SiteInfoListResponse>(JsonOptions).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not parse Cloudflare RUM site response");

        if (!response.IsSuccessStatusCode || !body.Success)
        {
            var msg = body.Errors?.Count > 0 ? body.Errors[0].Message : $"Cloudflare returned HTTP {(int)response.StatusCode}";
            throw new InvalidOperationException(msg);
        }

        var result = new List<WebAnalyticsSite>();
        foreach (var site in body.Result ?? new())
        {
            if (string.IsNullOrWhiteSpace(site.SiteTag)) continue;
            var host = site.Rules?
                .Select(r => r.Host)
                .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h))
                ?? site.Ruleset?.ZoneName;
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
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());

        using var response = await Http.SendAsync(req).ConfigureAwait(false);
        var graphql = await response.Content.ReadFromJsonAsync<GraphqlResponse>(JsonOptions).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not parse Cloudflare Web Analytics response");

        if (graphql.Errors is { Count: > 0 } errs)
        {
            throw new InvalidOperationException(errs[0].Message);
        }
        var account = graphql.Data?.Viewer?.Accounts?.FirstOrDefault();
        if (account is null)
        {
            throw new InvalidOperationException("Cloudflare did not return data for this account");
        }

        var rollupDate = untilDate;

        var result = new WebAnalyticsRollup();
        foreach (var d in account.Daily ?? new())
        {
            result.Daily.Add(new WebAnalyticsDailyRow
            {
                Date = d.Dimensions?.Date ?? "",
                Visits = d.Sum?.Visits ?? 0,
                PageViews = d.Count,
            });
        }
        foreach (var r in account.Referrers ?? new())
        {
            result.Referrers.Add(new WebAnalyticsCategoryRow
            {
                Date = rollupDate,
                Key = r.Dimensions?.RefererHost ?? "",
                Visits = r.Sum?.Visits ?? 0,
            });
        }
        foreach (var p in account.Pages ?? new())
        {
            result.Pages.Add(new WebAnalyticsPathRow
            {
                Date = rollupDate,
                Path = p.Dimensions?.RequestPath ?? "",
                PageViews = p.Count,
            });
        }
        foreach (var c in account.Countries ?? new())
        {
            result.Countries.Add(new WebAnalyticsCategoryRow
            {
                Date = rollupDate,
                Key = c.Dimensions?.CountryName ?? "",
                Visits = c.Sum?.Visits ?? 0,
            });
        }
        return result;
    }

    private sealed class SiteInfoListResponse
    {
        public bool Success { get; set; }
        public List<SiteInfo>? Result { get; set; }
        public List<ApiError>? Errors { get; set; }
    }
    private sealed class ApiError { public string Message { get; set; } = ""; }
    private sealed class SiteInfo
    {
        [JsonPropertyName("site_tag")] public string? SiteTag { get; set; }
        public List<RumRule>? Rules { get; set; }
        public RumRuleset? Ruleset { get; set; }
    }
    private sealed class RumRule { public string? Host { get; set; } }
    private sealed class RumRuleset { [JsonPropertyName("zone_name")] public string? ZoneName { get; set; } }

    private sealed class GraphqlResponse
    {
        public GraphqlData? Data { get; set; }
        public List<GraphqlError>? Errors { get; set; }
    }
    private sealed class GraphqlError { public string Message { get; set; } = ""; }
    private sealed class GraphqlData { public GraphqlViewer? Viewer { get; set; } }
    private sealed class GraphqlViewer { public List<GraphqlAccount>? Accounts { get; set; } }
    private sealed class GraphqlAccount
    {
        public List<DailyGroup>? Daily { get; set; }
        public List<ReferrerGroup>? Referrers { get; set; }
        public List<PageGroup>? Pages { get; set; }
        public List<CountryGroup>? Countries { get; set; }
    }
    private sealed class DailyGroup
    {
        public DailyDimensions? Dimensions { get; set; }
        public VisitsSum? Sum { get; set; }
        public long Count { get; set; }
    }
    private sealed class DailyDimensions { public string Date { get; set; } = ""; }
    private sealed class VisitsSum { public long Visits { get; set; } }
    private sealed class ReferrerGroup
    {
        public ReferrerDimensions? Dimensions { get; set; }
        public VisitsSum? Sum { get; set; }
    }
    private sealed class ReferrerDimensions { public string RefererHost { get; set; } = ""; }
    private sealed class PageGroup
    {
        public PageDimensions? Dimensions { get; set; }
        public long Count { get; set; }
    }
    private sealed class PageDimensions { public string RequestPath { get; set; } = ""; }
    private sealed class CountryGroup
    {
        public CountryDimensions? Dimensions { get; set; }
        public VisitsSum? Sum { get; set; }
    }
    private sealed class CountryDimensions { public string CountryName { get; set; } = ""; }
}

public sealed class WebAnalyticsSite
{
    public string Host { get; set; } = "";
    public string SiteTag { get; set; } = "";
}

public sealed class WebAnalyticsRollup
{
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
