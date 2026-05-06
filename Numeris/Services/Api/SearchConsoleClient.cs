using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;

namespace Numeris.Services.Api;

public sealed class SearchConsoleClient
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string SitesEndpoint = "https://www.googleapis.com/webmasters/v3/sites";
    public const string Scope = "https://www.googleapis.com/auth/webmasters.readonly";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly HttpClient Http = new();

    public static string BuildAuthUrl(string clientId, string redirectUri, string state)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("https://accounts.google.com/o/oauth2/v2/auth");
        sb.Append("?client_id=").Append(HttpUtility.UrlEncode(clientId));
        sb.Append("&redirect_uri=").Append(HttpUtility.UrlEncode(redirectUri));
        sb.Append("&response_type=code");
        sb.Append("&scope=").Append(HttpUtility.UrlEncode(Scope));
        sb.Append("&access_type=offline&prompt=consent");
        sb.Append("&state=").Append(HttpUtility.UrlEncode(state));
        return sb.ToString();
    }

    public async Task<OAuthTokens> ExchangeCodeAsync(string clientId, string clientSecret, string code, string redirectUri)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        });
        using var response = await Http.PostAsync(TokenEndpoint, form).ConfigureAwait(false);
        return await ParseTokenResponseAsync(response).ConfigureAwait(false);
    }

    public async Task<string> RefreshAccessTokenAsync(string clientId, string clientSecret, string refreshToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        });
        using var response = await Http.PostAsync(TokenEndpoint, form).ConfigureAwait(false);
        var tokens = await ParseTokenResponseAsync(response).ConfigureAwait(false);
        return tokens.AccessToken;
    }

    private static async Task<OAuthTokens> ParseTokenResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("Google OAuth", "token", response.StatusCode, ApiErrorMessage.FromBody(body));
        }
        var parsed = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse Google token response");
        return new OAuthTokens
        {
            AccessToken = parsed.AccessToken,
            RefreshToken = parsed.RefreshToken,
        };
    }

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

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
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
}

public sealed class OAuthTokens
{
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
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
