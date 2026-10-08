using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Sync;
using Numeris.ViewModels.Sources;

internal static partial class SearchConsoleReviewRegressionTests
{
    private const string Domain = "synthetic.example";
    private const string OldSync = "2000-01-01T00:00:00";

    public static void PreservesSitesOnSameClientMetadataSave()
    {
        using var database = (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
            .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var vault = CredentialVaultRegressionTests.CreateVault((_, _) => "synthetic-password");
        var repository = new ConnectionsRepository(database, vault);
        repository.UpsertSearchConsoleAsync(new SearchConsoleConnectionConfig
        {
            ClientId = "original-client",
            Sites = new() { "sc-domain:preserved.example" },
        }, "configured").GetAwaiter().GetResult();
        var source = new SearchConsoleSourceViewModel(repository, vault, new GoogleOAuthFlow(new GoogleOAuthClient()),
            new SearchConsoleSyncService(new SearchConsoleClient(), new GoogleOAuthClient(), new SearchConsoleRepository(database),
                new SitemapRepository(database), vault, repository));
        source.LoadAsync().GetAwaiter().GetResult();
        source.SaveAsync().GetAwaiter().GetResult();
        Require(source.StatusMessage.StartsWith("Saved.", StringComparison.Ordinal)
            && repository.ListConfiguredDomainsAsync().GetAwaiter().GetResult().Contains("preserved.example"),
            "Saving the same client metadata must retain its imported sync domain");

        repository.UpsertSearchConsoleAsync(new SearchConsoleConnectionConfig
        {
            ClientId = "original-client",
            Sites = new() { "https://explicit.example/" },
        }, "configured").GetAwaiter().GetResult();
        var explicitDomains = repository.ListConfiguredDomainsAsync().GetAwaiter().GetResult();
        Require(explicitDomains.Contains("explicit.example") && !explicitDomains.Contains("preserved.example"),
            "An explicit Sites list must replace metadata for the current client");
        Require(database.ReadAsync(c => c.ExecuteScalar<string>("SELECT config FROM connections WHERE id='sc'"))
            .GetAwaiter().GetResult()!.Contains("explicit.example", StringComparison.Ordinal),
            "The replacement property metadata must persist in the connection config");

        source.NewClientId = "replacement-client";
        source.SaveAsync().GetAwaiter().GetResult();
        Require(source.StatusMessage.StartsWith("Saved.", StringComparison.Ordinal)
            && !repository.ListConfiguredDomainsAsync().GetAwaiter().GetResult().Contains("explicit.example"),
            "Changing OAuth clients must not retain the previous client's property targets");
    }

    public static void RejectsInspectionUrlsOutsideThePropertyBeforeHttp()
    {
        var client = new SearchConsoleClient();
        foreach (var (property, url) in new[]
        {
            ("https://www.example.com/", "https://example.com/page/"),
            ("http://example.com/", "https://example.com/page/"),
            ("https://example.com:8443/", "https://example.com/page/"),
            ("https://example.com/", "https://example.com:8443/page/"),
            ("https://example.com/blog/", "https://example.com/blog-other/page/"),
            ("https://example.com/blog/", "https://example.com/blog%2Fpage/"),
            ("https://example.com/blog/", "https://example.com/Blog/page/"),
            ("https://example.com/", "https://otherexample.com/page/"),
            ("sc-domain:example.com", "https://otherexample.com/page/"),
            ("sc-domain:example.com", "https://example.com.other.org/page/"),
            ("sc-domain:example.com", "ftp://example.com/page/"),
            ("sc-domain:example.com", "not-a-url"),
        })
        {
            // The header is deliberately invalid so a missing membership guard cannot send a provider request.
            try { client.InspectUrlAsync("synthetic\r\naccess-token", property, url).GetAwaiter().GetResult(); }
            catch (InvalidOperationException ex) when (ex.Message == "The sitemap URL does not belong to the authorized Search Console property")
            {
                Require(!ex.Message.Contains(url, StringComparison.Ordinal), "The validation error must not disclose target URLs");
                continue;
            }
            throw new InvalidOperationException("An invalid inspection pair reached the HTTP path");
        }
    }

    public static void AcceptsInspectionPropertyMembershipBoundaries()
    {
        var validate = typeof(SearchConsoleClient).GetMethod("InspectionUrlBelongsToProperty", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (property, url) in new[]
        {
            ("sc-domain:example.com", "https://example.com/page/"),
            ("sc-domain:example.com", "http://www.example.com/page/"),
            ("sc-domain:example.com", "https://deep.sub.example.com/page/"),
            ("https://EXAMPLE.com/", "https://example.com/page/?a=one%2Ftwo&b=2"),
            ("https://example.com:443/blog/", "https://example.com/blog/page/?a=1"),
            ("http://example.com:8080/blog/", "http://example.com:8080/blog/page/"),
            ("https://example.com/blog%2Ftopic/", "https://example.com/blog%2Ftopic/page/"),
        })
        {
            Require((bool)validate.Invoke(null, new object[] { property, url })!,
                "A valid URL must remain within its domain or exact URL-prefix property");
        }
    }

    public static void SyncPreservesStateWithoutAuthorizedMatches()
    {
        using var database = CreateDatabase();
        var requests = new List<string>();
        using var http = new HttpClient(new SyntheticHandler((request, body) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsoluteUri == GoogleOAuthClient.TokenEndpoint)
                return AuthorizationResponse(request, body);
            Require(request.RequestUri.AbsoluteUri == SearchConsoleClient.SitesEndpoint,
                "No matched property may reach an analytics endpoint");
            Require(request.Method == HttpMethod.Get && request.Headers.Authorization?.Parameter == "synthetic-access",
                "Property discovery must use the refreshed bearer token");
            return JsonResponse("""{"siteEntry":[{"siteUrl":"sc-domain:unrelated.example"}]}""");
        }));
        var sync = CreateConfiguredSync(database, http);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO search_console (site_url,date,kind,query,clicks,fetched_at)
            VALUES (@Domain,'2026-01-01','daily','',9,'old');
            """, new { Domain })).GetAwaiter().GetResult();
        var before = ReadConnectionState(database);
        try { sync.SyncConfiguredAsync(7).GetAwaiter().GetResult(); }
        catch (InvalidOperationException ex) when (ex.Message == "No authorized Search Console properties match the configured domains")
        {
            Require(requests.Count == 2 && ReadConnectionState(database) == before,
                "A zero-match sync must preserve connection status, config and last_sync");
            Require(database.ReadAsync(c => c.ExecuteScalar<long>("SELECT SUM(clicks) FROM search_console")).Result == 9,
                "A zero-match sync must preserve stored analytics");
            return;
        }
        throw new InvalidOperationException("A zero-match sync must fail rather than record an empty successful refresh");
    }

    public static void SyncMapsDimensionsAndReplacesSuccessfulEmptyWindows()
    {
        using var database = CreateDatabase();
        var end = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var start = DateOnly.ParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pqStart = DateOnly.ParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-27).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        const string query = "quoted \"query\" & β";
        const string page = "https://synthetic.example/page/?a=one%2Ftwo&b=2";
        var dimensionsSeen = new List<string>();
        var empty = false;
        using var http = new HttpClient(new SyntheticHandler((request, body) =>
        {
            var authorization = AuthorizationOrSitesResponse(request, body);
            if (authorization is not null) return authorization;
            Require(request.Method == HttpMethod.Post
                && Uri.UnescapeDataString(request.RequestUri!.AbsolutePath) == "/webmasters/v3/sites/sc-domain:synthetic.example/searchAnalytics/query"
                && request.Headers.Authorization?.Parameter == "synthetic-access",
                "Analytics must use the exact authorized property and refreshed token");
            using var payload = JsonDocument.Parse(body);
            var dimensions = string.Join(",", payload.RootElement.GetProperty("dimensions").EnumerateArray().Select(d => d.GetString()));
            dimensionsSeen.Add(dimensions);
            Require(payload.RootElement.GetProperty("startDate").GetString() == (dimensions == "page,query" ? pqStart : start)
                && payload.RootElement.GetProperty("endDate").GetString() == end
                && payload.RootElement.GetProperty("rowLimit").GetInt32() == 5000,
                "Daily breakdowns use seven inclusive days and page-query uses 28 inclusive days");
            var keys = dimensions switch
            {
                "date" => new[] { end },
                "date,query" => new[] { end, query },
                "date,page" => new[] { end, page },
                "date,country" => new[] { end, "FIN" },
                "date,device" => new[] { end, "MOBILE" },
                "page,query" => new[] { page, query },
                _ => throw new InvalidOperationException("Unexpected dimensions"),
            };
            return JsonResponse(empty ? "{\"rows\":[]}" : JsonSerializer.Serialize(new
            {
                rows = new[] { new { keys, clicks = 2.4, impressions = 10.6, ctr = 0.25, position = 2.75 } },
            }));
        }));
        var sync = CreateConfiguredSync(database, http);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO search_console (site_url,date,kind,query,clicks,fetched_at)
            VALUES (@Domain,'2000-01-01','daily','old',9,'old'),('neighbor.example',@end,'daily','neighbor',8,'old');
            INSERT INTO search_devices (site_url,date,device,clicks) VALUES (@Domain,'2000-01-01','MOBILE',9);
            INSERT INTO search_page_queries (site_url,period_start,period_end,page,query,clicks)
            VALUES (@Domain,'2000-01-01','2000-01-28','old','old',9);
            """, new { Domain, end })).GetAwaiter().GetResult();
        var populated = sync.SyncConfiguredAsync(7).GetAwaiter().GetResult()!;
        Require(populated.DaysSynced == 7 && populated.RecordsUpserted == 6,
            "Each provider dimension must be stored in its intended dataset");
        Require(database.ReadAsync(c => c.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM search_console WHERE site_url=@Domain AND date=@end
            AND clicks=2 AND impressions=11 AND ctr=0.25 AND position=2.75
            """, new { Domain, end })).Result == 5, "Clicks and impressions are rounded while CTR and position remain fractional");
        var repository = new SearchConsoleRepository(database);
        Require(repository.GetQueriesAsync(Domain, start, end, "clicks", 10).Result.Single().Query == query
            && repository.GetPagesAsync(Domain, start, end, 10).Result.Single().Page == page
            && repository.GetCountriesAsync(Domain, start, end).Result.Single().Country == "FIN"
            && repository.GetDevicesAsync(Domain, start, end).Result.Single().Device == "MOBILE",
            "Query, page, country and device keys must round-trip without changing dimension order");
        Require(database.ReadAsync(c => c.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM search_page_queries WHERE site_url=@Domain AND period_start=@pqStart AND period_end=@end AND page=@page AND query=@query
            """, new { Domain, pqStart, end, page, query })).Result == 1,
            "Page-query pairs must use their exact 28-day snapshot key");

        empty = true;
        database.WriteAsync(c => c.Execute("UPDATE connections SET status='configured', last_sync=@OldSync WHERE id='sc'", new { OldSync })).GetAwaiter().GetResult();
        var cleared = sync.SyncConfiguredAsync(7).GetAwaiter().GetResult()!;
        Require(cleared.RecordsUpserted == 0 && dimensionsSeen.SequenceEqual(SearchConsoleReviewRegressionTestsInputs.Vector1), "An authorized empty response must complete all six requests successfully");
        Require(database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM search_console WHERE site_url=@Domain AND date BETWEEN @start AND @end", new { Domain, start, end })).Result == 0
            && repository.GetDevicesAsync(Domain, start, end).Result.Count == 0
            && database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM search_page_queries WHERE site_url=@Domain AND period_start=@pqStart AND period_end=@end", new { Domain, pqStart, end })).Result == 0,
            "Successful empty responses must replace the requested ordinary, device and page-query windows");
        Require(database.ReadAsync(c => c.ExecuteScalar<long>("SELECT SUM(clicks) FROM search_console")).Result == 17
            && database.ReadAsync(c => c.ExecuteScalar<long>("SELECT SUM(clicks) FROM search_devices")).Result == 9
            && database.ReadAsync(c => c.ExecuteScalar<long>("SELECT SUM(clicks) FROM search_page_queries")).Result == 9,
            "A successful empty replacement must retain adjacent history and other domains");
        Require(database.ReadAsync(c => c.ExecuteScalar<string>("SELECT status FROM connections WHERE id='sc'")).Result == "connected"
            && database.ReadAsync(c => c.ExecuteScalar<string>("SELECT last_sync FROM connections WHERE id='sc'")).Result != OldSync,
            "An authorized empty sync must advance its successful connection status and timestamp");
    }

    public static void InspectionPreservesPartialResultsAndReportsProgress()
    {
        using var database = CreateDatabase();
        const string firstUrl = "https://synthetic.example/a/?x=one%2Ftwo&y=β";
        const string failedUrl = "https://synthetic.example/b/";
        const string missingIndexUrl = "https://synthetic.example/c/";
        const string wrongPropertyUrl = "https://www.other.example/d/";
        var requested = new List<string>();
        using var http = new HttpClient(new SyntheticHandler((request, body) =>
        {
            var authorization = AuthorizationOrSitesResponse(request, body);
            if (authorization is not null) return authorization;
            Require(request.RequestUri!.AbsoluteUri == SearchConsoleClient.UrlInspectionEndpoint && request.Method == HttpMethod.Post
                && request.Headers.Authorization?.Parameter == "synthetic-access", "Inspection must use the refreshed bearer token");
            using var payload = JsonDocument.Parse(body);
            var url = payload.RootElement.GetProperty("inspectionUrl").GetString()!;
            Require(payload.RootElement.GetProperty("siteUrl").GetString() == "sc-domain:synthetic.example",
                "Inspection must use the authorized domain property");
            requested.Add(url);
            return url switch
            {
                firstUrl => JsonResponse("""{"inspectionResult":{"indexStatusResult":{"verdict":"PASS","coverageState":"Indexed","indexingState":"INDEXING_ALLOWED","robotsTxtState":"ALLOWED","pageFetchState":"SUCCESSFUL","crawledAs":"MOBILE","lastCrawlTime":"2020-01-01T00:00:00Z"}}}"""),
                failedUrl => JsonResponse("""{"error":{"message":"quota token=synthetic-leak Bearer synthetic-bearer"}}""", HttpStatusCode.TooManyRequests),
                missingIndexUrl => JsonResponse("{\"inspectionResult\":{}}"),
                _ => throw new InvalidOperationException("Removed or unrelated URLs must not reach HTTP"),
            };
        }));
        var sync = CreateConfiguredSync(database, http);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO sitemap_urls (domain,url,discovered_at,last_seen_at,last_inspected_at,verdict,coverage_state)
            VALUES (@Domain,@firstUrl,'old','old','old','FAIL','old'),(@Domain,@failedUrl,'old','old','old','PASS','Indexed old'),
                   (@Domain,@missingIndexUrl,'old','old','old','PASS','Indexed old'),(@Domain,@wrongPropertyUrl,'old','old','old','PASS','Indexed old');
            INSERT INTO sitemap_urls (domain,url,discovered_at,last_seen_at,removed_at,verdict)
            VALUES (@Domain,'https://synthetic.example/removed/','old','old','removed','REMOVED');
            INSERT INTO sitemap_urls (domain,url,discovered_at,last_seen_at,verdict)
            VALUES ('neighbor.example',@firstUrl,'old','old','NEIGHBOR');
            """, new { Domain, firstUrl, failedUrl, missingIndexUrl, wrongPropertyUrl })).GetAwaiter().GetResult();
        var progress = new InspectionProgress();
        var result = sync.InspectSitemapUrlsAsync(Domain, progress).GetAwaiter().GetResult();
        Require(requested.SequenceEqual(new[] { firstUrl, failedUrl, missingIndexUrl })
            && result.TotalUrls == 4 && result.UrlsChecked == 2 && result.Indexed == 1 && result.NotIndexed == 1 && result.Errors == 2,
            "Mixed outcomes must continue, count all active targets and exclude removed URLs");
        Require(progress.Counts.SequenceEqual(new[] { (1L, 0L), (1L, 1L), (2L, 1L), (2L, 2L) }),
            "Each success or failure must report the actual processed counts");
        var firstError = result.FirstError ?? "";
        Require(firstError.Contains("HTTP 429", StringComparison.Ordinal)
            && !firstError.Contains("synthetic-leak", StringComparison.Ordinal)
            && !firstError.Contains("synthetic-bearer", StringComparison.Ordinal),
            "Partial errors must retain safe provider context without secrets");
        var urls = new SitemapRepository(database).ListUrlsAsync(Domain).Result;
        var first = urls.Single(u => u.Url == firstUrl);
        Require(first.IsIndexed && first.IndexingState == "INDEXING_ALLOWED" && first.PageFetchState == "SUCCESSFUL"
            && first.CrawledAs == "MOBILE" && first.LastCrawlTime == "2020-01-01T00:00:00Z" && first.LastInspectedAt != "old",
            "A successful inspection must persist its fields and local timestamp");
        Require(database.ReadAsync(c => c.ExecuteScalar<string>("SELECT robots_txt_state FROM sitemap_urls WHERE domain=@Domain AND url=@firstUrl", new { Domain, firstUrl })).Result == "ALLOWED",
            "The robots field must persist alongside the public sitemap fields");
        Require(urls.Single(u => u.Url == failedUrl).LastInspectedAt == "old"
            && urls.Single(u => u.Url == wrongPropertyUrl).LastInspectedAt == "old"
            && !urls.Single(u => u.Url == missingIndexUrl).HasInspectionData
            && database.ReadAsync(c => c.ExecuteScalar<string>("SELECT verdict FROM sitemap_urls WHERE domain='neighbor.example'")).Result == "NEIGHBOR",
            "Failed targets preserve prior results; missing index fields cannot preserve a misleading indexed result or alter neighboring domains");
    }

    public static void InspectionStopsAfterThreeInitialFailures()
    {
        using var database = CreateDatabase();
        var inspectionRequests = 0;
        using var http = new HttpClient(new SyntheticHandler((request, body) =>
        {
            var authorization = AuthorizationOrSitesResponse(request, body);
            if (authorization is not null) return authorization;
            Require(request.RequestUri!.AbsoluteUri == SearchConsoleClient.UrlInspectionEndpoint, "Only inspection requests are expected");
            inspectionRequests++;
            return JsonResponse("{\"error\":{\"message\":\"synthetic provider failure\"}}", HttpStatusCode.ServiceUnavailable);
        }));
        var sync = CreateConfiguredSync(database, http);
        database.WriteAsync(c =>
        {
            foreach (var number in Enumerable.Range(1, 4))
                c.Execute("INSERT INTO sitemap_urls (domain,url,discovered_at,last_seen_at,verdict) VALUES (@Domain,@url,'old','old','OLD')",
                    new { Domain, url = $"https://{Domain}/{number}/" });
        }).GetAwaiter().GetResult();
        var progress = new InspectionProgress();
        var result = sync.InspectSitemapUrlsAsync(Domain, progress).GetAwaiter().GetResult();
        Require(inspectionRequests == 3 && result.TotalUrls == 4 && result.UrlsChecked == 0 && result.Errors == 3
            && progress.Counts.SequenceEqual(new[] { (0L, 1L), (0L, 2L), (0L, 3L) }),
            "Three initial failures must stop without claiming the unattempted fourth URL");
        Require(database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM sitemap_urls WHERE verdict='OLD' AND last_inspected_at IS NULL")).Result == 4,
            "An entirely failed batch must preserve all prior inspection data");
    }

    public static void OAuthFailuresPreserveSyncStateAndSanitizeErrors()
    {
        using var database = CreateDatabase();
        var grants = new List<string>();
        using var http = new HttpClient(new SyntheticHandler((request, body) =>
        {
            Require(request.RequestUri!.AbsoluteUri == GoogleOAuthClient.TokenEndpoint && request.Method == HttpMethod.Post,
                "Failed refresh must stop before Search Console requests");
            var form = HttpUtility.ParseQueryString(body);
            Require(form["client_id"] == "synthetic-client" && form["client_secret"] == "synthetic-password",
                "Token requests must carry the supplied client credentials in their form");
            var grant = form["grant_type"]!;
            grants.Add(grant);
            if (grant == "authorization_code")
                Require(form["code"] == "synthetic code & β" && form["redirect_uri"] == "http://127.0.0.1:12345/oauth2callback",
                    "Code exchange must preserve encoded code and redirect URI");
            else
                Require(grant == "refresh_token" && form["refresh_token"] == "synthetic-password",
                    "Refresh must carry the vault token in its form");
            return JsonResponse("""{"error":"invalid_grant","error_description":"expired refresh_token=synthetic-leak Bearer synthetic-bearer"}""", HttpStatusCode.BadRequest);
        }));
        var oauth = new GoogleOAuthClient(http);
        AssertOAuthFailure(() => oauth.ExchangeCodeAsync("synthetic-client", "synthetic-password", "synthetic code & β", "http://127.0.0.1:12345/oauth2callback").GetAwaiter().GetResult());
        AssertOAuthFailure(() => oauth.RefreshAccessTokenAsync("synthetic-client", "synthetic-password", "synthetic-password").GetAwaiter().GetResult());
        var sync = CreateConfiguredSync(database, http);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO search_console (site_url,date,kind,query,clicks,fetched_at) VALUES (@Domain,'2026-01-01','daily','',9,'old');
            """, new { Domain })).GetAwaiter().GetResult();
        var before = ReadConnectionState(database);
        AssertOAuthFailure(() => sync.SyncConfiguredAsync(7).GetAwaiter().GetResult());
        Require(grants.SequenceEqual(SearchConsoleReviewRegressionTestsInputs.Vector2)
            && ReadConnectionState(database) == before
            && database.ReadAsync(c => c.ExecuteScalar<long>("SELECT SUM(clicks) FROM search_console")).Result == 9,
            "A failed refresh must preserve connection metadata, last_sync and stored analytics");
    }

    private static SqliteDatabase CreateDatabase() => (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
        .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;

    private static SearchConsoleSyncService CreateConfiguredSync(SqliteDatabase database, HttpClient http)
    {
        var vault = CredentialVaultRegressionTests.CreateVault((_, _) => "synthetic-password");
        var connections = new ConnectionsRepository(database, vault);
        connections.UpsertSearchConsoleAsync(new SearchConsoleConnectionConfig
        {
            ClientId = "synthetic-client",
            Sites = new() { "sc-domain:synthetic.example" },
        }, "configured").GetAwaiter().GetResult();
        database.WriteAsync(c => c.Execute("UPDATE connections SET last_sync=@OldSync WHERE id='sc'", new { OldSync })).GetAwaiter().GetResult();
        return new SearchConsoleSyncService(new SearchConsoleClient(http), new GoogleOAuthClient(http),
            new SearchConsoleRepository(database), new SitemapRepository(database), vault, connections);
    }

    private static string ReadConnectionState(SqliteDatabase database) => database.ReadAsync(c => c.ExecuteScalar<string>(
        "SELECT status || '|' || COALESCE(last_sync,'') || '|' || COALESCE(config,'') FROM connections WHERE id='sc'")).Result!;

    private static HttpResponseMessage AuthorizationResponse(HttpRequestMessage request, string body)
    {
        var form = HttpUtility.ParseQueryString(body);
        Require(request.Method == HttpMethod.Post && form["grant_type"] == "refresh_token"
            && form["client_id"] == "synthetic-client" && form["client_secret"] == "synthetic-password"
            && form["refresh_token"] == "synthetic-password", "Refresh must use the configured client and vault credentials");
        return JsonResponse("{\"access_token\":\"synthetic-access\"}");
    }

    private static HttpResponseMessage? AuthorizationOrSitesResponse(HttpRequestMessage request, string body)
    {
        if (request.RequestUri!.AbsoluteUri == GoogleOAuthClient.TokenEndpoint) return AuthorizationResponse(request, body);
        if (request.RequestUri.AbsoluteUri != SearchConsoleClient.SitesEndpoint) return null;
        Require(request.Method == HttpMethod.Get && request.Headers.Authorization?.Parameter == "synthetic-access",
            "Property discovery must use the refreshed bearer token");
        return JsonResponse("""{"siteEntry":[{"siteUrl":"sc-domain:synthetic.example"}]}""");
    }

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static void AssertOAuthFailure(Action action)
    {
        try { action(); }
        catch (ApiRequestException ex)
        {
            Require(ex.Provider == "Google OAuth" && ex.Operation == "token" && ex.StatusCode == HttpStatusCode.BadRequest
                && ex.Message.Contains("Connect Google account again", StringComparison.Ordinal)
                && !ex.Message.Contains("synthetic-leak", StringComparison.Ordinal)
                && !ex.Message.Contains("synthetic-bearer", StringComparison.Ordinal),
                "OAuth failures must report reauthorization guidance without response secrets");
            return;
        }
        throw new InvalidOperationException("An unsuccessful token exchange or refresh must fail");
    }

    private sealed partial class SyntheticHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private sealed partial class InspectionProgress : IProgress<IndexingInspectionResult>
    {
        public List<(long Checked, long Errors)> Counts { get; } = new();
        public void Report(IndexingInspectionResult value) => Counts.Add((value.UrlsChecked, value.Errors));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class SearchConsoleReviewRegressionTestsInputs
{
    internal static readonly string[] Vector1 = new[]
        {
            "date", "date,query", "date,page", "date,country", "date,device", "page,query",
            "date", "date,query", "date,page", "date,country", "date,device", "page,query",
        };
    internal static readonly string[] Vector2 = new[] { "authorization_code", "refresh_token", "refresh_token" };
}
