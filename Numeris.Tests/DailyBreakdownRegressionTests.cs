using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;

internal static class DailyBreakdownRegressionTests
{
    public static void ReplacesCloudflareBreakdownRange()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("""
            INSERT INTO cloudflare_countries (domain, date, country, visitors) VALUES ('test.example', '2026-10-01', 'US', 99);
            INSERT INTO cloudflare_pages (domain, date, path, requests) VALUES ('test.example', '2026-10-01', '/old/', 99);
            """)).GetAwaiter().GetResult();
        var result = new CloudflareTrafficResult { BreakdownStartDate = "2026-10-01", BreakdownDate = "2026-10-02" };
        result.Countries.Add(new() { Date = "2026-10-01", Country = "FI", Value = 3 });
        result.Pages.Add(new() { Date = "2026-10-02", Path = "/new/", Value = 4 });
        var repository = new CloudflareRepository(database);
        repository.UpsertTrafficAsync("test.example", result, "2026-10-02").GetAwaiter().GetResult();
        var countries = repository.GetTrafficCountriesAsync("test.example", "2026-10-01", "2026-10-01").GetAwaiter().GetResult();
        var pages = repository.GetTrafficPagesAsync("test.example", "2026-10-01", "2026-10-01").GetAwaiter().GetResult();
        Require(countries.Count == 1 && countries[0].Country == "FI" && countries[0].Value == 3,
            "Refetched dates must replace stale countries and retain their actual day");
        Require(pages.Count == 0, "A removed page must not remain in a refetched day");
    }

    public static void PreservesWebAnalyticsCategoryDates()
    {
        using var database = CreateDatabase();
        var rollup = new WebAnalyticsRollup { StartDate = "2026-10-01" };
        rollup.Daily.Add(new() { Date = "2026-10-01", Visits = 3, PageViews = 4 });
        rollup.Daily.Add(new() { Date = "2026-10-02", Visits = 5, PageViews = 6 });
        rollup.Referrers.Add(new() { Date = "2026-10-01", Key = "example.org", Visits = 3 });
        rollup.Countries.Add(new() { Date = "2026-10-01", Key = "FI", Visits = 3 });
        rollup.Pages.Add(new() { Date = "2026-10-02", Path = "/", PageViews = 6 });
        var repository = new WebAnalyticsRepository(database);
        repository.UpsertRollupAsync("test.example", "2026-10-02", rollup, "2026-10-02").GetAwaiter().GetResult();
        var countries = repository.GetCountriesAsync("test.example", "2026-10-01", "2026-10-01").GetAwaiter().GetResult();
        var referrers = repository.GetReferrersAsync("test.example", "2026-10-01", "2026-10-01").GetAwaiter().GetResult();
        var pages = repository.GetPagesAsync("test.example", "2026-10-01", "2026-10-01").GetAwaiter().GetResult();
        Require(countries.Count == 1 && countries[0].Value == 3, "Countries must use the API group date");
        Require(referrers.Count == 1 && referrers[0].Visits == 3, "Referrers must use the API group date");
        Require(pages.Count == 0, "The next day's pages must not enter the selected day");
    }

    public static void RollsBackFailedWebAnalyticsRollup()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("""
            CREATE TRIGGER fail_category BEFORE INSERT ON web_analytics_countries
            BEGIN SELECT RAISE(ABORT, 'Test category failure'); END;
            """)).GetAwaiter().GetResult();
        var rollup = new WebAnalyticsRollup { StartDate = "2026-10-01" };
        rollup.Daily.Add(new() { Date = "2026-10-01", Visits = 3 });
        rollup.Countries.Add(new() { Date = "2026-10-01", Key = "FI", Visits = 3 });
        try
        {
            new WebAnalyticsRepository(database).UpsertRollupAsync("test.example", "2026-10-01", rollup, "2026-10-01")
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected test trigger to reject the country");
        }
        catch (SqliteException) { }
        Require(database.ReadAsync(connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM web_analytics_daily"))
            .GetAwaiter().GetResult() == 0, "A failed rollup must not leave partially updated daily metrics");
    }

    public static void RollsBackFailedWebAnalyticsDiscovery()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("""
            INSERT INTO web_analytics_sites (domain, site_tag, discovered_at)
                VALUES ('first.example', 'original', 'now');
            CREATE TRIGGER fail_mapping BEFORE INSERT ON web_analytics_sites
            WHEN NEW.domain = 'second.example'
            BEGIN SELECT RAISE(ABORT, 'Test mapping failure'); END;
            """)).GetAwaiter().GetResult();
        try
        {
            new WebAnalyticsRepository(database).SaveSitesAsync(new[]
            {
                new WebAnalyticsSite { Host = "first.example", SiteTag = "changed" },
                new WebAnalyticsSite { Host = "second.example", SiteTag = "new" },
            }, "now").GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected the test trigger to reject the mapping");
        }
        catch (SqliteException) { }
        Require(database.ReadAsync(connection => connection.ExecuteScalar<string>(
            "SELECT site_tag FROM web_analytics_sites WHERE domain = 'first.example'"))
            .GetAwaiter().GetResult() == "original", "Failed discovery must preserve the original mappings");
    }

    public static void MigratesOnlyInvalidBreakdownCaches()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection =>
        {
            connection.Execute("""
                INSERT INTO cloudflare_traffic (domain, date, pageviews, unique_visitors, fetched_at)
                    VALUES ('test.example', '2026-10-01', 4, 3, '2026-10-01');
                INSERT INTO cloudflare_countries (domain, date, country, visitors)
                    VALUES ('test.example', '2026-10-01', 'FI', 30);
                INSERT INTO web_analytics_sites (domain, site_tag, discovered_at)
                    VALUES ('test.example', 'test-tag', '2026-10-01');
                PRAGMA user_version = 8;
                """);
            typeof(SqliteDatabase).Assembly.GetType("Numeris.Services.Database.Migrations")!
                .GetMethod("RunAll", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { connection });
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM cloudflare_countries") == 0,
                "Old period aggregates must not enter daily reports");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM cloudflare_traffic") == 1,
                "Migration must preserve daily traffic");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM web_analytics_sites") == 1,
                "Migration must preserve configured sites");
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 11
                && connection.ExecuteScalar<string>("SELECT value FROM meta WHERE key='schema_version'") == "11",
                "Both schema versions must advance together");
        }).GetAwaiter().GetResult();
    }

    public static void WeightsSearchMetricsByTraffic()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("""
            INSERT INTO search_console (site_url,date,kind,query,clicks,impressions,position,fetched_at) VALUES
              ('first.example','2026-10-01','daily','',1,10,1,'now'),
              ('second.example','2026-10-01','daily','',45,90,10,'now'),
              ('first.example','2026-10-01','query','test',1,10,1,'now'),
              ('first.example','2026-10-02','query','test',45,90,10,'now');
            INSERT INTO search_devices (site_url,date,device,clicks,impressions,ctr,position) VALUES
              ('first.example','2026-10-01','MOBILE',1,10,0.1,1),
              ('second.example','2026-10-01','MOBILE',45,90,0.5,10);
            """)).GetAwaiter().GetResult();
        var repository = new SearchConsoleRepository(database);
        var daily = repository.GetSearchDailyAsync("all", "2026-10-01", "2026-10-02").GetAwaiter().GetResult().Single();
        var query = repository.GetQueriesAsync("first.example", "2026-10-01", "2026-10-02", "clicks", 10).GetAwaiter().GetResult().Single();
        var device = repository.GetDevicesAsync("all", "2026-10-01", "2026-10-02").GetAwaiter().GetResult().Single();
        var newQuery = repository.GetNewQueriesAsync("first.example", "2026-10-01", "2026-10-02", "2026-09-01", "2026-09-02")
            .GetAwaiter().GetResult().Single();
        Require(Math.Abs(daily.AvgPosition - 9.1) < 0.00001 && Math.Abs(query.Position - 9.1) < 0.00001,
            "Search position must weight rows by impressions");
        Require(Math.Abs(device.Ctr - 0.46) < 0.00001 && Math.Abs(device.Position - 9.1) < 0.00001,
            "All-sites device metrics must use combined clicks and impressions");
        Require(Math.Abs(newQuery.Ctr - 0.46) < 0.00001 && Math.Abs(newQuery.Position - 9.1) < 0.00001,
            "New queries must use the same weighted metrics as the main query list");
    }

    public static void WeightsBingPositionsByTraffic()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("""
            INSERT INTO bing_query_stats (site_url,query,date,clicks,impressions,avg_click_position,avg_impression_position,raw_json,fetched_at) VALUES
              ('https://test.example/','test','2026-10-01',1,10,2,1,'{}','now'),
              ('https://test.example/','test','2026-10-02',9,90,4,10,'{}','now');
            """)).GetAwaiter().GetResult();
        var repository = new BingRepository(database);
        foreach (var site in new[] { "all", "https://test.example/" })
        {
            var query = repository.GetQueriesAsync(site, "2026-10-01", "2026-10-02", "clicks", 10).GetAwaiter().GetResult().Single();
            Require(Math.Abs(query.AvgClickPosition - 3.8) < 0.00001 && Math.Abs(query.AvgImpressionPosition - 9.1) < 0.00001,
                "Bing positions must weight rows by their corresponding clicks or impressions");
        }
    }

    public static void ReadsZeroAndNonzeroSearchMetricsTogether()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("""
            INSERT INTO search_console (site_url,date,kind,query,clicks,impressions,position,fetched_at) VALUES
                ('test.example','2026-10-01','daily','',0,0,0,'now'),
                ('test.example','2026-10-02','daily','',1,4,15.5,'now'),
                ('test.example','2026-10-01','query','empty',0,0,0,'now'),
                ('test.example','2026-10-02','query','active',1,4,15.5,'now');
            INSERT INTO search_devices (site_url,date,device,clicks,impressions,ctr,position) VALUES
                ('test.example','2026-10-01','MOBILE',0,0,0,0),
                ('test.example','2026-10-02','MOBILE',1,4,0.25,15.5);
            """)).GetAwaiter().GetResult();
        var repository = new SearchConsoleRepository(database);
        foreach (var site in new[] { "all", "test.example" })
        {
            var daily = repository.GetSearchDailyAsync(site, "2026-10-01", "2026-10-02").GetAwaiter().GetResult();
            var queries = repository.GetQueriesAsync(site, "2026-10-01", "2026-10-02", "position", 10).GetAwaiter().GetResult();
            var devices = repository.GetDevicesAsync(site, "2026-10-01", "2026-10-02").GetAwaiter().GetResult();
            Require(daily.Count == 2 && daily[0].AvgPosition == 0 && daily[1].AvgPosition == 15.5,
                "Daily rows must retain zero and fractional positions without a mapping error");
            Require(queries.Count == 2 && queries[0].Position == 0 && queries[1].Position == 15.5 && queries[1].Ctr == 0.25,
                "Query rows must retain both zero and fractional metrics");
            Require(devices.Count == 2 && devices[0].Position == 0 && devices[1].Position == 15.5 && devices[1].Ctr == 0.25,
                "Device rows must retain both zero and fractional metrics");
        }
        var newQueries = repository.GetNewQueriesAsync("test.example", "2026-10-01", "2026-10-02", "2026-09-01", "2026-09-02").GetAwaiter().GetResult();
        Require(newQueries.Count == 2 && newQueries.Any(row => row.Position == 15.5 && row.Ctr == 0.25),
            "New-query rows must retain fractional metrics when empty rows are also present");
    }

    private static SqliteDatabase CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        typeof(SqliteDatabase).Assembly.GetType("Numeris.Services.Database.Migrations")!
            .GetMethod("RunAll", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { connection });
        var database = (SqliteDatabase)RuntimeHelpers.GetUninitializedObject(typeof(SqliteDatabase));
        typeof(SqliteDatabase).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, connection);
        typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, new SemaphoreSlim(1, 1));
        return database;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
