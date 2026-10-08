using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Settings;
using Numeris.Services.Sync;
using Numeris.ViewModels;
using Numeris.ViewModels.Sources;

internal static partial class SourcesPersistenceRegressionTests
{
    public static void CloudflareSavesReplacesAndDeletes()
    {
        using var fixture = new Fixture();
        var source = fixture.Cloudflare();
        source.NewDomain = " HTTPS://SAVE-TEST.INVALID/ ";
        source.NewZoneId = "test-zone";
        source.NewToken = " Bearer qa-first-token ";
        source.SaveAsync().GetAwaiter().GetResult();
        Require(source.StatusMessage.StartsWith("Saved", StringComparison.Ordinal) && source.NewToken == "", "Save must publish success and clear the token input");
        Require(fixture.Vault.GetCloudflareToken("save-test.invalid") == "qa-first-token", "Cloudflare must normalize and persist the token in the vault");
        source.NewToken = "qa-replacement-token";
        source.SaveAsync().GetAwaiter().GetResult();
        source = fixture.Cloudflare();
        source.LoadAsync().GetAwaiter().GetResult();
        Require(source.Connections.Count == 1 && source.Connections[0].HasToken && fixture.Shell.AvailableDomains.Contains("save-test.invalid"), "A new source view model must reload the saved connection and domain");
        Require(fixture.Vault.GetCloudflareToken("save-test.invalid") == "qa-replacement-token", "Saving again must replace the token");
        fixture.NoPlainTextSecrets();
        source.DeleteAsync(source.Connections.Single()).GetAwaiter().GetResult();
        Require(source.Connections.Count == 0 && fixture.Vault.GetCloudflareToken("save-test.invalid") is null && !fixture.Shell.AvailableDomains.Contains("save-test.invalid"), "Delete must remove the connection, credential and domain option");
    }

    public static void WebAnalyticsSavesMappingsAndDeletes()
    {
        using var fixture = new Fixture();
        var source = fixture.WebAnalytics();
        source.NewAccountId = "qa-test-account";
        source.NewToken = "qa-first-token";
        source.SaveAsync().GetAwaiter().GetResult();
        source.NewToken = "qa-replacement-token";
        source.SaveAsync().GetAwaiter().GetResult();
        source.NewDomain = "https://save-test.invalid/";
        source.NewSiteTag = "qa-first-tag";
        source.AddSiteAsync().GetAwaiter().GetResult();
        source.NewDomain = "SAVE-TEST.INVALID";
        source.NewSiteTag = "qa-replacement-tag";
        source.AddSiteAsync().GetAwaiter().GetResult();
        source = fixture.WebAnalytics();
        source.LoadAsync().GetAwaiter().GetResult();
        Require(source.Connection is { HasToken: true, AccountId: "qa-test-account" } && source.Sites.Count == 1 && source.Sites[0].SiteTag == "qa-replacement-tag", "Save and repeated Add must reload one updated mapping");
        Require(fixture.Vault.GetWebAnalyticsToken("qa-test-account") == "qa-replacement-token", "Web Analytics must replace the saved token");
        fixture.NoPlainTextSecrets();
        source.DeleteSiteAsync(source.Sites.Single()).GetAwaiter().GetResult();
        Require(source.Sites.Count == 0, "Delete mapping must remove the saved site");
        source.DeleteAsync().GetAwaiter().GetResult();
        Require(source.Connection is { Status: "disconnected", HasToken: false } && fixture.Vault.GetWebAnalyticsToken("qa-test-account") is null, "Delete connection must remove its credential and disconnect it");
    }

