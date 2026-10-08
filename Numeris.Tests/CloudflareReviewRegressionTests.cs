using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Sync;
using Numeris.ViewModels;
using Numeris.ViewModels.Sources;

internal static partial class CloudflareReviewRegressionTests
{
    private const string EmptyZone = """
        {"data":{"viewer":{"zones":[{"httpRequests1dGroups":[],"topCountries":[],"topPages":[]}]}},"errors":null}
        """;
    private const string EmptyRum = """
        {"data":{"viewer":{"accounts":[{"daily":[],"referrers":[],"pages":[],"countries":[]}]}},"errors":null}
        """;

    public static void RejectsMalformedZoneResponses()
    {
        Require(ParseZone(EmptyZone).Daily.Count == 0, "Valid empty Zone groups must remain successful");
        var groupNames = new[] { "httpRequests1dGroups", "topCountries", "topPages" };
        foreach (var name in groupNames)
        {
            var missing = JsonNode.Parse(EmptyZone)!;
            missing["data"]!["viewer"]!["zones"]![0]!.AsObject().Remove(name);
            Reject(() => ParseZone(missing.ToJsonString()));
            var nullGroup = JsonNode.Parse(EmptyZone)!;
            nullGroup["data"]!["viewer"]!["zones"]![0]![name] = null;
            Reject(() => ParseZone(nullGroup.ToJsonString()));
        }
        foreach (var nodes in new[] { "[]", "[{},{}]", "[{}]", "[null]" })
            Reject(() => ParseZone("{\"data\":{\"viewer\":{\"zones\":" + nodes + "}}}"));

        for (var groupIndex = 0; groupIndex < groupNames.Length; groupIndex++)
        {
            foreach (var date in new string?[] { null, "", "2026-02-30", "2026/10/08" })
            {
                var response = JsonNode.Parse(EmptyZone)!;
                var dimensions = new JsonObject { ["date"] = date, ["clientCountryName"] = "FI", ["clientRequestPath"] = "/" };
                response["data"]!["viewer"]!["zones"]![0]![groupNames[groupIndex]]!.AsArray()
                    .Add(new JsonObject { ["dimensions"] = dimensions });
                Reject(() => ParseZone(response.ToJsonString()));
            }
        }
        Reject(() => ParseZone(EmptyZone.Replace("\"errors\":null", "\"errors\":[{\"message\":\"Synthetic failure\"}]", StringComparison.Ordinal)));
    }

    public static void RejectsMalformedRumResponses()
    {
        Require(ParseRum(EmptyRum).Daily.Count == 0, "Valid empty RUM groups must remain successful");
        var groupNames = new[] { "daily", "referrers", "pages", "countries" };
        foreach (var name in groupNames)
        {
            var missing = JsonNode.Parse(EmptyRum)!;
            missing["data"]!["viewer"]!["accounts"]![0]!.AsObject().Remove(name);
            Reject(() => ParseRum(missing.ToJsonString()));
            var nullGroup = JsonNode.Parse(EmptyRum)!;
            nullGroup["data"]!["viewer"]!["accounts"]![0]![name] = null;
            Reject(() => ParseRum(nullGroup.ToJsonString()));
        }
        foreach (var nodes in new[] { "[]", "[{},{}]", "[{}]", "[null]" })
            Reject(() => ParseRum("{\"data\":{\"viewer\":{\"accounts\":" + nodes + "}}}"));

        foreach (var name in groupNames)
        {
            foreach (var date in new string?[] { null, "", "2026-02-30", "2026/10/08" })
            {
                var response = JsonNode.Parse(EmptyRum)!;
                response["data"]!["viewer"]!["accounts"]![0]![name]!.AsArray()
                    .Add(new JsonObject { ["dimensions"] = new JsonObject { ["date"] = date } });
                Reject(() => ParseRum(response.ToJsonString()));
            }
        }
        Reject(() => ParseRum(EmptyRum.Replace("\"errors\":null", "\"errors\":[{\"message\":\"Synthetic failure\"}]", StringComparison.Ordinal)));
    }

