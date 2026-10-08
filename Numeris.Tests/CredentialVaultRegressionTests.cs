using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Numeris.ViewModels;
using Numeris.ViewModels.Sources;

internal static class CredentialVaultRegressionTests
{
    public static void LiveStoreSavesReplacesAndDeletesTemporaryCredential()
    {
        var vault = new CredentialVault();
        var domain = $"review-{Guid.NewGuid():N}.invalid";
        var first = Guid.NewGuid().ToString("N");
        var second = Guid.NewGuid().ToString("N");
        Require(vault.GetCloudflareToken(domain) is null, "The temporary credential must not already exist");
        try
        {
            vault.SetCloudflareToken($" HTTPS://{domain.ToUpperInvariant()}/ ", $" {first} ");
            Require(vault.GetCloudflareToken(domain) == first, "Save must normalize the domain and trim the credential");
            vault.SetCloudflareToken(domain, second);
            Require(new CredentialVault().GetCloudflareToken(domain) == second, "Replacement must persist across vault instances");
            vault.SetCloudflareToken(domain, " ");
            Require(vault.GetCloudflareToken(domain) is null, "An empty value must delete the credential");
            vault.DeleteCloudflareToken(domain);
        }
        finally
        {
            vault.DeleteCloudflareToken(domain);
            Require(vault.GetCloudflareToken(domain) is null, "The temporary credential must be cleaned up");
        }
    }

    internal static CredentialVault CreateWritableVault()
    {
        var secrets = new Dictionary<(string Resource, string User), string>();
        Func<string, string, string> read = (resource, user) => secrets.TryGetValue((resource, user), out var secret)
            ? secret : throw new COMException("Synthetic missing credential", unchecked((int)0x80070490));
        Action<string, string, string> save = (resource, user, secret) => secrets[(resource, user)] = secret;
        Action<string, string> delete = (resource, user) => secrets.Remove((resource, user));
        var vault = (CredentialVault)Activator.CreateInstance(typeof(CredentialVault), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object[] { read, save, delete }, culture: null)!;
        Require(typeof(CredentialVault).GetField("_vault", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vault) is null,
            "An isolated writable vault must never construct the Windows credential store");
        return vault;
    }

    internal static CredentialVault CreateVault(Func<string, string, string>? read = null)
    {
        read ??= (_, _) => throw new COMException("Synthetic missing credential", unchecked((int)0x80070490));
        var vault = (CredentialVault)Activator.CreateInstance(typeof(CredentialVault), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object[] { read }, culture: null)!;
        Require(typeof(CredentialVault).GetField("_vault", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vault) is null,
            "An isolated reader must never construct the Windows credential store");
        return vault;
    }

    public static void MissingCredentialsReturnNull()
    {
        var vault = CreateVault();
        foreach (var read in Readers(vault)) Require(read() is null, "Only an absent credential should return null");
    }

    public static void SuccessfulReadsKeepProviderIdentity()
    {
        var identities = new List<(string Resource, string User)>();
        var vault = CreateVault((resource, user) => { identities.Add((resource, user)); return "synthetic-password"; });
        foreach (var read in Readers(vault)) Require(read() == "synthetic-password", "Successful reads must retain the password");
        Require(identities.SequenceEqual(new[]
        {
            ("Numeris.Cloudflare", "test.example"), ("Numeris.WebAnalytics", "test-account"),
            ("Numeris.SearchConsole.ClientSecret", "test-client"), ("Numeris.SearchConsole.RefreshToken", "test-client"),
            ("Numeris.Crux.ApiKey", "default"), ("Numeris.PageSpeed.ApiKey", "default"), ("Numeris.BingWebmaster.ApiKey", "default"),
        }), "Provider resources and normalized usernames must remain unchanged");
    }

