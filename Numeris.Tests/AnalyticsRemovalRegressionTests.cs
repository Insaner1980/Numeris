using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.ViewModels;

internal static class AnalyticsRemovalRegressionTests
{
    public static void RemovesLegacyDataAndPreservesOtherSources()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Migrate(connection);
        Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'google_analytics_%'") == 0,
            "A new installation must not create Analytics tables");
        Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM connections WHERE id = 'ga4' OR source = 'google_analytics'") == 0,
            "A new installation must not register Analytics");

        foreach (var table in new[] { "daily", "active_users", "pages", "sources", "events", "devices" })
        {
            connection.Execute($"CREATE TABLE google_analytics_{table} (payload TEXT); INSERT INTO google_analytics_{table} VALUES ('legacy');");
        }
        connection.Execute("""
            INSERT INTO connections (id, source, status, config, last_sync) VALUES
                ('ga4', 'google_analytics', 'connected', '{}', '2026-10-01'),
                ('legacy-ga', 'google_analytics', 'configured', '{}', NULL);
            UPDATE connections SET status = 'connected', config = '{"clientId":"search-test"}', last_sync = '2026-10-01' WHERE id = 'sc';
            INSERT INTO cloudflare_traffic (domain, date, pageviews, unique_visitors, fetched_at)
                VALUES ('test.example', '2026-10-01', 8, 4, '2026-10-01');
            INSERT INTO search_console (site_url, date, query, clicks, impressions, fetched_at)
                VALUES ('test.example', '2026-10-01', 'test', 3, 12, '2026-10-01');
            INSERT INTO bing_rank_traffic (site_url, date, clicks, impressions, raw_json, fetched_at)
                VALUES ('https://test.example/', '2026-10-01', 2, 9, '{}', '2026-10-01');
            PRAGMA user_version = 10;
            UPDATE meta SET value = '10' WHERE key = 'schema_version';
            """);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            Migrate(connection);
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'google_analytics_%'") == 0,
                "Migration must drop all retired tables and remain idempotent");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM connections WHERE id = 'ga4' OR source = 'google_analytics'") == 0,
                "Migration must remove retired connection metadata");
            Require(connection.ExecuteScalar<int>("SELECT unique_visitors FROM cloudflare_traffic") == 4
                && connection.ExecuteScalar<int>("SELECT clicks FROM search_console") == 3
                && connection.ExecuteScalar<int>("SELECT clicks FROM bing_rank_traffic") == 2,
                "Migration must preserve data from other sources");
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM connections WHERE id = 'sc' AND status = 'connected' AND config = '{\"clientId\":\"search-test\"}' AND last_sync = '2026-10-01'") == 1,
                "Migration must preserve Search Console credentials metadata and sync status");
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 11
                && connection.ExecuteScalar<string>("SELECT value FROM meta WHERE key = 'schema_version'") == "11"
                && connection.ExecuteScalar<string>("PRAGMA quick_check") == "ok",
                "Both schema versions and database integrity must be valid");
        }

        var database = (SqliteDatabase)RuntimeHelpers.GetUninitializedObject(typeof(SqliteDatabase));
        typeof(SqliteDatabase).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, connection);
        typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, new SemaphoreSlim(1, 1));
        new InsightMetricsRepository(database).GetInsightMetricsAsync("all", 7).GetAwaiter().GetResult();
        database.ClearAllData();
        Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM search_console") == 0,
            "Data reset must still work without Analytics tables");
        database.Dispose();
    }

    public static void RemovesNavigationAndSettings(string root)
    {
        foreach (var relativePath in new[] { "MainWindow.xaml", "MainWindow.xaml.cs", "Views/SourcesPage.xaml", "Views/SourcesPage.xaml.cs", "ViewModels/SourcesViewModel.cs", "App.xaml.cs" })
        {
            // The repository root and fixed test file list contain no user-supplied path components.
            // nosemgrep: csharp.lang.security.filesystem.unsafe-path-combine.unsafe-path-combine
            var text = File.ReadAllText(Path.Combine(root, "Numeris", relativePath));
            Require(!text.Contains("GoogleAnalytics", StringComparison.Ordinal) && !text.Contains("Google Analytics", StringComparison.Ordinal)
                && !text.Contains("Tag=\"analytics\"", StringComparison.Ordinal),
                "Navigation, dependency injection and settings must not expose Analytics");
        }
        var isKnownPage = typeof(ShellViewModel).GetMethod("IsKnownPage", BindingFlags.Static | BindingFlags.NonPublic)!;
        Require(!(bool)isKnownPage.Invoke(null, new object[] { "analytics" })! && (bool)isKnownPage.Invoke(null, new object[] { "dashboard" })!,
            "The retired saved page must use the existing dashboard fallback");
    }

    private static void Migrate(SqliteConnection connection)
        => typeof(SqliteDatabase).Assembly.GetType("Numeris.Services.Database.Migrations")!
            .GetMethod("RunAll", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { connection });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
