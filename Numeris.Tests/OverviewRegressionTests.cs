using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Insights;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Numeris.ViewModels;

internal static class OverviewRegressionTests
{
    public static void HidesComparisonsForComplementaryDomainCoverage()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection =>
        {
            for (var offset = 0; offset < 14; offset++)
            {
                var date = DateTime.Today.AddDays(-offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                foreach (var domain in OverviewRegressionTestsInputs.Vector1)
                {
                    if (offset < 7 && (offset % 2 == 0) != (domain == "first.example")) continue;
                    connection.Execute("INSERT INTO cloudflare_traffic(domain,date,unique_visitors,fetched_at) VALUES(@domain,@date,10,'now')", new { domain, date });
                    connection.Execute("INSERT INTO search_console(site_url,date,kind,clicks,query,fetched_at) VALUES(@domain,@date,'daily',10,'','now')", new { domain, date });
                    connection.Execute("INSERT INTO bing_rank_traffic(site_url,date,clicks,raw_json,fetched_at) VALUES(@site,@date,10,'{}','now')", new { site = "https://" + domain + "/", date });
                }
            }
        }).GetAwaiter().GetResult();
        var repository = new SummaryRepository(database);
        var summary = repository.GetSummaryAsync("all", 7).GetAwaiter().GetResult();
        var bing = repository.GetBingOverviewAsync("all", 7).GetAwaiter().GetResult();
        Require(summary.VisitorsDays == 7 && summary.ClicksDays == 7 && bing.Days == 7,
            "Aggregate date metadata must retain its union-of-dates meaning");
        Require(summary.VisitorsChangePct is null && summary.ClicksChangePct is null && bing.ClicksChangePct is null,
            "Complementary missing dates must not certify a comparable multi-site period");
        database.WriteAsync(connection =>
        {
            for (var offset = 0; offset < 7; offset++)
            {
                var date = DateTime.Today.AddDays(-offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var domain = offset % 2 == 0 ? "second.example" : "first.example";
                connection.Execute("INSERT INTO cloudflare_traffic(domain,date,unique_visitors,fetched_at) VALUES(@domain,@date,10,'now')", new { domain, date });
                connection.Execute("INSERT INTO search_console(site_url,date,kind,clicks,query,fetched_at) VALUES(@domain,@date,'daily',10,'','now')", new { domain, date });
                connection.Execute("INSERT INTO bing_rank_traffic(site_url,date,clicks,raw_json,fetched_at) VALUES(@site,@date,10,'{}','now')", new { site = "https://" + domain + "/", date });
            }
        }).GetAwaiter().GetResult();
        summary = repository.GetSummaryAsync("all", 7).GetAwaiter().GetResult();
        bing = repository.GetBingOverviewAsync("all", 7).GetAwaiter().GetResult();
        Require(summary.VisitorsChangePct == 0 && summary.ClicksChangePct == 0 && bing.ClicksChangePct == 0,
            "Complete matching site periods must still receive measured comparisons");
    }