    public static void PreservesProviderDatesAndDistinctMetrics()
    {
        var zone = ParseZone("""
            {"data":{"viewer":{"zones":[{
              "httpRequests1dGroups":[{"dimensions":{"date":"2026-10-07"},
                "sum":{"pageViews":2,"requests":3,"cachedRequests":4,"cachedBytes":5,"bytes":6,"threats":7,
                  "responseStatusMap":[{"edgeResponseStatus":200,"requests":9}]},"uniq":{"uniques":8}}],
              "topCountries":[{"dimensions":{"date":"2026-10-07","clientCountryName":"FI"},"sum":{"visits":10},"count":11}],
              "topPages":[{"dimensions":{"date":"2026-10-08","clientRequestPath":"/"},"sum":{"visits":0},"count":12}]
            }]}},"errors":null}
            """);
        var daily = zone.Daily.Single();
        Require(daily.Date == "2026-10-07" && daily.Pageviews == 2 && daily.Requests == 3 && daily.CachedRequests == 4
            && daily.CachedBytes == 5 && daily.TotalBytes == 6 && daily.Threats == 7 && daily.UniqueVisitors == 8,
            "Zone parsing must preserve each date and distinct provider metric");
        Require(zone.StatusCodes.Single().Date == daily.Date && zone.StatusCodes.Single().Requests == 9,
            "Status maps must retain their daily group identity");
        Require(zone.Countries.Single().Date == "2026-10-07" && zone.Countries.Single().Value == 10
            && zone.Pages.Single().Date == "2026-10-08" && zone.Pages.Single().Value == 12,
            "Breakdowns must preserve individual dates and visits/count selection");

        var rum = ParseRum("""
            {"data":{"viewer":{"accounts":[{
              "daily":[{"dimensions":{"date":"2026-10-07"},"sum":{"visits":13},"count":14}],
              "referrers":[{"dimensions":{"date":"2026-10-07","refererHost":"ref.example"},"sum":{"visits":15}}],
              "pages":[{"dimensions":{"date":"2026-10-08","requestPath":"/"},"count":16}],
              "countries":[{"dimensions":{"date":"2026-10-08","countryName":"FI"},"sum":{"visits":17}}]
            }]}},"errors":null}
            """);
        Require(rum.Daily.Single().Date == "2026-10-07" && rum.Daily.Single().Visits == 13 && rum.Daily.Single().PageViews == 14,
            "RUM visits and pageviews must stay distinct");
        Require(rum.Referrers.Single().Date == "2026-10-07" && rum.Referrers.Single().Visits == 15
            && rum.Pages.Single().Date == "2026-10-08" && rum.Pages.Single().PageViews == 16
            && rum.Countries.Single().Date == "2026-10-08" && rum.Countries.Single().Visits == 17,
            "RUM categories must preserve their own dates and metric fields");
    }

    public static void GroupsOnlyActual5xxAsServerErrors()
    {
        var rows = CloudflareReviewRegressionTestsInputs.Vector1
            .Select(code => new StatusCodeDay { Date = "2026-10-08", StatusCode = code, Requests = 1 }).ToArray();
        var insight = CloudflareViewModel.BuildStatusCodeInsight(rows);
        foreach (var group in new[] { StatusCodeGroup.Success, StatusCodeGroup.Redirect, StatusCodeGroup.ClientError, StatusCodeGroup.ServerError })
            Require(insight.Groups.Single(item => item.Group == group).Total == 2,
                "Each HTTP partial class must include only its exact lower and upper boundaries");
        Require(insight.Groups.Single(group => group.Group == StatusCodeGroup.Other).Total == 3,
            "Informational and unknown codes must not be mislabeled as server errors");
        Require(insight.Groups.Sum(group => group.Total) == rows.Length && insight.TopCodes.All(code => int.Parse(code.Code) < 600),
            "Each response must have one group and top issues must be 4xx/5xx");
        var known = CloudflareViewModel.BuildStatusCodeInsight(rows.Where(row => row.StatusCode is >= 200 and <= 599).ToArray());
        Require(known.Groups.Count == 4, "Known status responses must retain the original four chart groups");
    }

