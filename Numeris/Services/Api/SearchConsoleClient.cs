using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Numeris.Services.Auth;

namespace Numeris.Services.Api;

public sealed class SearchConsoleClient
{
    public const string SitesEndpoint = "https://www.googleapis.com/webmasters/v3/sites";
    public const string UrlInspectionEndpoint = "https://searchconsole.googleapis.com/v1/urlInspection/index:inspect";
    public const string Scope = "https://www.googleapis.com/auth/webmasters.readonly";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly HttpClient Http = new();
    private static readonly TimeSpan UrlInspectionTimeout = TimeSpan.FromSeconds(15);

    public async Task<List<string>> ListSitesAsync(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Google OAuth refresh did not return an access token");
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, SitesEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        using var response = await Http.SendAsync(req).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("Google Search Console", "sites.list", response.StatusCode, ApiErrorMessage.FromBody(body));
        }
        var parsed = JsonSerializer.Deserialize<SitesResponse>(body, JsonOptions);
        return parsed?.SiteEntry?.Select(e => e.SiteUrl).ToList() ?? new List<string>();
    }

    public static string? PropertyForDomain(IEnumerable<string> sites, string domain)
    {
        var d = domain.Trim().ToLowerInvariant();
        var list = sites.ToList();
        var candidates = new[]
        {
            $"sc-domain:{d}",
            $"https://{d}/",
            $"http://{d}/",
            $"https://www.{d}/",
            $"http://www.{d}/",
        };
        foreach (var candidate in candidates)
        {
            var match = list.FirstOrDefault(s => string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return null;
    }

    public async Task<List<SearchConsoleApiRow>> QueryAsync(
        string accessToken,
        string propertyUrl,
        string startDate,
        string endDate,
        SearchQueryKind kind,
        int rowLimit)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Google OAuth refresh did not return an access token");
        }

        var url = $"{SitesEndpoint}/{HttpUtility.UrlEncode(propertyUrl)}/searchAnalytics/query";
        var dimensions = kind switch
        {
            SearchQueryKind.Daily => new[] { "date" },
            SearchQueryKind.Query => new[] { "date", "query" },
            SearchQueryKind.Page => new[] { "date", "page" },
            SearchQueryKind.Country => new[] { "date", "country" },
            SearchQueryKind.Device => new[] { "date", "device" },
            SearchQueryKind.PageQuery => new[] { "page", "query" },
            _ => new[] { "date" },
        };
        var requestBody = new
        {
            startDate,
            endDate,
            dimensions,
            rowLimit,
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
            throw new ApiRequestException("Google Search Console", "searchAnalytics.query", response.StatusCode, ApiErrorMessage.FromBody(body));
        }
        var parsed = JsonSerializer.Deserialize<QueryResponse>(body, JsonOptions);
        var rows = new List<SearchConsoleApiRow>();
        foreach (var row in parsed?.Rows ?? new())
        {
            var keys = row.Keys ?? new();
            var (date, query, page, country, device) = kind switch
            {
                SearchQueryKind.Daily => (keys.ElementAtOrDefault(0) ?? "", "__daily__", (string?)null, (string?)null, (string?)null),
                SearchQueryKind.Query => (keys.ElementAtOrDefault(0) ?? "", keys.ElementAtOrDefault(1) ?? "(not set)", (string?)null, (string?)null, (string?)null),
                SearchQueryKind.Page => (keys.ElementAtOrDefault(0) ?? "", "__page__", (string?)keys.ElementAtOrDefault(1), (string?)null, (string?)null),
                SearchQueryKind.Country => (keys.ElementAtOrDefault(0) ?? "", "__country__", (string?)null, (string?)keys.ElementAtOrDefault(1), (string?)null),
                SearchQueryKind.Device => (keys.ElementAtOrDefault(0) ?? "", "__device__", (string?)null, (string?)null, (string?)keys.ElementAtOrDefault(1)),
                SearchQueryKind.PageQuery => ("", keys.ElementAtOrDefault(1) ?? "(not set)", (string?)keys.ElementAtOrDefault(0), (string?)null, (string?)null),
                _ => ("", "", (string?)null, (string?)null, (string?)null),
            };
            rows.Add(new SearchConsoleApiRow
            {
                Date = date,
                Query = query,
                Page = page,
                Country = country,
                Device = device,
                Clicks = (long)Math.Round(row.Clicks),
                Impressions = (long)Math.Round(row.Impressions),
                Ctr = row.Ctr,
                Position = row.Position,
            });
        }
        return rows;
    }

    public async Task<UrlInspectionData> InspectUrlAsync(string accessToken, string propertyUrl, string inspectionUrl)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Google OAuth refresh did not return an access token");
        }

        var requestBody = new
        {
            inspectionUrl,
            siteUrl = propertyUrl,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, UrlInspectionEndpoint)
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());

        using var timeout = new CancellationTokenSource(UrlInspectionTimeout);
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(req, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Google Search Console URL inspection timed out after {UrlInspectionTimeout.TotalSeconds:0} seconds", ex);
        }

        using (response)
        {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("Google Search Console", "urlInspection.index.inspect", response.StatusCode, ApiErrorMessage.FromBody(body));
        }

        var parsed = JsonSerializer.Deserialize<InspectionResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse URL inspection response");
        var index = parsed.InspectionResult?.IndexStatusResult;
        return new UrlInspectionData
        {
            Url = inspectionUrl,
            Verdict = index?.Verdict,
            CoverageState = index?.CoverageState,
            IndexingState = index?.IndexingState,
            RobotsTxtState = index?.RobotsTxtState,
            PageFetchState = index?.PageFetchState,
            CrawledAs = index?.CrawledAs,
            LastCrawlTime = index?.LastCrawlTime,
        };
        }
    }

    private sealed class SitesResponse { public List<SiteEntry>? SiteEntry { get; set; } }
    private sealed class SiteEntry { public string SiteUrl { get; set; } = ""; }
    private sealed class QueryResponse { public List<QueryRow>? Rows { get; set; } }
    private sealed class QueryRow
    {
        public List<string>? Keys { get; set; }
        public double Clicks { get; set; }
        public double Impressions { get; set; }
        public double Ctr { get; set; }
        public double Position { get; set; }
    }
    private sealed class InspectionResponse { public InspectionResult? InspectionResult { get; set; } }
    private sealed class InspectionResult { public IndexStatusResult? IndexStatusResult { get; set; } }
    private sealed class IndexStatusResult
    {
        public string? Verdict { get; set; }
        public string? CoverageState { get; set; }
        public string? IndexingState { get; set; }
        public string? RobotsTxtState { get; set; }
        public string? PageFetchState { get; set; }
        public string? CrawledAs { get; set; }
        public string? LastCrawlTime { get; set; }
    }
}

public enum SearchQueryKind { Daily, Query, Page, Country, Device, PageQuery }

public sealed class SearchConsoleApiRow
{
    public string Date { get; set; } = "";
    public string Query { get; set; } = "";
    public string? Page { get; set; }
    public string? Country { get; set; }
    public string? Device { get; set; }
    public long Clicks { get; set; }
    public long Impressions { get; set; }
    public double Ctr { get; set; }
    public double Position { get; set; }
}

public sealed class UrlInspectionData
{
    public string Url { get; set; } = "";
    public string? Verdict { get; set; }
    public string? CoverageState { get; set; }
    public string? IndexingState { get; set; }
    public string? RobotsTxtState { get; set; }
    public string? PageFetchState { get; set; }
    public string? CrawledAs { get; set; }
    public string? LastCrawlTime { get; set; }
}
