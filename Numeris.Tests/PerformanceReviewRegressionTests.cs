using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Insights;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Numeris.ViewModels;

internal static partial class PerformanceReviewRegressionTests
{
    private const string SyntheticKey = "synthetic&key=?value#";
    private const string TargetUrl = "https://test.example/page/?a=x%26y";

    public static void RejectsPageSpeedRuntimeErrorsWithoutReplacingHistory()
    {
        using var database = CreateDatabase();
        var repository = new PerformanceRepository(database);
        repository.AddUrlAsync(TargetUrl).GetAwaiter().GetResult();
        repository.AddUrlAsync("https://z-other.example/").GetAwaiter().GetResult();
        SeedStoredData(repository);
        var calls = 0;
        using var http = new HttpClient(new SyntheticHandler(request =>
        {
            calls++;
            var json = QueryValues(request, "url").Single() == TargetUrl
                ? PageSpeedJson.Replace("\"lighthouseVersion\":\"synthetic\",",
                    "\"runtimeError\":{\"code\":\"NO_FCP\",\"message\":\"Synthetic failure\"},\"lighthouseVersion\":\"synthetic\",")
                : PageSpeedJson;
            return Response(HttpStatusCode.OK, json);
        }));
        var vault = CreateVault(crux: false, pageSpeed: true);
        var connections = new ConnectionsRepository(database, vault);
        var sync = new PerformanceSyncService(new CruxClient(), new PageSpeedClient(http), repository, connections, vault);
        Require(!sync.TestPageSpeedAsync().GetAwaiter().GetResult().Ok, "A failed Lighthouse run must fail the source test");
        var result = sync.SyncConfiguredAsync().GetAwaiter().GetResult()!;
        Require(calls == 5 && result.PageSpeedErrors == 2 && result.PageSpeedRuns == 2,
            "Runtime errors must count per strategy, avoid retries, and allow later URLs to finish");
        Require(repository.GetLatestPageSpeedRunsAsync("test.example").GetAwaiter().GetResult()
            .Single().AnalysisUtc == "2026-08-28T12:00:00Z", "Failed runs must preserve prior history");
        Require(!string.IsNullOrEmpty(connections.GetPerformanceAsync().GetAwaiter().GetResult()!.LastSync),
            "Completed partial sync must reach retention and the last-sync update");
    }

    public static void RejectsInvalidPageSpeedAnalysisTimesWithoutReplacingHistory()
    {
        foreach (var replacement in new[] { "", "\"analysisUTCTimestamp\":\"invalid\"," })
        {
            AssertRejectedPageSpeedResponse(PageSpeedJson.Replace("\"analysisUTCTimestamp\":\"2026-10-01T12:30:00+02:00\",", replacement), "timestamp");
        }
    }

    public static void RejectsNonfinitePageSpeedScoresWithoutReplacingHistory()
    {
        foreach (var score in new[] { "1e400", "-1e400" })
        {
            AssertRejectedPageSpeedResponse(PageSpeedJson.Replace("\"performance\":{\"score\":0.91}",
                "\"performance\":{\"score\":" + score + "}"), "finite");
        }
    }

    public static void PreservesPageSpeedWarningsAndRequestedIdentity()
    {
        using var database = CreateDatabase();
        var repository = new PerformanceRepository(database);
        repository.AddUrlAsync(TargetUrl).GetAwaiter().GetResult();
        var json = PageSpeedJson.Replace("\"id\":\"https://test.example/page/?a=x%26y\"", "\"id\":\"https://redirect.example/final/\"")
            .Replace("\"lighthouseVersion\":\"synthetic\",", "\"runWarnings\":[\"Synthetic warning\"],\"lighthouseVersion\":\"synthetic\",");
        using var http = new HttpClient(new SyntheticHandler(_ => Response(HttpStatusCode.OK, json)));
        var vault = CreateVault(crux: false, pageSpeed: true);
        var sync = new PerformanceSyncService(new CruxClient(), new PageSpeedClient(http), repository,
            new ConnectionsRepository(database, vault), vault);
        Require(sync.TestPageSpeedAsync().GetAwaiter().GetResult().Ok, "Warnings alone must not fail the source test");
        var result = sync.SyncConfiguredAsync().GetAwaiter().GetResult()!;
        var runs = database.ReadAsync(c => c.Query<PageSpeedRun>("SELECT url AS Url, final_url AS FinalUrl, lighthouse_version AS LighthouseVersion, analysis_utc AS AnalysisUtc, warnings_json AS WarningsJson FROM pagespeed_runs").AsList()).GetAwaiter().GetResult();
        Require(result.PageSpeedRuns == 2 && runs.Count == 2 && runs.All(run => run.Url == TargetUrl
            && run.FinalUrl == "https://redirect.example/final/" && run.LighthouseVersion == "synthetic"
            && run.AnalysisUtc == "2026-10-01T10:30:00Z" && run.WarningsJson!.Contains("Synthetic warning", StringComparison.Ordinal)),
            "Warning-only runs must retain provider time/version/final URL under the requested identity");
    }