    public static void SearchConsoleSavesReplacesAndDeletes()
    {
        using var fixture = new Fixture();
        var source = fixture.SearchConsole();
        source.NewClientId = "qa-test-client";
        source.NewClientSecret = "qa-first-secret";
        source.SaveAsync().GetAwaiter().GetResult();
        fixture.Vault.SetSearchConsoleRefreshToken("qa-test-client", "qa-refresh-token");
        source.NewClientSecret = "qa-replacement-secret";
        source.SaveAsync().GetAwaiter().GetResult();
        source = fixture.SearchConsole();
        source.LoadAsync().GetAwaiter().GetResult();
        Require(source.Connection is { ClientId: "qa-test-client", HasClientSecret: true, HasRefreshToken: true } && source.NewClientSecret == "", "Saved OAuth metadata must reload without exposing the secret");
        Require(fixture.Vault.GetSearchConsoleClientSecret("qa-test-client") == "qa-replacement-secret", "Save must replace the client secret");
        fixture.NoPlainTextSecrets();
        source.DeleteAsync().GetAwaiter().GetResult();
        Require(source.Connection is { Status: "disconnected", HasClientSecret: false, HasRefreshToken: false }
            && fixture.Vault.GetSearchConsoleClientSecret("qa-test-client") is null && fixture.Vault.GetSearchConsoleRefreshToken("qa-test-client") is null,
            "Delete must remove both OAuth credentials and disconnect Search Console");
        source.ConnectAsync().GetAwaiter().GetResult();
        Require(source.StatusMessage == "Save client ID and secret first" && source.CanRun, "Connect without saved credentials must stop before opening the browser");
    }

    public static void PerformanceSavesUrlsAndDeletes()
    {
        using var fixture = new Fixture();
        var source = fixture.Performance();
        source.NewCruxApiKey = "qa-first-key";
        source.NewPageSpeedApiKey = "qa-pagespeed-key";
        source.SaveAsync().GetAwaiter().GetResult();
        source.NewCruxApiKey = "qa-replacement-key";
        source.SaveAsync().GetAwaiter().GetResult();
        source.NewUrl = "https://save-test.invalid/path/";
        source.AddUrlAsync().GetAwaiter().GetResult();
        source.NewUrl = "https://save-test.invalid/path/";
        source.AddUrlAsync().GetAwaiter().GetResult();
        source = fixture.Performance();
        source.LoadAsync().GetAwaiter().GetResult();
        var url = source.Urls.Single(row => row.Url == "https://save-test.invalid/path/");
        Require(source.Connection is { HasCruxApiKey: true, HasPageSpeedApiKey: true } && fixture.Vault.GetCruxApiKey() == "qa-replacement-key" && fixture.Vault.GetPageSpeedApiKey() == "qa-pagespeed-key", "Performance must reload both keys and preserve the unchanged key");
        fixture.NoPlainTextSecrets();
        source.DeleteUrlAsync(url).GetAwaiter().GetResult();
        Require(!source.Urls.Any(row => row.Url == url.Url) && !fixture.Shell.AvailableDomains.Contains("save-test.invalid"), "Delete URL must update storage, list and available domains");
        source.DeleteAsync().GetAwaiter().GetResult();
        Require(source.Connection is { Status: "disconnected", HasCruxApiKey: false, HasPageSpeedApiKey: false } && fixture.Vault.GetCruxApiKey() is null && fixture.Vault.GetPageSpeedApiKey() is null, "Delete Performance must remove both keys and disconnect the integration");
    }

    public static void BingSavesSitesAndDeletes()
    {
        using var fixture = new Fixture();
        var source = fixture.Bing();
        source.NewApiKey = "qa-first-key";
        source.SaveAsync().GetAwaiter().GetResult();
        source.NewApiKey = "qa-replacement-key";
        source.SaveAsync().GetAwaiter().GetResult();
        source.NewSiteUrl = "https://SAVE-TEST.INVALID/";
        source.AddSiteAsync().GetAwaiter().GetResult();
        Require(source.Connection!.Sites.Contains("https://save-test.invalid/"), "Add must update the current connection metadata immediately");
        source.NewSiteUrl = "https://save-test.invalid/";
        source.AddSiteAsync().GetAwaiter().GetResult();
        source = fixture.Bing();
        source.LoadAsync().GetAwaiter().GetResult();
        Require(source.Sites.Count(site => site == "https://save-test.invalid/") == 1 && source.Connection!.Sites.Contains("https://save-test.invalid/") && fixture.Vault.GetBingApiKey() == "qa-replacement-key", "Bing must normalize repeated adds and reload the current key and sites");
        fixture.NoPlainTextSecrets();
        source.DeleteSiteAsync("https://save-test.invalid/").GetAwaiter().GetResult();
        Require(!source.Sites.Contains("https://save-test.invalid/") && !source.Connection!.Sites.Contains("https://save-test.invalid/"), "Delete site must remove it from both registry and saved connection metadata");
        source.DeleteAsync().GetAwaiter().GetResult();
        Require(source.Connection is { Status: "disconnected", HasApiKey: false } && fixture.Vault.GetBingApiKey() is null, "Delete Bing must remove the key and disconnect the integration");
    }