    public static void ReadFailuresAreReportedWithoutExposingDetails()
    {
        foreach (var failure in new Exception[]
        {
            new COMException("Synthetic private detail", unchecked((int)0x80070005)),
            new UnauthorizedAccessException("Synthetic private detail"), new IOException("Synthetic private detail"),
        })
        {
            var vault = CreateVault((_, _) => throw failure);
            foreach (var read in Readers(vault))
            {
                try { read(); }
                catch (InvalidOperationException ex)
                {
                    Require(ReferenceEquals(ex.InnerException, failure)
                        && ex.Message.Contains("saved credentials", StringComparison.Ordinal)
                        && !ApiErrorMessage.Sanitize(ex).Contains("Synthetic private detail", StringComparison.Ordinal),
                        "Store failures must remain distinct from missing keys and expose safe recovery text");
                    continue;
                }
                throw new InvalidOperationException("A credential read failure was treated as a missing key");
            }
        }
    }

    public static void CloudflareRegistryPropagatesVaultFailures()
    {
        using var database = CreateDatabase();
        var fail = false;
        var repository = new ConnectionsRepository(database, CreateVault((_, _) => fail
            ? throw new UnauthorizedAccessException("Synthetic private detail") : "synthetic-password"));
        repository.UpsertCloudflareAsync(new CloudflareConnectionConfig { Domain = "test.example", ZoneId = "test-zone" }, "connected")
            .GetAwaiter().GetResult();
        database.WriteAsync(c => c.Execute("INSERT INTO connections(id,source,status,config) VALUES ('bad','cloudflare','configured','{')"))
            .GetAwaiter().GetResult();
        Require(repository.ListCloudflareConnectionsAsync().GetAwaiter().GetResult().Count == 1, "Malformed neighbors must still be skipped");
        fail = true;
        try { repository.ListCloudflareConnectionsAsync().GetAwaiter().GetResult(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("saved credentials", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Cloudflare registry hid the credential-store failure");
    }

    public static void SourceLoadsPreserveStateAndOtherProviders()
    {
        using var database = CreateDatabase();
        string? failedResource = null;
        var vault = CreateVault((resource, _) => failedResource == resource || failedResource == "all"
            ? throw new UnauthorizedAccessException("Synthetic private detail") : "synthetic-password");
        var repository = new ConnectionsRepository(database, vault);
        Seed(repository);
        var sources = CreateSources(database, repository, vault);
        sources.LoadAsync().GetAwaiter().GetResult();
        var previousCloudflare = sources.Cloudflare.Connections.Single();
        failedResource = "Numeris.Cloudflare";
        sources.LoadAsync().GetAwaiter().GetResult();
        Require(ReferenceEquals(previousCloudflare, sources.Cloudflare.Connections.Single())
            && sources.Cloudflare.StatusMessage.StartsWith("Load failed:", StringComparison.Ordinal)
            && sources.WebAnalytics.Connection!.HasToken && sources.SearchConsole.Connection!.HasClientSecret
            && sources.Performance.Connection!.HasCruxApiKey && sources.Bing.Connection!.HasApiKey,
            "A failed provider must retain its previous state without blocking the other providers");
        var previousWebAnalytics = sources.WebAnalytics.Connection;
        var previousSearch = sources.SearchConsole.Connection;
        var previousPerformance = sources.Performance.Connection;
        var previousBing = sources.Bing.Connection;
        failedResource = "all";
        sources.LoadAsync().GetAwaiter().GetResult();
        Require(ReferenceEquals(previousCloudflare, sources.Cloudflare.Connections.Single())
            && ReferenceEquals(previousWebAnalytics, sources.WebAnalytics.Connection)
            && ReferenceEquals(previousSearch, sources.SearchConsole.Connection)
            && ReferenceEquals(previousPerformance, sources.Performance.Connection)
            && ReferenceEquals(previousBing, sources.Bing.Connection), "Failed loads must not replace saved connection information");
        foreach (var source in SourceList(sources))
        {
            var message = (string)source.GetType().GetProperty("StatusMessage")!.GetValue(source)!;
            Require(message.StartsWith("Load failed:", StringComparison.Ordinal) && !message.Contains("Synthetic private detail", StringComparison.Ordinal),
                "Each provider must show a safe load failure");
        }
    }

    public static void FailedReadsPreventMetadataSaves()
    {
        using var database = CreateDatabase();
        var fail = false;
        var vault = CreateVault((_, _) => fail ? throw new IOException("Synthetic private detail") : "synthetic-password");
        var repository = new ConnectionsRepository(database, vault);
        Seed(repository);
        var sources = CreateSources(database, repository, vault);
        sources.LoadAsync().GetAwaiter().GetResult();
        sources.Cloudflare.NewDomain = "test.example";
        sources.Cloudflare.NewZoneId = "new-zone";
        var before = Snapshot(database);
        fail = true;
        foreach (var source in SourceList(sources))
        {
            ((System.Threading.Tasks.Task)source.GetType().GetMethod("SaveAsync")!.Invoke(source, null)!).GetAwaiter().GetResult();
            var message = (string)source.GetType().GetProperty("StatusMessage")!.GetValue(source)!;
            Require(message.StartsWith("Save failed:", StringComparison.Ordinal)
                && (bool)source.GetType().GetProperty("CanRun")!.GetValue(source)!, "Failed reads must stop saves and release the busy state");
        }
        Require(before == Snapshot(database), "Read failures must preserve all connection metadata");
    }

    private static Func<string?>[] Readers(CredentialVault vault) => new Func<string?>[]
    {
        () => vault.GetCloudflareToken("https://TEST.Example./"), () => vault.GetWebAnalyticsToken(" test-account "),
        () => vault.GetSearchConsoleClientSecret(" test-client "), () => vault.GetSearchConsoleRefreshToken(" test-client "),
        vault.GetCruxApiKey, vault.GetPageSpeedApiKey, vault.GetBingApiKey,
    };

    private static SqliteDatabase CreateDatabase() => (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
        .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;

    private static void Seed(ConnectionsRepository repository)
    {
        repository.UpsertCloudflareAsync(new CloudflareConnectionConfig { Domain = "test.example", ZoneId = "test-zone" }, "connected").GetAwaiter().GetResult();
        repository.UpsertWebAnalyticsAsync(new WebAnalyticsConnectionConfig { AccountId = "test-account" }, "connected").GetAwaiter().GetResult();
        repository.UpsertSearchConsoleAsync(new SearchConsoleConnectionConfig { ClientId = "test-client" }, "connected").GetAwaiter().GetResult();
        repository.UpsertPerformanceAsync(new PerformanceConnectionConfig(), "connected").GetAwaiter().GetResult();
        repository.UpsertBingAsync(new BingConnectionConfig(), "connected").GetAwaiter().GetResult();
    }

    private static SourcesViewModel CreateSources(SqliteDatabase database, ConnectionsRepository connections, CredentialVault vault)
    {
        var shell = (ShellViewModel)typeof(DomainSelectionRegressionTests).GetMethod("CreateShell", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { connections })!;
        return new SourcesViewModel(
            new CloudflareSourceViewModel(connections, vault, new CloudflareGraphqlClient(), new CloudflareSyncService(new CloudflareGraphqlClient(), new CloudflareRepository(database), vault, connections), shell),
            new WebAnalyticsSourceViewModel(connections, vault, new WebAnalyticsSyncService(new CloudflareRumClient(), new WebAnalyticsRepository(database), vault, connections)),
            new SearchConsoleSourceViewModel(connections, vault, new GoogleOAuthFlow(new GoogleOAuthClient()), new SearchConsoleSyncService(new SearchConsoleClient(), new GoogleOAuthClient(), new SearchConsoleRepository(database), new SitemapRepository(database), vault, connections)),
            new PerformanceSourceViewModel(connections, vault, new PerformanceSyncService(new CruxClient(), new PageSpeedClient(), new PerformanceRepository(database), connections, vault), shell),
            new BingSourceViewModel(connections, vault, new BingWebmasterSyncService(new BingWebmasterClient(), new BingRepository(database), connections, vault)));
    }

    private static object[] SourceList(SourcesViewModel sources) => new object[]
        { sources.Cloudflare, sources.WebAnalytics, sources.SearchConsole, sources.Performance, sources.Bing };

    private static string Snapshot(SqliteDatabase database) => database.ReadAsync(c => string.Join("\n",
        c.Query<string>("SELECT id || '|' || status || '|' || COALESCE(config,'') || '|' || COALESCE(last_sync,'') FROM connections ORDER BY id")))
        .GetAwaiter().GetResult();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
