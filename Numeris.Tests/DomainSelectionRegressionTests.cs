using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Numeris.ViewModels;
using Numeris.ViewModels.Sources;

internal static class DomainSelectionRegressionTests
{
    public static void IncludesConfiguredDomainsBeforeFirstSync()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        connections.UpsertCloudflareAsync(new CloudflareConnectionConfig
        {
            Domain = "runcheckapp.com",
            ZoneId = "test-zone",
        }, "configured").GetAwaiter().GetResult();
        connections.UpsertCloudflareAsync(new CloudflareConnectionConfig
        {
            Domain = "KNITTOOLSAPP.COM",
            ZoneId = "test-zone",
        }, "connected").GetAwaiter().GetResult();
        database.WriteAsync(connection => connection.Execute(
            "INSERT INTO connections (id, source, status, config) VALUES ('cf', 'cloudflare', 'disconnected', NULL), ('invalid', 'cloudflare', 'configured', 'invalid json')"))
            .GetAwaiter().GetResult();

        var shell = CreateShell(connections);
        CreateSource(shell, connections, database, vault).LoadAsync().GetAwaiter().GetResult();

        Require(shell.AvailableDomains.SequenceEqual(new[] { "all", Domains.KnitTools, Domains.Finnvek, "runcheckapp.com" }),
            "Configured domains must appear before their first sync, without duplicating existing domains");
        Require(shell.SelectedDomain == "all", "Loading domains must preserve the current selection");
    }

    public static void UpdatesDomainsAfterConnectionChanges()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        var shell = CreateShell(connections);
        var source = CreateSource(shell, connections, database, vault);
        source.LoadAsync().GetAwaiter().GetResult();
        Require(!shell.AvailableDomains.Contains("runcheckapp.com"), "An unconfigured domain must not be added");

        connections.UpsertCloudflareAsync(new CloudflareConnectionConfig
        {
            Domain = "runcheckapp.com",
            ZoneId = "test-zone",
        }, "configured").GetAwaiter().GetResult();
        source.LoadAsync().GetAwaiter().GetResult();
        Require(shell.AvailableDomains.Contains("runcheckapp.com"), "Saving a connection must update the domain list");

        SetSelectedDomain(shell, "runcheckapp.com");
        source.LoadAsync().GetAwaiter().GetResult();
        Require(shell.SelectedDomain == "runcheckapp.com", "Reloading domains must preserve a configured selection");
        SetSelectedDomain(shell, "all");
        connections.DeleteCloudflareAsync("runcheckapp.com").GetAwaiter().GetResult();
        source.LoadAsync().GetAwaiter().GetResult();
        Require(!shell.AvailableDomains.Contains("runcheckapp.com"), "Deleting a connection must remove its domain");
        Require(shell.AvailableDomains.Contains(Domains.KnitTools) && shell.AvailableDomains.Contains(Domains.Finnvek),
            "Existing default domains must remain available");
    }

    private static CloudflareSourceViewModel CreateSource(ShellViewModel shell, ConnectionsRepository connections, SqliteDatabase database, CredentialVault vault)
    {
        var client = new CloudflareGraphqlClient();
        var sync = new CloudflareSyncService(client, new CloudflareRepository(database), vault, connections);
        return new CloudflareSourceViewModel(connections, vault, client, sync, shell);
    }

    public static void IncludesSitesWithoutCloudflareConnection()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        new PerformanceRepository(database).AddUrlAsync("https://fonecheck.app/").GetAwaiter().GetResult();
        new BingRepository(database).AddSiteAsync("https://fonecheck.app/").GetAwaiter().GetResult();
        connections.UpsertSearchConsoleAsync(new SearchConsoleConnectionConfig
        {
            ClientId = "test-client",
            Sites = new() { "sc-domain:fonecheck.app" },
        }, "configured").GetAwaiter().GetResult();
        database.WriteAsync(connection => connection.Execute(
            "INSERT INTO performance_urls (url, origin, enabled) VALUES ('https://disabled.example/', 'https://disabled.example', 0)"))
            .GetAwaiter().GetResult();

        var shell = CreateShell(connections);
        shell.RefreshAvailableDomainsAsync().GetAwaiter().GetResult();
        Require(shell.AvailableDomains.SequenceEqual(new[] { "all", Domains.KnitTools, Domains.Finnvek, "fonecheck.app" }),
            "Enabled provider sites must appear once without requiring a Cloudflare connection");
        Require(connections.ListConfiguredDomainsAsync().GetAwaiter().GetResult().SequenceEqual(DomainSelectionRegressionTestsInputs.Vector1),
            "Search Console sync must receive the configured domain without disabled sites or property prefixes");
    }

    public static void IgnoresDisabledOrDeletedBingMetadataTargets()
    {
        using var database = CreateDatabase();
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        connections.UpsertBingAsync(new BingConnectionConfig
        {
            Sites = new() { "https://disabled.example/", "https://deleted.example/" },
        }, "configured").GetAwaiter().GetResult();
        connections.UpsertSearchConsoleAsync(new SearchConsoleConnectionConfig
        {
            ClientId = "test-client",
            Sites = new() { "sc-domain:search.example" },
        }, "configured").GetAwaiter().GetResult();
        database.WriteAsync(connection => connection.Execute(
            "INSERT INTO bing_sites (site_url, enabled) VALUES ('https://disabled.example/', 0), ('https://active.example/', 1)"))
            .GetAwaiter().GetResult();

        Require(connections.ListConfiguredDomainsAsync().GetAwaiter().GetResult()
                .SequenceEqual(DomainSelectionRegressionTestsInputs.Vector2),
            "Only enabled Bing rows and Search Console property metadata must drive domain discovery");
    }

    private static ShellViewModel CreateShell(ConnectionsRepository connections)
    {
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        typeof(ShellViewModel).GetField("_connectionsRepo", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, connections);
        SetSelectedDomain(shell, "all");
        return shell;
    }

    private static void SetSelectedDomain(ShellViewModel shell, string domain)
        => typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, domain);

    private static SqliteDatabase CreateDatabase()
    {
        // Keep regression tests away from the user's database, settings, and credentials.
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.Execute("CREATE TABLE connections (id TEXT PRIMARY KEY, source TEXT, status TEXT, config TEXT, last_sync TEXT)");
        connection.Execute("CREATE TABLE performance_urls (id INTEGER PRIMARY KEY, url TEXT UNIQUE, origin TEXT, source TEXT, enabled INTEGER, created_at TEXT)");
        connection.Execute("CREATE TABLE bing_sites (site_url TEXT UNIQUE, source TEXT, enabled INTEGER, discovered_at TEXT)");
        connection.Execute("CREATE TABLE web_analytics_sites (domain TEXT, site_tag TEXT)");
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

internal static class DomainSelectionRegressionTestsInputs
{
    internal static readonly string[] Vector1 = new[] { "fonecheck.app" };
    internal static readonly string[] Vector2 = new[] { "active.example", "search.example" };
}
