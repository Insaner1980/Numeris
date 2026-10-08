using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Models;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Numeris.ViewModels;

internal static class HealthRegressionTests
{
    public static void RetainsRapidUptimeSamples()
    {
        using var database = CreateDatabase();
        var repository = new HealthRepository(database);
        for (var index = 0; index < 20; index++)
        {
            repository.RecordUptimeProbeAsync(new UptimeProbeResult
            {
                Domain = "test.example",
                Status = index % 2 == 0 ? "up" : "down",
                ResponseMs = index,
            }).GetAwaiter().GetResult();
        }
        Require(database.ReadAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM uptime_checks WHERE domain='test.example'"))
            .GetAwaiter().GetResult() == 20, "Every rapid probe must remain a distinct sample");
    }

    public static void ExcludesMissingResponseTimesFromAverages()
    {
        using var database = CreateDatabase();
        database.WriteAsync(c => c.Execute("""
            INSERT INTO uptime_checks (domain,checked_at,status,response_ms) VALUES
            ('test.example','2026-10-01T12:00:00','up',1000),
            ('test.example','2026-10-01T13:00:00','down',NULL),
            ('test.example','2026-10-02T12:00:00','down',NULL);
            """)).GetAwaiter().GetResult();
        foreach (var domain in new[] { "test.example", "all" })
        {
            var rows = new HealthRepository(database).GetUptimeDailyAsync(domain, "2026-10-01", "2026-10-02").GetAwaiter().GetResult();
            Require(rows[0].AvgResponseMs == 1000 && (object?)rows[1].AvgResponseMs is null,
                "Unknown timings must neither dilute measured duration nor become a real zero");
            Require(rows[0].Incidents == 1 && rows[1].Incidents == 1, "Missing timing must not remove failure samples");
        }
    }

    public static void RejectsUnsupportedSitemapShapes()
    {
        foreach (var action in new Action[]
        {
            () => SitemapClient.ParseSitemapIndex("<urlset><url><loc>https://test.example/page/</loc></url></urlset>"),
            () => SitemapClient.ParseUrlset("<sitemapindex><sitemap><loc>https://test.example/child.xml</loc></sitemap></sitemapindex>"),
            () => SitemapClient.ParseUrlset("<html><loc>https://test.example/page/</loc></html>"),
        })
        {
            try { action(); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException("Unsupported roots must not publish a false complete membership set");
        }
    }

    public static void ParsesOnlySitemapMembershipLocations()
    {
        foreach (var ns in new[] { "", " xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"" })
        {
            var index = SitemapClient.ParseSitemapIndex($"<sitemapindex{ns}><sitemap><loc> https://test.example/child.xml </loc></sitemap><metadata><loc>https://wrong.example/</loc></metadata></sitemapindex>");
            var pages = SitemapClient.ParseUrlset($"<urlset{ns}><url><loc>https://test.example/page/</loc><metadata><loc>https://wrong.example/</loc></metadata></url></urlset>");
            Require(index.SequenceEqual(HealthRegressionTestsInputs.Vector1) && pages.SequenceEqual(HealthRegressionTestsInputs.Vector2),
                "Only direct sitemap/url loc elements belong to the expected namespace and shape");
            Require(SitemapClient.ParseUrlset($"<urlset{ns}/>").Count == 0, "A valid empty URL set must remain authoritative");
        }
    }

    public static void DiscardsSitemapActionResultsAfterDomainSwitch()
    {
        VerifySitemapActionAfterDomainSwitch(failRead: false);
        VerifySitemapActionAfterDomainSwitch(failRead: true);
    }

    private static void VerifySitemapActionAfterDomainSwitch(bool failRead)
    {
        using var database = CreateDatabase();
        database.WriteAsync(c => c.Execute("INSERT INTO sitemap_urls(domain,url,discovered_at,last_seen_at) VALUES('first.example','https://first.example/','2026-10-01','2026-10-01')"))
            .GetAwaiter().GetResult();
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        var domainField = typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        domainField.SetValue(shell, "first.example");
        using var viewModel = new HealthViewModel(shell, new HealthRepository(database), new SitemapRepository(database), null!, null!);
        var gate = (SemaphoreSlim)typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        gate.Wait();
        System.Threading.Tasks.Task pending;
        try
        {
            pending = viewModel.RefreshSitemapAsync();
            domainField.SetValue(shell, "second.example");
            viewModel.SitemapUrls = new() { new SitemapUrl { Domain = "second.example", Url = "https://second.example/" } };
            viewModel.SitemapSummaryText = "Second domain";
            if (failRead)
            {
                var connection = (SqliteConnection)typeof(SqliteDatabase).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(database)!;
                connection.Execute("DROP TABLE sitemap_urls");
            }
        }
        finally { gate.Release(); }
        pending.GetAwaiter().GetResult();
        Require(viewModel.SitemapUrls.Single().Domain == "second.example" && viewModel.SitemapSummaryText == "Second domain",
            "A completed old-domain sitemap action must not replace the selected domain's list or status");
    }

    public static void RollsBackFailedSitemapRefresh()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("""
            INSERT INTO sitemap_urls (domain, url, discovered_at, last_seen_at)
            VALUES ('test.example', 'https://test.example/old/', '2026-01-01', '2026-01-01');
            CREATE TRIGGER fail_removal BEFORE UPDATE OF removed_at ON sitemap_urls
            WHEN NEW.removed_at IS NOT NULL BEGIN SELECT RAISE(ABORT, 'Test removal failure'); END;
            """)).GetAwaiter().GetResult();
        try
        {
            new HealthRepository(database).RefreshSitemapAsync("test.example", HealthRegressionTestsInputs.Vector3)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected test trigger to reject removal");
        }
        catch (SqliteException)
        {
        }
        var urls = database.ReadAsync(connection => connection.Query<string>("SELECT url FROM sitemap_urls ORDER BY url").ToArray())
            .GetAwaiter().GetResult();
        Require(urls.SequenceEqual(new[] { "https://test.example/old/" }), "Failed refresh must preserve the entire previous sitemap");
    }

    public static void UpdatesSitemapSummaryAfterDomainChange()
    {
        var viewModel = (HealthViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HealthViewModel));
        viewModel.SitemapUrls = new();
        var replace = typeof(HealthViewModel).GetMethod("ReplaceSitemap", BindingFlags.Instance | BindingFlags.NonPublic)!;
        replace.Invoke(viewModel, new object[] { new List<SitemapUrl> { new() { Url = "https://test.example/" } } });
        replace.Invoke(viewModel, new object[] { new List<SitemapUrl>() });
        Require(viewModel.SitemapUrls.Count == 0, "New domain must show its own sitemap rows");
        Require(viewModel.SitemapSummaryText.Contains("No sitemap data", StringComparison.Ordinal), "Summary must not retain the previous domain's URL count");
    }