    public static void UsesExactlySelectedDays()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection =>
        {
            for (var offset = 0; offset < 14; offset++)
            {
                var date = DateTime.Today.AddDays(-offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var clicks = offset < 7 ? 10 : 5;
                connection.Execute("INSERT INTO search_console (site_url,date,kind,clicks,query,fetched_at) VALUES ('knittoolsapp.com',@date,'daily',@clicks,'','2026-10-07')", new { date, clicks });
                connection.Execute("INSERT INTO cloudflare_traffic (domain,date,unique_visitors,fetched_at) VALUES ('knittoolsapp.com',@date,@clicks,'2026-10-07')", new { date, clicks });
                connection.Execute("INSERT INTO bing_rank_traffic (site_url,date,clicks,raw_json,fetched_at) VALUES ('https://knittoolsapp.com/',@date,@clicks,'{}','2026-10-07')", new { date, clicks });
            }
        }).GetAwaiter().GetResult();
        var repository = new SummaryRepository(database);
        var summary = repository.GetSummaryAsync("knittoolsapp.com", 7).GetAwaiter().GetResult();
        Require(summary.ClicksTotal == 70 && summary.VisitorsTotal == 70, "Seven days must exclude the eighth day's values");
        Require(summary.ClicksChangePct == 100 && summary.VisitorsChangePct == 100, "Comparison windows must both contain seven days");
        var bing = repository.GetBingOverviewAsync("knittoolsapp.com", 7).GetAwaiter().GetResult();
        Require(bing.Clicks == 70 && bing.PreviousClicks == 35, "Bing must use the same seven-day boundaries");
    }

    public static void DoesNotInventMissingTrafficOrChanges()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection =>
        {
            var date = DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            connection.Execute("INSERT INTO cloudflare_traffic (domain,date,unique_visitors,fetched_at) VALUES ('knittoolsapp.com',@date,100,'2026-10-07')", new { date });
        }).GetAwaiter().GetResult();
        var repository = new SummaryRepository(database);
        var summary = repository.GetSummaryAsync("knittoolsapp.com", 7).GetAwaiter().GetResult();
        Require(Read<int>(summary, "VisitorsDays") == 0, "Missing traffic must be distinguishable from measured zero");
        Require((object?)summary.VisitorsChangePct is null, "Missing current data must not imply a 100% loss");
        Require((object?)summary.ClicksChangePct is null, "Missing search data must not imply a measured 0% change");
        var bing = repository.GetBingOverviewAsync("knittoolsapp.com", 7).GetAwaiter().GetResult();
        Require(Read<int>(bing, "Days") == 0 && (object?)bing.ClicksChangePct is null, "Missing Bing data must stay missing");
        database.WriteAsync(connection =>
        {
            for (var offset = 0; offset < 14; offset++)
            {
                var date = DateTime.Today.AddDays(-offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                connection.Execute("INSERT OR REPLACE INTO cloudflare_traffic (domain,date,unique_visitors,fetched_at) VALUES ('knittoolsapp.com',@date,@value,'2026-10-07')",
                    new { date, value = offset < 7 ? 0 : 10 });
            }
        }).GetAwaiter().GetResult();
        summary = repository.GetSummaryAsync("knittoolsapp.com", 7).GetAwaiter().GetResult();
        Require(Read<int>(summary, "VisitorsDays") == 7 && summary.VisitorsTotal == 0 && summary.VisitorsChangePct == -100,
            "A measured zero for a complete period must remain a real zero and a valid decline");
    }

    public static void HidesChangesForPartialAndNewData()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection =>
        {
            for (var offset = 0; offset < 14; offset++)
            {
                if (offset is 0 or 1 or 2) continue;
                var date = DateTime.Today.AddDays(-offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                connection.Execute("INSERT INTO search_console (site_url,date,kind,clicks,query,fetched_at) VALUES ('knittoolsapp.com',@date,'daily',10,'','2026-10-07')", new { date });
            }
        }).GetAwaiter().GetResult();
        var summary = new SummaryRepository(database).GetSummaryAsync("knittoolsapp.com", 7).GetAwaiter().GetResult();
        Require(summary.ClicksTotal == 40 && (object?)summary.ClicksChangePct is null, "Partial Google data must not be compared with a full previous period");
        database.WriteAsync(connection => connection.Execute("DELETE FROM search_console WHERE date < @date", new { date = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd") })).GetAwaiter().GetResult();
        summary = new SummaryRepository(database).GetSummaryAsync("knittoolsapp.com", 7).GetAwaiter().GetResult();
        Require((object?)summary.ClicksChangePct is null, "A missing previous period must not become an invented +100%");
    }

    public static void ConvertsBingDates()
    {
        var method = typeof(BingWebmasterClient).GetMethod("NormalizeDate") ?? throw new InvalidOperationException("Bing date normalization is missing");
        foreach (var input in new[] { "/Date(1791072000000)/", "/Date(1791072000000+0300)/", "2026-10-04", "2026-10-04T00:00:00Z" })
        {
            Require((string)method.Invoke(null, new object[] { input })! == "2026-10-04", "Bing dates must normalize to UTC calendar days");
        }
        foreach (var input in new[] { "/Date(invalid)/", $"/Date({new string('9', 100_000)})/" })
        {
            try { method.Invoke(null, new object[] { input }); }
            catch (TargetInvocationException ex) when (ex.InnerException is FormatException) { continue; }
            throw new InvalidOperationException("Malformed Bing dates must be rejected");
        }
    }

    public static void RepairsStoredBingDatesWithoutDuplicates()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection =>
        {
            connection.Execute("""
                INSERT INTO bing_rank_traffic VALUES ('https://knittoolsapp.com/','/Date(1791072000000)/',5,100,'{}','2026-10-06');
                INSERT INTO bing_rank_traffic VALUES ('https://knittoolsapp.com/','2026-10-04',8,100,'{}','2026-10-07');
                INSERT INTO bing_query_stats VALUES ('https://knittoolsapp.com/','knitting','/Date(1791072000000+0300)/',2,10,1,2,'{}','2026-10-06');
                INSERT INTO bing_page_stats VALUES ('https://knittoolsapp.com/','https://knittoolsapp.com/','/Date(1791072000000)/',2,10,'{}','2026-10-06');
                PRAGMA user_version = 7;
                UPDATE meta SET value='7' WHERE key='schema_version';
                """);
            RunMigrations(connection);
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM bing_rank_traffic") == 1, "Date repair must not duplicate rank rows");
            Require(connection.ExecuteScalar<int>("SELECT clicks FROM bing_rank_traffic WHERE date='2026-10-04'") == 8, "Date repair must preserve the fresher result");
            Require(connection.ExecuteScalar<string>("SELECT date FROM bing_query_stats") == "2026-10-04", "Query history dates must be repaired");
            Require(connection.ExecuteScalar<string>("SELECT date FROM bing_page_stats") == "2026-10-04", "Page history dates must be repaired");
            RunMigrations(connection);
            Require(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM bing_rank_traffic") == 1, "Migration must be idempotent");
            Require(connection.ExecuteScalar<int>("PRAGMA user_version") == 11, "Schema version must advance after date repair");
        }).GetAwaiter().GetResult();
    }

    public static void LimitsOnlyCloudflareBreakdowns()
    {
        var method = typeof(CloudflareGraphqlClient).GetMethod("AdaptiveStartDate", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Cloudflare breakdown window is not bounded");
        Require((string)method.Invoke(null, new object[] { "2025-10-08", "2026-10-07" })! == "2026-09-08", "Breakdowns must respect Cloudflare's 30-day request window");
        Require((string)method.Invoke(null, new object[] { "2026-10-01", "2026-10-07" })! == "2026-10-01", "Short periods must not be widened");
    }

    public static void RefreshesConfiguredSources(bool fail, bool concurrent)
    {
        using var database = CreateDatabase();
        if (fail) database.WriteAsync(c => c.Execute("DROP TABLE connections")).GetAwaiter().GetResult();
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, "knittoolsapp.com");
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        var cf = new CloudflareRepository(database);
        var sc = new SearchConsoleRepository(database);
        var bing = new BingRepository(database);
        var sitemap = new SitemapRepository(database);
        var performance = new PerformanceRepository(database);
        var viewModel = new DashboardViewModel(shell, new SummaryRepository(database), cf, sc, bing, sitemap, new InsightMetricsRepository(database),
            new CloudflareSyncService(new CloudflareGraphqlClient(), cf, vault, connections),
            new SearchConsoleSyncService(new SearchConsoleClient(), new GoogleOAuthClient(), sc, sitemap, vault, connections),
            new BingWebmasterSyncService(new BingWebmasterClient(), bing, connections, vault),
            new PerformanceSyncService(new CruxClient(), new PageSpeedClient(), performance, connections, vault));
        typeof(DashboardViewModel).GetProperty("IsLoading")!.SetValue(viewModel, false);
        var refresh = typeof(DashboardViewModel).GetMethod("RefreshAsync") ?? throw new InvalidOperationException("Overview refresh is missing");
        var gate = (SemaphoreSlim)typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        if (concurrent) gate.Wait();
        Task pending;
        try
        {
            pending = (Task)refresh.Invoke(viewModel, null)!;
            if (concurrent)
            {
                Require(!Read<bool>(viewModel, "CanRefresh") && !pending.IsCompleted, "Overview must remain busy while syncing");
                Require(((Task)refresh.Invoke(viewModel, null)!).IsCompleted, "Duplicate Overview refresh must not queue");
            }
        }
        finally { if (concurrent) gate.Release(); }
        pending.GetAwaiter().GetResult();
        var status = Read<string>(viewModel, "RefreshStatusMessage");
        Require(fail ? status.Contains("Cloudflare") && status.Contains("Google") && status.Contains("Bing") && status.Contains("Performance") : status.Contains("Sources"), "Every source must be checked and missing/failed sync must be visible");
        Require(Read<object>(viewModel, "RefreshSeverity").ToString() == (fail ? "Error" : "Warning"), "Refresh must not falsely report success");
        Require(Read<bool>(viewModel, "CanRefresh"), "Overview refresh must become available again");
    }

    private static SqliteDatabase CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        RunMigrations(connection);
        var database = (SqliteDatabase)RuntimeHelpers.GetUninitializedObject(typeof(SqliteDatabase));
        typeof(SqliteDatabase).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, connection);
        typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, new SemaphoreSlim(1, 1));
        return database;
    }

    private static void RunMigrations(SqliteConnection connection)
        => typeof(SqliteDatabase).Assembly.GetType("Numeris.Services.Database.Migrations")!
            .GetMethod("RunAll", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { connection });

    private static T Read<T>(object target, string property)
        => (T)(target.GetType().GetProperty(property)?.GetValue(target) ?? throw new InvalidOperationException("Missing property: " + property));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class OverviewRegressionTestsInputs
{
    internal static readonly string[] Vector1 = new[] { "first.example", "second.example" };
}
