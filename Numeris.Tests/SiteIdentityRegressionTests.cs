using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Helpers;
using Numeris.Services.Secrets;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;

internal static class SiteIdentityRegressionTests
{
    public static void RejectsInvalidDnsLabels()
    {
        foreach (var domain in new[] { "first-.example", "first.-second.example", "first.second-.example", "first..example" })
        {
            try { SiteIdentity.NormalizeDomain(domain); throw new InvalidOperationException("Invalid DNS label was accepted"); }
            catch (ArgumentException) { }
        }
        Require(SiteIdentity.NormalizeDomain("First.Second-Label.Example.") == "first.second-label.example", "Valid internal hyphens and trailing dot must remain supported");
    }

    public static void VaultDomainKeysUseSiteIdentity()
    {
        var normalize = typeof(CredentialVault).GetMethod("NormalizeDomain", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var value in new[] { "First.Example.", "https://First.Example./path", "first.example/" })
        {
            Require((string)normalize.Invoke(null, new object[] { value })! == SiteIdentity.NormalizeDomain(value), "Save, lookup and delete must share the site identity key");
        }
    }

    public static void RejectsSecretTargetsAndPreservesPageKeys()
    {
        foreach (var value in new[] { "https://synthetic:placeholder@test.example/", "https://test.example/?access_token=placeholder", "https://test.example/?api%5Fkey=placeholder" })
        {
            try { SiteIdentity.NormalizeHttpsPageUrl(value); throw new InvalidOperationException("A secret-bearing target was accepted"); }
            catch (ArgumentException) { }
        }
        foreach (var value in new[] { "https://test.example/Case%2FPart?search=a%26b#fragment", "https://test.example/with%20space?q=%C3%A4" })
        {
            var first = SiteIdentity.NormalizeHttpsPageUrl(value);
            Require(first == SiteIdentity.NormalizeHttpsPageUrl(first), "Page keys must remain idempotent");
            Require(!first.Contains('#') && first.Contains('?'), "Fragments must be removed and meaningful queries preserved");
        }
    }

    public static void BingDomainReportsIncludeConfiguredPathSites()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        typeof(SqliteDatabase).Assembly.GetType("Numeris.Services.Database.Migrations")!
            .GetMethod("RunAll", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new object[] { connection });
        var database = (SqliteDatabase)RuntimeHelpers.GetUninitializedObject(typeof(SqliteDatabase));
        typeof(SqliteDatabase).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, connection);
        typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, new SemaphoreSlim(1, 1));
        using (database)
        {
            var repository = new BingRepository(database);
            repository.AddSiteAsync("https://test.example/section").GetAwaiter().GetResult();
            var today = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var site in new[] { "https://test.example/section/", "https://other.example/section/", "https://test.example.evil/" })
            {
                connection.Execute("""
                    INSERT INTO bing_rank_traffic (site_url, date, clicks, impressions, raw_json, fetched_at) VALUES (@site, @today, 3, 10, '{}', @today);
                    INSERT INTO bing_query_stats (site_url, query, date, clicks, impressions, raw_json, fetched_at) VALUES (@site, 'query', @today, 3, 10, '{}', @today);
                    INSERT INTO bing_page_stats (site_url, page_url, date, clicks, impressions, raw_json, fetched_at) VALUES (@site, 'https://test.example/page/', @today, 3, 10, '{}', @today);
                    INSERT INTO bing_raw_items (method, site_url, item_key, raw_json, fetched_at) VALUES ('GetCrawlIssues', @site, 'item', '{}', @today);
                    """, new { site, today });
            }
            Require(repository.GetTrafficDailyAsync("https://test.example/", today, today).Result[0].Clicks == 3, "Domain traffic must include path sites and exclude other hosts");
            Require(repository.GetQueriesAsync("test.example", today, today, "clicks", 10).Result[0].Clicks == 3, "Domain queries must include path sites");
            Require(repository.GetPagesAsync("test.example", today, today, 10).Result[0].Clicks == 3, "Domain pages must include path sites");
            Require(repository.GetRawMethodSummaryAsync("test.example").Result[0].ItemCount == 1, "Domain crawl summaries must exclude other hosts");
            Require(repository.GetCrawlIssueItemsAsync("test.example", 10).Result.Count == 1, "Domain crawl issues must include path sites");
            Require(new SummaryRepository(database).GetBingOverviewAsync("test.example", 7).Result.Clicks == 3, "Overview must use the same domain boundary");
            Require(new InsightMetricsRepository(database).GetInsightMetricsAsync("test.example", 7).Result.BingClicks.Current == 3, "Insights must use the same domain boundary");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