    public static void ReloadsAllSavedMappingsAfterDiscoveryUpsert()
    {
        using var database = (SqliteDatabase)typeof(DailyBreakdownRegressionTests)
            .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var repository = new WebAnalyticsRepository(database);
        repository.SaveSitesAsync(new[] { new WebAnalyticsSite { Host = "manual.example", SiteTag = "manual" } }, "now")
            .GetAwaiter().GetResult();
        repository.SaveSitesAsync(new[] { new WebAnalyticsSite { Host = "found.example", SiteTag = "found" } }, "now")
            .GetAwaiter().GetResult();
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        var viewModel = new WebAnalyticsSourceViewModel(connections, vault,
            new WebAnalyticsSyncService(new CloudflareRumClient(), repository, vault, connections));
        viewModel.Sites.Add(new WebAnalyticsSite { Host = "found.example", SiteTag = "found" });
        ((Task)typeof(WebAnalyticsSourceViewModel).GetMethod("LoadSitesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, null)!).GetAwaiter().GetResult();
        Require(viewModel.Sites.Count == 2 && viewModel.Sites.Any(site => site.Host == "manual.example" && site.SiteTag == "manual"),
            "The reload used after discovery must show manual mappings that discovery did not replace");
    }

    public static void SyncsZoneHttpResponsesWithoutReplacingDataOnMalformedPayloads()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault((_, _) => "Bearer synthetic-token");
        var connections = new ConnectionsRepository(database, vault);
        var repository = new CloudflareRepository(database);
        connections.UpsertCloudflareAsync(new CloudflareConnectionConfig { Domain = "zone.example", ZoneId = "synthetic-zone" }, "configured")
            .GetAwaiter().GetResult();
        connections.UpsertCloudflareAsync(new CloudflareConnectionConfig { Domain = "other.example", ZoneId = "other-zone" }, "configured")
            .GetAwaiter().GetResult();
        var today = Today();
        var payload = ZoneHttpRows();
        var handler = new HttpFixture((request, body) =>
        {
            Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/graphql", StringComparison.Ordinal),
                "Zone sync must use the GraphQL endpoint");
            var json = JsonNode.Parse(body)!;
            var variables = json["variables"]!;
            Require((string?)variables["zoneTag"] == "synthetic-zone" && (string?)variables["until"] == today
                && (string?)variables["since"] == DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-3649).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                && (string?)variables["adaptiveSince"] == DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-29).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z"
                && (string?)variables["adaptiveUntil"] == DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z",
                "Daily and adaptive requests must retain their separate windows and selected target");
            Require(((string?)json["query"])!.Contains("requestSource: \"eyeball\"", StringComparison.Ordinal), "Adaptive traffic must retain the eyeball filter");
            return Response(payload);
        });
        using var http = new HttpClient(handler);
        var service = new CloudflareSyncService(new CloudflareGraphqlClient(http), repository, vault, connections);
        var normal = service.SyncConfiguredAsync("https://ZONE.EXAMPLE/", 3650).GetAwaiter().GetResult().Single();
        Require(handler.Requests == 1 && normal.RecordsUpserted == 4 && normal.DaysSynced == 1,
            "Only the selected configured zone must sync all returned record categories");
        var daily = repository.GetTrafficDailyAsync("zone.example", today, today).GetAwaiter().GetResult().Single();
        var cache = repository.GetCacheDailyAsync("zone.example", today, today).GetAwaiter().GetResult().Single();
        Require(daily.Pageviews == 2 && daily.UniqueVisitors == 8 && cache.CachedRequests == 4 && cache.TotalRequests == 3
            && cache.CachedBytes == 5 && cache.TotalBytes == 6
            && repository.GetSecurityDailyAsync("zone.example", today, today).GetAwaiter().GetResult().Single().Threats == 7
            && repository.GetStatusCodesDailyAsync("zone.example", today, today).GetAwaiter().GetResult().Single().Requests == 9
            && repository.GetTrafficCountriesAsync("zone.example", today, today).GetAwaiter().GetResult().Single().Value == 10
            && repository.GetTrafficPagesAsync("zone.example", today, today).GetAwaiter().GetResult().Single().Pageviews == 12,
            "The actual sync must persist distinct daily, cache, security, status and category metrics");
        Require(connections.ListCloudflareConnectionsAsync().GetAwaiter().GetResult().Single(item => item.Domain == "zone.example") is { Status: "connected", LastSync: not null },
            "A completed HTTP sync must update its connection timestamp");

        connections.UpdateCloudflareLastSyncAsync("zone.example", "before-malformed").GetAwaiter().GetResult();
        var before = Snapshot(database);
        var malformed = new List<string>
        {
            EmptyZone.Replace("\"topPages\":[]", "\"topPages\":null", StringComparison.Ordinal),
            "{\"data\":{\"viewer\":{\"zones\":[]}}}",
            EmptyZone.Replace("\"errors\":null", "\"errors\":[{\"message\":\"Synthetic failure\"}]", StringComparison.Ordinal),
        };
        foreach (var group in new[] { "httpRequests1dGroups", "topCountries", "topPages" })
        {
            var invalid = JsonNode.Parse(ZoneHttpRows())!;
            invalid["data"]!["viewer"]!["zones"]![0]![group]![0]!["dimensions"]!["date"] = "2026-02-30";
            malformed.Add(invalid.ToJsonString());
        }
        foreach (var invalid in malformed)
        {
            payload = invalid;
            ExpectFailure(() => service.SyncDomainAsync("zone.example", "synthetic-zone", 3650).GetAwaiter().GetResult());
            Require(Snapshot(database) == before, "Rejected Zone responses must preserve every stored row and last_sync");
        }

        payload = EmptyZone;
        connections.UpdateCloudflareLastSyncAsync("zone.example", "before-empty").GetAwaiter().GetResult();
        var empty = service.SyncDomainAsync("zone.example", "synthetic-zone", 3650).GetAwaiter().GetResult();
        Require(empty.DaysSynced == 0 && empty.RecordsUpserted == 0
            && repository.GetTrafficCountriesAsync("zone.example", today, today).GetAwaiter().GetResult().Count == 0
            && repository.GetTrafficPagesAsync("zone.example", today, today).GetAwaiter().GetResult().Count == 0
            && repository.GetTrafficDailyAsync("zone.example", today, today).GetAwaiter().GetResult().Count == 1
            && repository.GetStatusCodesDailyAsync("zone.example", today, today).GetAwaiter().GetResult().Count == 1
            && connections.ListCloudflareConnectionsAsync().GetAwaiter().GetResult().Single(item => item.Domain == "zone.example").LastSync != "before-empty",
            "Valid empty HTTP groups must complete, replace their category range and retain upsert-only daily/status history");
    }

    public static void SyncsRumHttpResponsesWithoutAdvancingLastSyncOnFailure()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault((_, _) => "synthetic-token");
        var connections = new ConnectionsRepository(database, vault);
        var repository = new WebAnalyticsRepository(database);
        connections.UpsertWebAnalyticsAsync(new WebAnalyticsConnectionConfig { AccountId = "synthetic-account" }, "configured")
            .GetAwaiter().GetResult();
        repository.SaveSitesAsync(new[]
        {
            new WebAnalyticsSite { Host = "rum.example", SiteTag = "first-tag" },
            new WebAnalyticsSite { Host = "second.example", SiteTag = "second-tag" },
        }, "before").GetAwaiter().GetResult();
        var today = Today();
        var payload = RumHttpRows();
        var failSecond = false;
        var tags = new List<string>();
        var starts = new List<string?>();
        var handler = new HttpFixture((request, body) =>
        {
            Require(request.Method == HttpMethod.Post, "RUM sync and test must use GraphQL");
            var variables = JsonNode.Parse(body)!["variables"]!;
            var tag = (string?)variables["siteTag"];
            var sinceDate = (string?)variables["sinceDate"];
            var oldest = DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-89).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Require((string?)variables["accountTag"] == "synthetic-account" && (tag is "first-tag" or "second-tag")
                && (string?)variables["untilDate"] == today && (sinceDate == oldest || sinceDate == today)
                && (string?)variables["since"] == sinceDate + "T00:00:00Z" && (string?)variables["until"] == today + "T23:59:59Z",
                "RUM requests must retain the configured account/site and clamped sync or today-only test window");
            tags.Add(tag!);
            starts.Add(sinceDate);
            return Response(failSecond && tag == "second-tag" ? EmptyRum.Replace("\"pages\":[]", "\"pages\":null", StringComparison.Ordinal) : payload);
        });
        using var http = new HttpClient(handler);
        var service = new WebAnalyticsSyncService(new CloudflareRumClient(http), repository, vault, connections);
        var normal = service.SyncConfiguredAsync(100).GetAwaiter().GetResult()!;
        Require(normal.RecordsUpserted == 8 && normal.DaysSynced == 2 && tags.OrderBy(tag => tag, StringComparer.Ordinal).SequenceEqual(CloudflareReviewRegressionTestsInputs.Vector2),
            "Account sync must visit both saved mappings and persist all returned categories");
        Require(starts.All(start => start == DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-89).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "A request for 100 days must fetch exactly the clamped 90-day inclusive window");
        Require(repository.GetDailyAsync("rum.example", today, today).GetAwaiter().GetResult().Single() is { Visits: 13, PageViews: 14 }
            && repository.GetReferrersAsync("rum.example", today, today).GetAwaiter().GetResult().Single().Visits == 15
            && repository.GetPagesAsync("rum.example", today, today).GetAwaiter().GetResult().Single().PageViews == 16
            && repository.GetCountriesAsync("rum.example", today, today).GetAwaiter().GetResult().Single().Value == 17
            && connections.GetWebAnalyticsAsync().GetAwaiter().GetResult() is { Status: "connected", LastSync: not null },
            "Actual RUM sync must retain distinct visits/pageviews/categories and timestamp completion");

        connections.UpdateWebAnalyticsLastSyncAsync("synthetic-account", "before-tests").GetAwaiter().GetResult();
        var before = Snapshot(database);
        foreach (var testPayload in new[] { RumHttpRows(), EmptyRum })
        {
            payload = testPayload;
            var result = service.TestAccountAsync("synthetic-account").GetAwaiter().GetResult();
            Require(result.Ok && starts.Last() == today && (testPayload != EmptyRum || result.Message.Contains("empty", StringComparison.Ordinal))
                && Snapshot(database) == before, "Account tests must remain read-only for normal and valid empty responses");
        }
        var malformed = new List<string>
        {
            EmptyRum.Replace("\"pages\":[]", "\"pages\":null", StringComparison.Ordinal),
            "{\"data\":{\"viewer\":{\"accounts\":[]}}}",
            EmptyRum.Replace("\"errors\":null", "\"errors\":[{\"message\":\"Synthetic failure\"}]", StringComparison.Ordinal),
        };
        foreach (var group in new[] { "daily", "referrers", "pages", "countries" })
        {
            var invalid = JsonNode.Parse(RumHttpRows())!;
            invalid["data"]!["viewer"]!["accounts"]![0]![group]![0]!["dimensions"]!["date"] = "2026-02-30";
            malformed.Add(invalid.ToJsonString());
        }
        foreach (var invalid in malformed)
        {
            payload = invalid;
            ExpectFailure(() => service.SyncAccountAsync("synthetic-account", 100).GetAwaiter().GetResult());
            Require(!service.TestAccountAsync("synthetic-account").GetAwaiter().GetResult().Ok && Snapshot(database) == before,
                "Rejected RUM responses must preserve every stored row and last_sync in both sync and test paths");
        }

        payload = RumHttpRows().Replace("\"visits\":13", "\"visits\":100", StringComparison.Ordinal);
        failSecond = true;
        connections.UpdateWebAnalyticsLastSyncAsync("synthetic-account", "before-partial").GetAwaiter().GetResult();
        ExpectFailure(() => service.SyncAccountAsync("synthetic-account", 100).GetAwaiter().GetResult());
        Require(repository.GetDailyAsync("rum.example", today, today).GetAwaiter().GetResult().Single().Visits == 100
            && repository.GetDailyAsync("second.example", today, today).GetAwaiter().GetResult().Single().Visits == 13
            && connections.GetWebAnalyticsAsync().GetAwaiter().GetResult()!.LastSync == "before-partial",
            "A later-site failure must retain earlier committed work without claiming account completion");

        failSecond = false;
        payload = EmptyRum;
        var empty = service.SyncAccountAsync("synthetic-account", 100).GetAwaiter().GetResult();
        Require(empty.RecordsUpserted == 0 && empty.DaysSynced == 0 && connections.GetWebAnalyticsAsync().GetAwaiter().GetResult()!.LastSync != "before-partial",
            "Valid empty account responses must complete and advance last_sync");
        foreach (var domain in new[] { "rum.example", "second.example" })
            Require(repository.GetDailyAsync(domain, today, today).GetAwaiter().GetResult().Count == 1
                && repository.GetReferrersAsync(domain, today, today).GetAwaiter().GetResult().Count == 0
                && repository.GetPagesAsync(domain, today, today).GetAwaiter().GetResult().Count == 0
                && repository.GetCountriesAsync(domain, today, today).GetAwaiter().GetResult().Count == 0,
                "Valid empty RUM categories replace their range while daily history follows its upsert policy");
    }

    public static void KeepsSavedZoneHttpTestsReadOnly()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault((_, _) => "synthetic-token");
        var connections = new ConnectionsRepository(database, vault);
        var repository = new CloudflareRepository(database);
        connections.UpsertCloudflareAsync(new CloudflareConnectionConfig { Domain = "zone.example", ZoneId = "synthetic-zone" }, "configured")
            .GetAwaiter().GetResult();
        var payload = ZoneHttpRows();
        var handler = new HttpFixture((request, body) => request.Method == HttpMethod.Get
            ? Response("{\"success\":true,\"result\":{\"name\":\"ZONE.EXAMPLE\"}}") : Response(payload));
        using var http = new HttpClient(handler);
        var service = new CloudflareSyncService(new CloudflareGraphqlClient(http), repository, vault, connections);
        service.SyncDomainAsync("zone.example", "synthetic-zone", 1).GetAwaiter().GetResult();
        connections.UpdateCloudflareLastSyncAsync("zone.example", "before-read-only").GetAwaiter().GetResult();
        var before = Snapshot(database);
        foreach (var testPayload in new[] { ZoneHttpRows(), EmptyZone, EmptyZone.Replace("\"topPages\":[]", "\"topPages\":null", StringComparison.Ordinal) })
        {
            payload = testPayload;
            var requests = handler.Requests;
            var result = service.TestDomainAsync(" ZONE.EXAMPLE ", " synthetic-zone ").GetAwaiter().GetResult();
            Require(result.Ok == (testPayload == ZoneHttpRows() || testPayload == EmptyZone)
                && (testPayload != EmptyZone || result.Message.Contains("empty", StringComparison.Ordinal))
                && handler.Requests == requests + 2 && Snapshot(database) == before,
                "Saved zone tests must validate identity, distinguish empty/failure and leave all rows and metadata unchanged");
        }
    }

    public static void RejectsUnsavedMismatchedZoneThroughSourceTest()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        var name = "other.example";
        var handler = new HttpFixture((request, _) =>
        {
            Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/zones/synthetic-zone", StringComparison.Ordinal),
                "Unsaved tests must use the entered zone ID and only the zone-details endpoint");
            return Response("{\"success\":true,\"result\":{\"name\":\"" + name + "\"}}");
        });
        using var http = new HttpClient(handler);
        var client = new CloudflareGraphqlClient(http);
        var service = new CloudflareSyncService(client, new CloudflareRepository(database), vault, connections);
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        var source = new CloudflareSourceViewModel(connections, vault, client, service, shell)
        {
            NewDomain = "https://ENTERED.EXAMPLE/",
            NewZoneId = " synthetic-zone ",
            NewToken = " Bearer synthetic-token ",
        };
        var before = Snapshot(database);
        source.TestAsync().GetAwaiter().GetResult();
        Require(source.StatusMessage.StartsWith("Failed:", StringComparison.Ordinal) && source.StatusMessage.Contains("other.example", StringComparison.Ordinal)
            && source.StatusMessage.Contains("entered.example", StringComparison.Ordinal) && source.CanRun && handler.Requests == 1 && Snapshot(database) == before,
            "An unsaved mismatched zone must visibly fail without saving credentials, metadata or analytics");
        name = "ENTERED.EXAMPLE";
        source.TestAsync().GetAwaiter().GetResult();
        Require(source.StatusMessage == "OK - token validates entered.example" && source.CanRun && handler.Requests == 2 && Snapshot(database) == before,
            "A matching normalized domain must validate without persisting the unsaved connection");
    }

    public static void DiscoversSitesThroughHttpAndPreservesManualMappingsInSource()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault((_, _) => "synthetic-token");
        var connections = new ConnectionsRepository(database, vault);
        var repository = new WebAnalyticsRepository(database);
        connections.UpsertWebAnalyticsAsync(new WebAnalyticsConnectionConfig { AccountId = "synthetic-account" }, "configured")
            .GetAwaiter().GetResult();
        connections.UpdateWebAnalyticsLastSyncAsync("synthetic-account", "before-discovery").GetAwaiter().GetResult();
        repository.SaveSitesAsync(new[] { new WebAnalyticsSite { Host = "manual.example", SiteTag = "manual-tag" } }, "manual-before")
            .GetAwaiter().GetResult();
        var payload = "{\"success\":true,\"result\":[{\"site_tag\":\" found-tag \",\"host\":\" FOUND.EXAMPLE \"}]}";
        var status = HttpStatusCode.OK;
        var handler = new HttpFixture((request, _) =>
        {
            Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/accounts/synthetic-account/rum/site_info/list", StringComparison.Ordinal),
                "Discovery must request site mappings for the configured account");
            return Response(payload, status);
        });
        using var http = new HttpClient(handler);
        var source = new WebAnalyticsSourceViewModel(connections, vault,
            new WebAnalyticsSyncService(new CloudflareRumClient(http), repository, vault, connections));
        source.LoadAsync().GetAwaiter().GetResult();
        source.DiscoverSitesAsync().GetAwaiter().GetResult();
        Require(source.StatusMessage == "Found 1 site(s)" && source.CanRun && source.Sites.Count == 2
            && source.Sites.Single(site => site.Host == "manual.example").SiteTag == "manual-tag"
            && source.Sites.Single(site => site.Host == "found.example").SiteTag == "found-tag"
            && repository.ListSitesAsync().GetAwaiter().GetResult().Count == 2
            && connections.GetWebAnalyticsAsync().GetAwaiter().GetResult()!.LastSync == "before-discovery",
            "The full HTTP discovery/upsert/reload flow must show retained manual mappings without advancing sync metadata");
        var before = Snapshot(database);
        payload = "{\"success\":true,\"result\":[]}";
        source.DiscoverSitesAsync().GetAwaiter().GetResult();
        Require(source.StatusMessage == "Found 0 site(s)" && source.Sites.Count == 2 && Snapshot(database) == before,
            "A valid empty discovery result must retain every saved mapping");
        status = HttpStatusCode.Forbidden;
        payload = "{\"success\":false,\"errors\":[{\"code\":10000,\"message\":\"Synthetic discovery permission failure\"}]}";
        source.DiscoverSitesAsync().GetAwaiter().GetResult();
        Require(source.StatusMessage.Contains("Discovery failed:", StringComparison.Ordinal)
            && source.StatusMessage.Contains("Saved mappings can still sync", StringComparison.Ordinal)
            && source.CanRun && source.Sites.Count == 2 && handler.Requests == 3 && Snapshot(database) == before,
            "Discovery permission failure must preserve mappings and expose the manual-sync fallback");
    }

    private static SqliteDatabase CreateDatabase()
        => (SqliteDatabase)typeof(DailyBreakdownRegressionTests)
            .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;

    private static string Today() => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ZoneHttpRows() => """
        {"data":{"viewer":{"zones":[{
          "httpRequests1dGroups":[{"dimensions":{"date":"DAY"},
            "sum":{"pageViews":2,"requests":3,"cachedRequests":4,"cachedBytes":5,"bytes":6,"threats":7,
              "responseStatusMap":[{"edgeResponseStatus":200,"requests":9}]},"uniq":{"uniques":8}}],
          "topCountries":[{"dimensions":{"date":"DAY","clientCountryName":"FI"},"sum":{"visits":10},"count":11}],
          "topPages":[{"dimensions":{"date":"DAY","clientRequestPath":"/fresh"},"count":12}]
        }]}},"errors":null}
        """.Replace("DAY", Today(), StringComparison.Ordinal);

    private static string RumHttpRows() => """
        {"data":{"viewer":{"accounts":[{
          "daily":[{"dimensions":{"date":"DAY"},"sum":{"visits":13},"count":14}],
          "referrers":[{"dimensions":{"date":"DAY","refererHost":"ref.example"},"sum":{"visits":15}}],
          "pages":[{"dimensions":{"date":"DAY","requestPath":"/fresh"},"count":16}],
          "countries":[{"dimensions":{"date":"DAY","countryName":"FI"},"sum":{"visits":17}}]
        }]}},"errors":null}
        """.Replace("DAY", Today(), StringComparison.Ordinal);

    private static string Snapshot(SqliteDatabase database) => database.ReadAsync(connection =>
    {
        var rows = new List<string>();
        foreach (var table in CloudflareReviewRegressionTestsInputs.Vector3)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY 1, 2";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(table + ":" + JsonSerializer.Serialize(values));
            }
        }
        return string.Join("\n", rows);
    }).GetAwaiter().GetResult();

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("A malformed provider response must fail before persistence");
    }

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body) };

    private sealed partial class HttpFixture : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _respond;
        public int Requests { get; private set; }
        public HttpFixture(Func<HttpRequestMessage, string, HttpResponseMessage> respond) => _respond = respond;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Require(request.RequestUri!.Host == "api.cloudflare.com" && request.Headers.Authorization is { Scheme: "Bearer", Parameter: "synthetic-token" },
                "The actual client request must retain its endpoint and normalized Bearer authorization");
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond(request, body);
        }
    }

    private static CloudflareTrafficResult ParseZone(string json)
        => (CloudflareTrafficResult)typeof(CloudflareGraphqlClient)
            .GetMethod("ParseTrafficResponse", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { json, "2026-10-07", "2026-10-08" })!;

    private static WebAnalyticsRollup ParseRum(string json)
        => (WebAnalyticsRollup)typeof(CloudflareRumClient)
            .GetMethod("ParseRollupResponse", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { json, "2026-10-07" })!;

    private static void Reject(Action parse)
    {
        try { parse(); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return; }
        throw new InvalidOperationException("An incomplete or undated provider response must be rejected before persistence");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class CloudflareReviewRegressionTestsInputs
{
    internal static readonly int[] Vector1 = new[] { 0, 103, 200, 299, 300, 399, 400, 499, 500, 599, 600 };
    internal static readonly string[] Vector2 = new[] { "first-tag", "second-tag" };
    internal static readonly string[] Vector3 = new[] { "connections", "cloudflare_traffic", "cloudflare_status_codes", "cloudflare_countries", "cloudflare_pages",
            "web_analytics_sites", "web_analytics_daily", "web_analytics_referrers", "web_analytics_pages", "web_analytics_countries" };
}