    private sealed partial class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Numeris-source-tests-" + Guid.NewGuid().ToString("N"));
        public SqliteDatabase Database { get; }
        public CredentialVault Vault { get; }
        public ConnectionsRepository Connections { get; }
        public ShellViewModel Shell { get; }

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            typeof(SqliteDatabase).Assembly.GetType("Numeris.Services.Database.Migrations")!.GetMethod("RunAll", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, new object[] { connection });
            Database = (SqliteDatabase)RuntimeHelpers.GetUninitializedObject(typeof(SqliteDatabase));
            typeof(SqliteDatabase).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Database, connection);
            typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Database, new SemaphoreSlim(1, 1));
            Vault = CredentialVaultRegressionTests.CreateWritableVault();
            Connections = new ConnectionsRepository(Database, Vault);
            var settings = (SettingsStore)Activator.CreateInstance(typeof(SettingsStore), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { Path.Combine(_directory, "settings.json") }, null)!;
            Shell = new ShellViewModel(settings, Connections);
        }

        public CloudflareSourceViewModel Cloudflare() => new(Connections, Vault, new CloudflareGraphqlClient(), new CloudflareSyncService(new CloudflareGraphqlClient(), new CloudflareRepository(Database), Vault, Connections), Shell);
        public WebAnalyticsSourceViewModel WebAnalytics() => new(Connections, Vault, new WebAnalyticsSyncService(new CloudflareRumClient(), new WebAnalyticsRepository(Database), Vault, Connections));
        public SearchConsoleSourceViewModel SearchConsole() => new(Connections, Vault, new GoogleOAuthFlow(new GoogleOAuthClient()), new SearchConsoleSyncService(new SearchConsoleClient(), new GoogleOAuthClient(), new SearchConsoleRepository(Database), new SitemapRepository(Database), Vault, Connections));
        public PerformanceSourceViewModel Performance() => new(Connections, Vault, new PerformanceSyncService(new CruxClient(), new PageSpeedClient(), new PerformanceRepository(Database), Connections, Vault), Shell);
        public BingSourceViewModel Bing() => new(Connections, Vault, new BingWebmasterSyncService(new BingWebmasterClient(), new BingRepository(Database), Connections, Vault));

        public void NoPlainTextSecrets()
        {
            var configs = Database.ReadAsync(connection => connection.Query<string>("SELECT config FROM connections WHERE config IS NOT NULL").ToList()).GetAwaiter().GetResult();
            Require(configs.All(config => !config.Contains("qa-first-", StringComparison.Ordinal) && !config.Contains("qa-replacement-", StringComparison.Ordinal) && !config.Contains("qa-pagespeed-key", StringComparison.Ordinal) && !config.Contains("qa-refresh-token", StringComparison.Ordinal)), "Connection metadata must not contain the test secrets");
        }

        public void Dispose()
        {
            Vault.DeleteCloudflareToken("save-test.invalid");
            Vault.DeleteWebAnalyticsToken("qa-test-account");
            Vault.DeleteSearchConsoleClientSecret("qa-test-client");
            Vault.DeleteSearchConsoleRefreshToken("qa-test-client");
            Vault.DeleteCruxApiKey();
            Vault.DeletePageSpeedApiKey();
            Vault.DeleteBingApiKey();
            Database.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