    public static void PublishesLargeSitemapInOneUpdate()
    {
        var viewModel = (HealthViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HealthViewModel));
        viewModel.SitemapUrls = new();
        var rowChanges = 0;
        var listChanges = 0;
        viewModel.SitemapUrls.CollectionChanged += (_, _) => rowChanges++;
        viewModel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(HealthViewModel.SitemapUrls)) listChanges++; };
        var urls = Enumerable.Range(1, 2000).Select(i => new SitemapUrl { Url = $"https://test.example/{i}/" }).ToList();
        typeof(HealthViewModel).GetMethod("ReplaceSitemap", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, new object[] { urls });
        Require(viewModel.SitemapUrls.Count == 2000 && rowChanges == 0 && listChanges == 1,
            "A large sitemap must replace the visible list once instead of triggering thousands of row updates");
    }

    public static void PreventsConcurrentChecks()
    {
        var viewModel = (HealthViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HealthViewModel));
        viewModel.IsCheckingUptime = true;
        viewModel.CheckUptimeNowAsync().GetAwaiter().GetResult();
        viewModel.RefreshSitemapAsync().GetAwaiter().GetResult();
        Require(viewModel.IsCheckingUptime, "A duplicate action must not clear the running check");
        Require(!viewModel.CanRefresh && !viewModel.CanRefreshSitemap, "Health actions must stay disabled during another check");
    }

    public static void IncludesAllSitesInIndexing()
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection => connection.Execute("""
            INSERT INTO sitemap_urls (domain, url, discovered_at, last_seen_at) VALUES
                ('first.example', 'https://first.example/', '2026-10-01', '2026-10-01'),
                ('second.example', 'https://second.example/', '2026-10-01', '2026-10-01');
            """)).GetAwaiter().GetResult();
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, "all");
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        var repository = new SitemapRepository(database);
        var search = new SearchConsoleRepository(database);
        var sync = new SearchConsoleSyncService(new SearchConsoleClient(), new GoogleOAuthClient(), search, repository, vault, connections);
        using var viewModel = new SearchConsoleViewModel(shell, search, repository, sync);
        viewModel.InspectIndexingAsync().GetAwaiter().GetResult();
        Require(viewModel.SitemapUrls.Count == 2, "All-sites indexing must include each site's stored sitemap");
        Require(viewModel.IndexingDetailText.Contains("all URLs", StringComparison.Ordinal)
            && viewModel.RefreshSeverity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
            "Failed authorization must be visible without dropping the other site's rows");
        typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(shell, "empty.example");
        viewModel.InspectIndexingAsync().GetAwaiter().GetResult();
        Require(viewModel.RefreshSeverity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error
            && viewModel.RefreshStatusMessage.Contains("Inspection failed", StringComparison.Ordinal)
            && !viewModel.IndexingDetailText.StartsWith("Checked 0", StringComparison.Ordinal),
            "A site-level failure must remain visible even without stored sitemap URLs");
    }

    public static void DoesNotCountBingFailuresAsSavedData()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault();
        var sync = new BingWebmasterSyncService(null!, new BingRepository(database), new ConnectionsRepository(database, vault), vault);
        var method = typeof(BingWebmasterSyncService).GetMethod("StoreMethodAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var operation = (System.Threading.Tasks.Task<long>)method.Invoke(sync, new object[]
        {
            "test-key", "GetCrawlStats", "https://test.example/", new Dictionary<string, string?>(), "2026-10-01",
        })!;
        try
        {
            operation.GetAwaiter().GetResult();
            throw new InvalidOperationException("A failed Bing method must not be counted as a successfully saved item");
        }
        catch (NullReferenceException) { }
        Require(database.ReadAsync(connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM bing_raw_items WHERE item_key='error'"))
            .GetAwaiter().GetResult() == 1, "The failure must retain its sanitized diagnostic record");
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

internal static class HealthRegressionTestsInputs
{
    internal static readonly string[] Vector1 = new[] { "https://test.example/child.xml" };
    internal static readonly string[] Vector2 = new[] { "https://test.example/page/" };
    internal static readonly string[] Vector3 = new[] { "https://test.example/new/" };
}
