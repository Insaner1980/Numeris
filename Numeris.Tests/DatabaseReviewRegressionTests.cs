using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;

internal static class DatabaseReviewRegressionTests
{
    public static void SearchReplacementRollsBackBothDeviceTables()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("""
            INSERT INTO search_console (site_url, date, kind, query, clicks, fetched_at) VALUES ('test.example', '2026-10-01', 'device', '', 9, 'old');
            INSERT INTO search_devices (site_url, date, device, clicks) VALUES ('test.example', '2026-10-01', 'MOBILE', 9);
            CREATE TRIGGER reject_device BEFORE INSERT ON search_devices WHEN NEW.device = 'FAIL' BEGIN SELECT RAISE(ABORT, 'synthetic device failure'); END;
            """)).GetAwaiter().GetResult();
        try
        {
            new SearchConsoleRepository(database).ReplaceSearchAnalyticsRowsAsync("test.example", SearchQueryKind.Device,
                new[] { new SearchConsoleApiRow { Date = "2026-10-01", Device = "MOBILE", Clicks = 2 }, new SearchConsoleApiRow { Date = "2026-10-01", Device = "FAIL", Clicks = 3 } },
                "2026-10-01", "2026-10-01", "new").GetAwaiter().GetResult();
            throw new InvalidOperationException("A failing replacement must throw");
        }
        catch (SqliteException) { }
        Require(database.ReadAsync(c => c.ExecuteScalar<long>("SELECT SUM(clicks) FROM search_console")).Result == 9
            && database.ReadAsync(c => c.ExecuteScalar<long>("SELECT SUM(clicks) FROM search_devices")).Result == 9,
            "A failed replacement must restore both old device datasets");
    }

    public static void SearchPageQueriesRollBackReplacement()
    {
        using var database = CreateDatabase();
        database.WriteAsync(c => c.Execute("""
            INSERT INTO search_page_queries (site_url, period_start, period_end, page, query, clicks) VALUES ('test.example', '2026-10-01', '2026-10-07', 'old', 'old', 9);
            CREATE TRIGGER reject_pair BEFORE INSERT ON search_page_queries WHEN NEW.query = 'FAIL' BEGIN SELECT RAISE(ABORT, 'synthetic pair failure'); END;
            """)).GetAwaiter().GetResult();
        try
        {
            new SearchConsoleRepository(database).ReplacePageQueryRowsAsync("test.example", "2026-10-01", "2026-10-07",
                new[] { new SearchConsoleApiRow { Page = "first", Query = "first", Clicks = 2 }, new SearchConsoleApiRow { Page = "second", Query = "FAIL", Clicks = 3 } }).GetAwaiter().GetResult();
            throw new InvalidOperationException("A failing replacement must throw");
        }
        catch (SqliteException) { }
        Require(database.ReadAsync(c => c.ExecuteScalar<long>("SELECT SUM(clicks) FROM search_page_queries")).Result == 9, "Failed pair replacement must preserve old rows");
    }

    public static void FailedUpgradeResumesAtCompletedVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Migrate(connection);
        connection.Execute("""
            INSERT INTO connections (id, source, status) VALUES ('youtube', 'youtube', 'configured');
            PRAGMA user_version=4; UPDATE meta SET value='4' WHERE key='schema_version';
            CREATE TRIGGER reject_retirement BEFORE DELETE ON connections WHEN OLD.id='youtube' BEGIN SELECT RAISE(ABORT, 'synthetic migration failure'); END;
            """);
        try { Migrate(connection); throw new InvalidOperationException("Migration failure must propagate"); }
        catch (SqliteException) { }
        Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 5 && connection.ExecuteScalar<string>("SELECT value FROM meta WHERE key='schema_version'") == "5", "Failed v6 must leave both markers at completed v5");
        connection.Execute("DROP TRIGGER reject_retirement");
        Migrate(connection);
        Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM connections WHERE id='youtube'") == 0, "Retry must execute unfinished retirement");
        Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 11, "Successful retry must finish current schema");
    }

    public static void RejectsFutureAndMalformedVersionsWithoutMutation()
    {
        foreach (var marker in new string?[] { "12", "invalid", "-1", "999999999999999", null })
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            connection.Execute("CREATE TABLE meta (key TEXT PRIMARY KEY,value TEXT); INSERT INTO meta VALUES ('schema_version',@marker); CREATE TABLE sentinel (value INTEGER); INSERT INTO sentinel VALUES (7);", new { marker });
            try { Migrate(connection); throw new Exception("Unsupported marker must fail closed"); }
            catch (InvalidOperationException) { }
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table'") == 2
                && connection.ExecuteScalar<int>("SELECT value FROM sentinel") == 7
                && connection.ExecuteScalar<string>("SELECT value FROM meta WHERE key='schema_version'") == marker,
                "Refusal must preserve data, schema and version marker");
        }
    }

    public static void RejectsFuturePragmaWithoutCreatingSchema()
    {
        foreach (var version in new[] { 12, -1 })
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            connection.Execute($"CREATE TABLE sentinel (value INTEGER); INSERT INTO sentinel VALUES (7); PRAGMA user_version={version};");
            try { Migrate(connection); throw new Exception("Unsupported pragma must fail closed"); }
            catch (InvalidOperationException) { }
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table'") == 1
                && connection.ExecuteScalar<int>("SELECT value FROM sentinel") == 7
                && connection.ExecuteScalar<int>("PRAGMA user_version") == version, "Refusal must leave a future/invalid pragma database untouched");
        }
    }

    public static void FailedClearPreservesCompleteData()
    {
        using var database = CreateDatabase();
        database.WriteAsync(c => c.Execute("""
            INSERT INTO cloudflare_traffic (domain,date,unique_visitors,fetched_at) VALUES ('test.example','2026-10-01',5,'old');
            INSERT INTO search_console (site_url,date,kind,query,clicks,fetched_at) VALUES ('test.example','2026-10-01','daily','',9,'old');
            UPDATE connections SET status='connected' WHERE id='sc';
            CREATE TRIGGER reject_clear BEFORE DELETE ON search_console BEGIN SELECT RAISE(ABORT,'synthetic clear failure'); END;
            """)).GetAwaiter().GetResult();
        try { database.ClearAllData(); throw new InvalidOperationException("A failed clear must throw"); }
        catch (SqliteException) { }
        Require(database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM cloudflare_traffic")).Result == 1
            && database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM search_console")).Result == 1
            && database.ReadAsync(c => c.ExecuteScalar<string>("SELECT status FROM connections WHERE id='sc'")).Result == "connected",
            "A failed clear must preserve all data and connection status");
        database.WriteAsync(c => c.Execute("DROP TRIGGER reject_clear")).GetAwaiter().GetResult();
        database.ClearAllData();
        Require(database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM cloudflare_traffic")).Result == 0
            && database.ReadAsync(c => c.ExecuteScalar<string>("SELECT status FROM connections WHERE id='sc'")).Result == "disconnected",
            "A successful clear must still remove metrics and reset status");
    }

    private static SqliteDatabase CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Migrate(connection);
        var database = (SqliteDatabase)RuntimeHelpers.GetUninitializedObject(typeof(SqliteDatabase));
        typeof(SqliteDatabase).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, connection);
        typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, new SemaphoreSlim(1, 1));
        return database;
    }

    private static void Migrate(SqliteConnection connection)
    {
        try { typeof(SqliteDatabase).Assembly.GetType("Numeris.Services.Database.Migrations")!.GetMethod("RunAll", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new object[] { connection }); }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
