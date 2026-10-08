using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Sync;

internal static partial class ProviderCoverageRegressionTests
{
    public static void BingPersistsCoreReportsAndHistory()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("DELETE FROM bing_sites")).GetAwaiter().GetResult();
        var methods = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var method = request.RequestUri!.AbsolutePath.Split('/')[^1];
            methods.Add(method);
            var json = method switch
            {
                "GetUserSites" => """{"d":[{"Url":"https://bing-test.example/"}]}""",
                "GetRankAndTrafficStats" => """{"d":[{"Date":"2026-10-01","Clicks":20,"Impressions":200},{"Date":"2026-10-02","clicks":5,"impressions":50}]}""",
                "GetQueryStats" => """{"d":[{"Query":"first","Date":"2026-10-01","Clicks":12,"Impressions":120,"AvgClickPosition":2,"AvgImpressionPosition":3},{"keyword":"first","date":"2026-10-02","clicks":4,"impressions":40,"AverageClickPosition":4,"AverageImpressionPosition":5},{}]}""",
                "GetPageStats" => """{"d":[{"Url":"https://bing-test.example/","Date":"2026-10-01","Clicks":12,"Impressions":120},{"page":"https://bing-test.example/","date":"2026-10-02","clicks":4,"impressions":40},{}]}""",
                "GetCrawlStats" => """{"d":{"Date":"2026-10-02","CrawledPages":25}}""",
                "GetCrawlIssues" => """{"d":[{"Name":"not-found","Url":"https://bing-test.example/missing"},7]}""",
                _ => throw new InvalidOperationException("Unexpected Bing operation: " + method),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }));
        var vault = CredentialVaultRegressionTests.CreateVault((_, _) => "synthetic-bing-key");
        var connections = new ConnectionsRepository(database, vault);
        connections.UpsertBingAsync(new BingConnectionConfig(), "configured").GetAwaiter().GetResult();
        var repository = new BingRepository(database);
        var sync = new BingWebmasterSyncService(new BingWebmasterClient(http), repository, connections, vault);
        sync.AddSiteAsync("https://bing-test.example/").GetAwaiter().GetResult();
        Require(sync.TestAsync().GetAwaiter().GetResult().Ok, "Bing credential test must parse the site response");
        var result = sync.SyncConfiguredAsync().GetAwaiter().GetResult()!;
        Require(result.SitesSynced == 1 && result.RankRows == 2 && result.QueryRows == 2 && result.PageRows == 2,
            "Bing sync must persist every dated row and ignore rows without an identity");
        Require(methods.Distinct().Count() == 6, "Automatic Bing sync must use exactly the six core methods");
        var traffic = repository.GetTrafficDailyAsync("bing-test.example", "2026-10-01", "2026-10-02").Result;
        Require(traffic.Count == 2 && traffic.Sum(row => row.Clicks) == 25, "Bing daily history must survive sync");
        Require(repository.GetQueriesAsync("all", "2026-10-01", "2026-10-02", "position", 10).Result.Single().Clicks == 16,
            "Query reports must aggregate dated rows");
        Require(repository.GetPagesAsync("bing-test.example", "2026-10-01", "2026-10-02", 10).Result.Single().Clicks == 16,
            "Page reports must aggregate dated rows");
        Require(repository.GetRawMethodSummaryAsync("all").Result.Count == 6, "Raw data must preserve all six methods");
        sync.DeleteSiteAsync("https://bing-test.example/").GetAwaiter().GetResult();
        Require(sync.ListSitesAsync().Result.Count == 0, "Deleting a configured site must persist");
    }

    public static void BingErrorsPreserveStateAndDiagnostics()
    {
        foreach (var malformed in new[] { false, true })
        {
            using var database = CreateDatabase();
            using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(malformed ? HttpStatusCode.OK : HttpStatusCode.Forbidden)
            { Content = new StringContent(malformed ? "{" : """{"error":{"message":"Denied"}}""") }));
            var vault = CredentialVaultRegressionTests.CreateVault((_, _) => "synthetic-bing-key");
            var connections = new ConnectionsRepository(database, vault);
            var sync = new BingWebmasterSyncService(new BingWebmasterClient(http), new BingRepository(database), connections, vault);
            Require(!sync.TestAsync().Result.Ok, "Malformed and rejected Bing responses must fail the test");
            try { sync.SyncAsync().GetAwaiter().GetResult(); throw new InvalidOperationException("Expected Bing sync to fail"); }
            catch (ApiRequestException) when (!malformed) { /* Expected rejected response. */ }
            catch (System.Text.Json.JsonException) when (malformed) { /* Expected malformed response. */ }
            var saved = database.ReadAsync(connection => connection.QuerySingle<(string Key, string Json)>(
                "SELECT item_key AS Key, raw_json AS Json FROM bing_raw_items")).Result;
            Require(saved.Key == "error" && !saved.Json.Contains("synthetic-bing-key", StringComparison.Ordinal),
                "Failed Bing calls must retain sanitized diagnostics");
        }
    }

    public static void SitemapDiscoveryAndUptimeMapResponses()
    {
        using var http = new HttpClient(new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("sitemap-index.xml", StringComparison.Ordinal)
                ? "<sitemapindex><sitemap><loc>https://offline.example/one.xml</loc></sitemap><sitemap><loc>https://offline.example/two.xml</loc></sitemap></sitemapindex>"
                : "<urlset><url><loc>https://offline.example/page/</loc></url><url><loc> </loc></url></urlset>"),
        }));
        var urls = new SitemapClient(http).DiscoverUrlsAsync("https://offline.example/").Result;
        Require(urls.Count == 1 && urls[0] == "https://offline.example/page/", "Sitemap discovery must deduplicate child maps and ignore blank locations");
        foreach (var status in ProbeStatuses)
        {
            using var probeHttp = new HttpClient(new Handler(_ => new HttpResponseMessage(status)));
            var result = new UptimeClient(probeHttp).ProbeAsync("HTTP://OFFLINE.EXAMPLE/").Result;
            Require(result.Domain == "offline.example" && result.StatusCode == (int)status
                && result.Status == ((int)status < 400 ? "up" : "down"), "Uptime must classify each HTTP response correctly");
        }
        using var failingHttp = new HttpClient(new Handler(_ => throw new HttpRequestException("Synthetic transport failure")));
        var failure = new UptimeClient(failingHttp).ProbeAsync("offline.example").Result;
        Require(failure.Status == "down" && failure.StatusCode is null && failure.ResponseMs is null,
            "Transport errors must not be reported as measured HTTP status or latency");
        using var forbiddenHttp = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        try { new SitemapClient(forbiddenHttp).DiscoverUrlsAsync("offline.example").GetAwaiter().GetResult(); throw new InvalidOperationException("Expected sitemap rejection"); }
        catch (ApiRequestException) { /* Expected safe HTTP failure. */ }
    }

    private static readonly HttpStatusCode[] ProbeStatuses = [HttpStatusCode.OK, HttpStatusCode.Moved, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError];

    private static SqliteDatabase CreateDatabase() => (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
        .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;

    private sealed partial class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
