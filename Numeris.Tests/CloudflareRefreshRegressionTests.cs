using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Numeris.ViewModels;

internal static class CloudflareRefreshRegressionTests
{
    public static void DiscoversStandaloneWebAnalyticsSites()
    {
        const string json = """
            {"success":true,"result":[
              {"site_tag":"root-site-tag","host":"Runcheckapp.com"},
              {"site_tag":"zone-site-tag","ruleset":{"zone_name":"knittoolsapp.com"}},
              {"site_tag":"rule-site-tag","rules":[{"host":"finnvek.com"}]}
            ]}
            """;
        var type = typeof(CloudflareRumClient);
        var responseType = type.GetNestedType("SiteInfoListResponse", BindingFlags.NonPublic)!;
        var options = (JsonSerializerOptions)type.GetField("JsonOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var response = JsonSerializer.Deserialize(json, responseType, options)!;
        var parser = type.GetMethod("ParseSites", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Web Analytics discovery must parse all supported hostname fields");
        var sites = (List<WebAnalyticsSite>)parser.Invoke(null, new[] { response })!;
        Require(sites.Count == 3, "Discovery must include standalone sites alongside zone and rule based sites");
        Require(sites.Single(site => site.Host == "runcheckapp.com").SiteTag == "root-site-tag", "Standalone host must retain its current API site tag");
    }

    public static void ReportsMissingConnection(bool analytics)
    {
        using var database = CreateDatabase();
        using var report = CreateReport(database, analytics);
        report.RefreshAsync().GetAwaiter().GetResult();
        Require(Read<string>(report, "RefreshStatusMessage").Contains("Sources"), "Missing Zone Analytics connection must direct the user to Sources");
        Require(Read<object>(report, "RefreshSeverity").ToString() == "Warning", "Missing connection must not claim success");
    }

    public static void ReportsSyncFailure(bool analytics)
    {
        using var database = CreateDatabase();
        database.WriteAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE connections";
            command.ExecuteNonQuery();
        }).GetAwaiter().GetResult();
        using var report = CreateReport(database, analytics);
        report.RefreshAsync().GetAwaiter().GetResult();
        Require(Read<string>(report, "RefreshStatusMessage").Contains("failed", StringComparison.OrdinalIgnoreCase), "A failed Cloudflare sync must be visible");
        Require(Read<object>(report, "RefreshSeverity").ToString() == "Error", "Failed sync must have error severity");
        Require(report.CanRefresh, "Refresh must be available after failure");
    }

    public static void PreventsConcurrentRefresh(bool analytics)
    {
        using var database = CreateDatabase();
        using var report = CreateReport(database, analytics);
        var gate = (SemaphoreSlim)typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var enabledNotifications = 0;
        report.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(report.CanRefresh) && report.CanRefresh) enabledNotifications++;
        };
        gate.Wait();
        Task pending;
        try
        {
            pending = report.RefreshAsync();
            Require(!report.CanRefresh && !pending.IsCompleted, "Refresh must remain disabled while syncing");
            Require(report.RefreshAsync().IsCompleted, "Duplicate refresh must not queue another sync");
        }
        finally { gate.Release(); }
        pending.GetAwaiter().GetResult();
        Require(report.CanRefresh && enabledNotifications == 1, "Refresh must become available once, after the operation");
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

    private static CloudflareViewModel CreateReport(SqliteDatabase database, bool webAnalytics)
    {
        // These tests never open the user's database or credential vault.
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, "dbcheck.app");
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        var cloudflare = new CloudflareRepository(database);
        var analytics = new WebAnalyticsRepository(database);
        var report = new CloudflareViewModel(shell, cloudflare, analytics,
            new CloudflareSyncService(new CloudflareGraphqlClient(), cloudflare, vault, connections),
            new WebAnalyticsSyncService(new CloudflareRumClient(), analytics, vault, connections));
        Require(report.GetType().GetProperty("RefreshStatusMessage") is not null, "Cloudflare refresh must expose a visible status message");
        report.ActiveTab = webAnalytics ? "webanalytics" : "traffic";
        return report;
    }

    private static T Read<T>(object target, string property)
        => (T)(target.GetType().GetProperty(property)?.GetValue(target) ?? throw new InvalidOperationException("Missing report property: " + property));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