    private static void AssertRejectedPageSpeedResponse(string json, string message)
    {
        using var database = CreateDatabase();
        var repository = new PerformanceRepository(database);
        repository.AddUrlAsync(TargetUrl).GetAwaiter().GetResult();
        SeedStoredData(repository);
        using var http = new HttpClient(new SyntheticHandler(_ => Response(HttpStatusCode.OK, json)));
        var vault = CreateVault(crux: false, pageSpeed: true);
        var sync = new PerformanceSyncService(new CruxClient(), new PageSpeedClient(http), repository,
            new ConnectionsRepository(database, vault), vault);
        var test = sync.TestPageSpeedAsync().GetAwaiter().GetResult();
        Require(!test.Ok && test.Message.Contains(message, StringComparison.OrdinalIgnoreCase), "Unusable responses must not claim a successful source test");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                sync.SyncConfiguredAsync().GetAwaiter().GetResult();
                throw new Exception("Unusable responses must fail sync");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { }
            AssertStoredDataUnchanged(database, repository);
        }
    }

    public static void RetainsCruxCollectionDatesWhenNoReplacementIsStored()
    {
        using var database = (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
            .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var repository = new PerformanceRepository(database);
        repository.UpsertCruxMetricAsync(new CruxMetricPoint
        {
            TargetType = "origin",
            Target = "https://test.example",
            FormFactor = "PHONE",
            CollectionStart = "2026-08-01",
            CollectionEnd = "2026-08-28",
            Metric = "largest_contentful_paint",
            P75 = 2000,
            RawJson = "{}",
            FetchedAt = "2026-09-01T12:00:00",
        }).GetAwaiter().GetResult();

        // A no-data response stores no replacement point; reloading must expose the old date.
        var row = repository.GetLatestCruxCoreVitalsAsync("test.example").GetAwaiter().GetResult().Single();
        if (row.LatestCollectionEnd != "2026-08-28" || row.P75 != 2000 || row.Status != "Pass")
        {
            throw new InvalidOperationException("Retained CrUX values must keep their actual collection date and status");
        }
    }

    public static void RequestsCruxTargetsAndPreservesCollectionDates()
    {
        using var database = CreateDatabase();
        var repository = new PerformanceRepository(database);
        repository.AddUrlAsync(TargetUrl).GetAwaiter().GetResult();
        repository.AddUrlAsync("https://test.example/second/").GetAwaiter().GetResult();
        var requests = new HashSet<string>(StringComparer.Ordinal);
        using var cruxHttp = new HttpClient(new SyntheticHandler(request =>
        {
            Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/v1/records:queryHistoryRecord",
                "CrUX must POST to the history endpoint");
            Require(QueryValues(request, "key").SequenceEqual(new[] { SyntheticKey }), "CrUX must trim and escape the API key");
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var root = payload.RootElement;
            var isOrigin = root.TryGetProperty("origin", out var origin);
            var isUrl = root.TryGetProperty("url", out var url);
            Require(isOrigin != isUrl, "A CrUX request must contain exactly one target kind");
            var factor = root.TryGetProperty("formFactor", out var formFactor) ? formFactor.GetString()! : "ALL";
            Require(root.EnumerateObject().Count() == (factor == "ALL" ? 1 : 2), "Aggregate form factor must be omitted");
            Require(requests.Add($"{(isOrigin ? "origin" : "url")}|{(isOrigin ? origin : url).GetString()}|{factor}"),
                "Each target/form-factor must be requested once");
            return Response(HttpStatusCode.OK, CruxHistoryJson);
        }));
        using var pageSpeedHttp = UnexpectedHttp();
        var vault = CreateVault(crux: true, pageSpeed: false);
        var connections = new ConnectionsRepository(database, vault);
        var sync = new PerformanceSyncService(new CruxClient(cruxHttp), new PageSpeedClient(pageSpeedHttp), repository, connections, vault);

        var result = sync.SyncConfiguredAsync().GetAwaiter().GetResult()!;
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var factor in new[] { "ALL", "PHONE", "DESKTOP", "TABLET" })
        {
            expected.Add($"origin|https://test.example|{factor}");
            expected.Add($"url|{TargetUrl}|{factor}");
            expected.Add($"url|https://test.example/second/|{factor}");
        }
        Require(requests.SetEquals(expected) && result.UrlsSynced == 2 && result.CruxMetricPoints == 48
            && result.CruxSkipped == 0 && result.PageSpeedRuns == 0, "CrUX must deduplicate origins and store every returned metric/period");
        var points = database.ReadAsync(c => c.Query<CruxMetricPoint>(
            "SELECT collection_start AS CollectionStart, collection_end AS CollectionEnd, metric AS Metric, p75 AS P75, good_density AS GoodDensity, needs_improvement_density AS NeedsImprovementDensity, poor_density AS PoorDensity FROM crux_metric_points").AsList())
            .GetAwaiter().GetResult();
        Require(points.Count == 48 && points.All(point => point.CollectionStart == (point.CollectionEnd == "2026-08-28" ? "2026-08-01" : "2026-09-01")),
            "Collection dates must come from the aligned response periods");
        foreach (var point in points)
        {
            var first = point.CollectionEnd == "2026-08-28";
            Require(point.CollectionEnd is "2026-08-28" or "2026-09-28", "Collection date serialization changed");
            Require(point.P75 == (point.Metric == "largest_contentful_paint" ? (first ? 2000 : 3000) : (first ? 0.08 : 0.12))
                && point.GoodDensity == (first ? 0.8 : 0.6) && point.NeedsImprovementDensity == (first ? 0.15 : 0.25)
                && point.PoorDensity == (first ? 0.05 : 0.15), "Percentiles and densities must retain the same period index");
        }
        var latest = repository.GetLatestCruxCoreVitalsAsync("test.example").GetAwaiter().GetResult();
        Require(latest.Count == 24 && latest.All(row => row.LatestCollectionEnd == "2026-09-28"), "Latest rows must expose provider collection dates");
        var trend = repository.GetCruxTrendAsync("test.example", "largest_contentful_paint", "PHONE", "2026-08-01", "2026-09-30").GetAwaiter().GetResult();
        Require(trend.Select(row => row.P75).SequenceEqual(new double?[] { 2000, 3000 }), "Stored history must remain queryable by collection date");
    }

    public static void AllCruxNotFoundResponsesPreserveHistoryAndNeutralFeedback()
    {
        using var database = CreateDatabase();
        var repository = new PerformanceRepository(database);
        repository.AddUrlAsync(TargetUrl).GetAwaiter().GetResult();
        SeedStoredData(repository);
        var requests = 0;
        using var cruxHttp = new HttpClient(new SyntheticHandler(_ => { requests++; return Response(HttpStatusCode.NotFound, "{\"error\":{\"message\":\"No field data\"}}"); }));
        using var pageSpeedHttp = UnexpectedHttp();
        var vault = CreateVault(crux: true, pageSpeed: false);
        var connections = new ConnectionsRepository(database, vault);
        var sync = new PerformanceSyncService(new CruxClient(cruxHttp), new PageSpeedClient(pageSpeedHttp), repository, connections, vault);

        var test = sync.TestCruxAsync().GetAwaiter().GetResult();
        Require(requests == 2 && test.Ok && test.Message.Contains("2 configured target(s)", StringComparison.Ordinal)
            && test.Message.Contains("API key validity was not verified", StringComparison.Ordinal)
            && !test.Message.Contains("API key works", StringComparison.Ordinal), "Every 404 must preserve the no-data policy without claiming valid credentials");
        var result = sync.SyncConfiguredAsync().GetAwaiter().GetResult()!;
        Require(requests == 10 && result.UrlsSynced == 1 && result.CruxSkipped == 8 && result.CruxMetricPoints == 0
            && result.PageSpeedRuns == 0 && result.PageSpeedErrors == 0, "All 404 results must count skipped target/form-factor requests without saving points");
        using var overview = CreateOverview(database, vault, connections, sync);
        overview.RefreshAsync().GetAwaiter().GetResult();
        Require(requests == 18 && overview.RefreshSeverity.ToString() == "Warning" && overview.CanRefresh
            && overview.RefreshStatusMessage.StartsWith("No sources were updated.", StringComparison.Ordinal)
            && overview.RefreshStatusMessage.Contains("8 CrUX target/form-factor request(s) had no field data", StringComparison.Ordinal)
            && overview.RefreshStatusMessage.Contains("Performance: no new reports were stored", StringComparison.Ordinal)
            && !overview.RefreshStatusMessage.Contains("Bing, Performance in Sources", StringComparison.Ordinal),
            "Overview must distinguish configured no-data from an updated or missing Performance source");
        AssertStoredDataUnchanged(database, repository);
    }

    public static void Non404CruxFailuresPreserveStoredDataAndAbortPageSpeed()
    {
        using var database = CreateDatabase();
        var repository = new PerformanceRepository(database);
        repository.AddUrlAsync(TargetUrl).GetAwaiter().GetResult();
        SeedStoredData(repository);
        var requests = 0;
        using var cruxHttp = new HttpClient(new SyntheticHandler(_ => { requests++; return Response(HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"Synthetic access denied\"}}"); }));
        using var pageSpeedHttp = UnexpectedHttp();
        var vault = CreateVault(crux: true, pageSpeed: true);
        var connections = new ConnectionsRepository(database, vault);
        connections.UpsertPerformanceAsync(new PerformanceConnectionConfig(), "configured").GetAwaiter().GetResult();
        connections.UpdatePerformanceLastSyncAsync("2026-09-01T12:00:00", "configured").GetAwaiter().GetResult();
        var sync = new PerformanceSyncService(new CruxClient(cruxHttp), new PageSpeedClient(pageSpeedHttp), repository, connections, vault);
        var test = sync.TestCruxAsync().GetAwaiter().GetResult();
        Require(!test.Ok && test.Message.Contains("HTTP 403", StringComparison.Ordinal), "Non-404 CrUX errors must fail the connection test");
        try
        {
            sync.SyncConfiguredAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException("Non-404 CrUX errors must abort sync");
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden) { }
        using var report = new PerformanceViewModel(CreateShell(), repository, sync);
        report.RefreshAsync().GetAwaiter().GetResult();
        Require(requests == 3 && report.RefreshSeverity.ToString() == "Error" && report.CanRefresh
            && report.RefreshStatusMessage.Contains("HTTP 403", StringComparison.Ordinal)
            && report.RefreshStatusMessage.Contains("Previously loaded data is still shown", StringComparison.Ordinal),
            "Performance must report a failed sync and release its busy state");
        AssertStoredDataUnchanged(database, repository);
        Require(connections.GetPerformanceAsync().GetAwaiter().GetResult()!.LastSync == "2026-09-01T12:00:00",
            "An aborted CrUX workflow must preserve the completed-sync timestamp");
    }

    public static void PageSpeedMixedResultsCountSavedReportsAndPreserveFailedStrategy()
    {
        using var database = CreateDatabase();
        var repository = new PerformanceRepository(database);
        repository.AddUrlAsync(TargetUrl).GetAwaiter().GetResult();
        SeedStoredData(repository);
        var mobile = 0;
        var desktop = 0;
        using var cruxHttp = UnexpectedHttp();
        using var pageSpeedHttp = new HttpClient(new SyntheticHandler(request =>
        {
            Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/pagespeedonline/v5/runPagespeed"
                && QueryValues(request, "url").SequenceEqual(new[] { TargetUrl }) && QueryValues(request, "key").SequenceEqual(new[] { SyntheticKey }),
                "PageSpeed must preserve the encoded target and key");
            Require(QueryValues(request, "category").SequenceEqual(new[] { "PERFORMANCE", "ACCESSIBILITY", "BEST_PRACTICES", "SEO" }),
                "Every requested PageSpeed category must be retained");
            if (QueryValues(request, "strategy").Single() == "MOBILE")
            {
                mobile++;
                return Response(HttpStatusCode.OK, PageSpeedJson);
            }
            desktop++;
            return Response(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"Synthetic rate limit\"}}");
        }));
        var vault = CreateVault(crux: false, pageSpeed: true);
        var connections = new ConnectionsRepository(database, vault);
        var sync = new PerformanceSyncService(new CruxClient(cruxHttp), new PageSpeedClient(pageSpeedHttp), repository, connections, vault);
        var result = sync.SyncConfiguredAsync().GetAwaiter().GetResult()!;
        Require(mobile == 1 && desktop == 3 && result.UrlsSynced == 1 && result.PageSpeedRuns == 1 && result.PageSpeedAudits == 1
            && result.PageSpeedErrors == 1 && result.CruxMetricPoints == 0, "Saved report counters must exclude an exhausted strategy");
        var latest = repository.GetLatestPageSpeedRunsAsync("test.example").GetAwaiter().GetResult();
        var saved = latest.Single(row => row.Strategy == "MOBILE");
        var retained = latest.Single(row => row.Strategy == "DESKTOP");
        Require(saved.AnalysisUtc == "2026-10-01T10:30:00Z" && saved.PerformanceScore == 0.91
            && saved.AccessibilityScore == 0.92 && saved.BestPracticesScore == 0.93 && saved.SeoScore == 0.94
            && retained.AnalysisUtc == "2026-08-28T12:00:00Z" && retained.PerformanceScore == 0.75,
            "The successful strategy must store its UTC timestamp/scores while a failed strategy retains its old run");
        Require(database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM pagespeed_runs")).GetAwaiter().GetResult() == 2
            && database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM pagespeed_audits")).GetAwaiter().GetResult() == 2,
            "Mixed results must retain the failed strategy's report and audits");
    }

    public static void PageSpeedFailuresDoNotMarkOverviewUpdated()
    {
        using var database = CreateDatabase();
        var repository = new PerformanceRepository(database);
        repository.AddUrlAsync(TargetUrl).GetAwaiter().GetResult();
        SeedStoredData(repository);
        var requests = new List<string>();
        using var cruxHttp = UnexpectedHttp();
        using var pageSpeedHttp = new HttpClient(new SyntheticHandler(request =>
        {
            requests.Add(QueryValues(request, "strategy").Single());
            return Response(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"message\":\"Synthetic unavailable\"}}");
        }));
        var vault = CreateVault(crux: false, pageSpeed: true);
        var connections = new ConnectionsRepository(database, vault);
        var sync = new PerformanceSyncService(new CruxClient(cruxHttp), new PageSpeedClient(pageSpeedHttp), repository, connections, vault);
        using var overview = CreateOverview(database, vault, connections, sync);
        overview.RefreshAsync().GetAwaiter().GetResult();
        Require(requests.SequenceEqual(PerformanceReviewRegressionTestsInputs.Vector1)
            && overview.RefreshSeverity.ToString() == "Error" && overview.CanRefresh
            && overview.RefreshStatusMessage.StartsWith("No sources were updated.", StringComparison.Ordinal)
            && overview.RefreshStatusMessage.Contains("Performance: 2 PageSpeed request(s) failed", StringComparison.Ordinal)
            && overview.RefreshStatusMessage.Contains("Performance: no new reports were stored", StringComparison.Ordinal)
            && !overview.RefreshStatusMessage.Contains("Bing, Performance in Sources", StringComparison.Ordinal),
            "Overview must expose exhausted requests without treating configured Performance as updated or absent");
        AssertStoredDataUnchanged(database, repository);
    }

    private static SqliteDatabase CreateDatabase()
    {
        var database = (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
            .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        database.WriteAsync(c => c.Execute("DELETE FROM performance_urls")).GetAwaiter().GetResult();
        return database;
    }

    private static CredentialVault CreateVault(bool crux, bool pageSpeed)
        => CredentialVaultRegressionTests.CreateVault((resource, _) =>
            resource == "Numeris.Crux.ApiKey" && crux || resource == "Numeris.PageSpeed.ApiKey" && pageSpeed
                ? " " + SyntheticKey + " "
                : throw new COMException("Synthetic missing credential", unchecked((int)0x80070490)));

    private static ShellViewModel CreateShell()
    {
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, "test.example");
        typeof(ShellViewModel).GetField("<SelectedPeriod>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, Period.Last7Days);
        return shell;
    }

    private static DashboardViewModel CreateOverview(SqliteDatabase database, CredentialVault vault, ConnectionsRepository connections, PerformanceSyncService sync)
    {
        var cf = new CloudflareRepository(database);
        var sc = new SearchConsoleRepository(database);
        var bing = new BingRepository(database);
        var sitemap = new SitemapRepository(database);
        return new DashboardViewModel(CreateShell(), new SummaryRepository(database), cf, sc, bing, sitemap,
            new InsightMetricsRepository(database),
            new CloudflareSyncService(new CloudflareGraphqlClient(), cf, vault, connections),
            new SearchConsoleSyncService(new SearchConsoleClient(), new GoogleOAuthClient(), sc, sitemap, vault, connections),
            new BingWebmasterSyncService(new BingWebmasterClient(), bing, connections, vault), sync)
        { IsLoading = false };
    }

    private static void SeedStoredData(PerformanceRepository repository)
    {
        repository.UpsertCruxMetricAsync(new CruxMetricPoint
        {
            TargetType = "origin",
            Target = "https://test.example",
            FormFactor = "PHONE",
            Metric = "largest_contentful_paint",
            CollectionStart = "2026-08-01",
            CollectionEnd = "2026-08-28",
            P75 = 2000,
            RawJson = "{}",
            FetchedAt = "2026-09-01T12:00:00",
        }).GetAwaiter().GetResult();
        repository.UpsertPageSpeedRunAsync(new PageSpeedRun
        {
            Url = TargetUrl,
            Strategy = "DESKTOP",
            AnalysisUtc = "2026-08-28T12:00:00Z",
            PerformanceScore = 0.75,
            RawJson = "{}",
            FetchedAt = "2026-09-01T12:00:00",
        }, new[] { new PageSpeedAudit { Url = TargetUrl, Strategy = "DESKTOP", AnalysisUtc = "2026-08-28T12:00:00Z", AuditId = "retained-audit", Score = 0.5 } }).GetAwaiter().GetResult();
    }

    private static void AssertStoredDataUnchanged(SqliteDatabase database, PerformanceRepository repository)
    {
        var vitals = repository.GetLatestCruxCoreVitalsAsync("test.example").GetAwaiter().GetResult().Single();
        var run = repository.GetLatestPageSpeedRunsAsync("test.example").GetAwaiter().GetResult().Single();
        Require(vitals.LatestCollectionEnd == "2026-08-28" && vitals.P75 == 2000
            && run.AnalysisUtc == "2026-08-28T12:00:00Z" && run.PerformanceScore == 0.75
            && database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM crux_metric_points")).GetAwaiter().GetResult() == 1
            && database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM pagespeed_runs")).GetAwaiter().GetResult() == 1
            && database.ReadAsync(c => c.ExecuteScalar<string>("SELECT audit_id FROM pagespeed_audits")).GetAwaiter().GetResult() == "retained-audit",
            "Unavailable provider responses must preserve previously stored history, dates, scores and audits");
    }

    private static string[] QueryValues(HttpRequestMessage request, string name)
        => request.RequestUri!.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
            .Where(pair => pair[0] == name).Select(pair => Uri.UnescapeDataString(pair[1])).ToArray();

    private static HttpClient UnexpectedHttp()
        => new(new SyntheticHandler(_ => throw new InvalidOperationException("An unconfigured/aborted provider was called")));

    private static HttpResponseMessage Response(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed partial class SyntheticHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string CruxHistoryJson = """
        {"record":{"collectionPeriods":[{"firstDate":{"year":2026,"month":8,"day":1},"lastDate":{"year":2026,"month":8,"day":28}},{"firstDate":{"year":2026,"month":9,"day":1},"lastDate":{"year":2026,"month":9,"day":28}}],"metrics":{"largest_contentful_paint":{"percentilesTimeseries":{"p75s":["2000","3000"]},"histogramTimeseries":[{"densities":[0.8,0.6]},{"densities":[0.15,0.25]},{"densities":[0.05,0.15]}]},"cumulative_layout_shift":{"percentilesTimeseries":{"p75s":["0.08","0.12"]},"histogramTimeseries":[{"densities":[0.8,0.6]},{"densities":[0.15,0.25]},{"densities":[0.05,0.15]}]}}}}
        """;

    private const string PageSpeedJson = """
        {"id":"https://test.example/page/?a=x%26y","analysisUTCTimestamp":"2026-10-01T12:30:00+02:00","lighthouseResult":{"lighthouseVersion":"synthetic","categories":{"performance":{"score":0.91},"accessibility":{"score":0.92},"best-practices":{"score":0.93},"seo":{"score":0.94}},"audits":{"synthetic-audit":{"title":"Synthetic audit","score":0.5,"numericValue":2500,"numericUnit":"millisecond","displayValue":"2.5 s","scoreDisplayMode":"numeric","details":{"type":"table","items":[]}}}}}
        """;
}

internal static class PerformanceReviewRegressionTestsInputs
{
    internal static readonly string[] Vector1 = new[] { "MOBILE", "MOBILE", "MOBILE", "DESKTOP", "DESKTOP", "DESKTOP" };
}
