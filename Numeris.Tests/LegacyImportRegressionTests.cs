using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Services.Migration;

internal static class LegacyImportRegressionTests
{
    public static void SkipsIncompatibleLegacySchemas()
    {
        WithSource("CREATE TABLE sentinel (value INTEGER); INSERT INTO sentinel VALUES (7);", path =>
        {
            Require(Read("ReadWebAnalyticsSource", path) is null, "Missing registry must be skipped");
            Require(Read("ReadSearchConsoleSource", path) is null, "Missing registry must be skipped");
            Require(((IList)Read("ReadCloudflareSourceConnections", path)!).Count == 0, "Missing registry must be skipped");
        });
        WithSource("CREATE TABLE connections (id TEXT, config TEXT);", path =>
            Require(Read("ReadWebAnalyticsSource", path) is null, "Missing legacy columns must be skipped"));
    }

    public static void SkipsMalformedRowsAndKeepsValidIndependentSources()
    {
        WithSource("""
            CREATE TABLE connections (id TEXT, source TEXT, status TEXT, config TEXT);
            INSERT INTO connections VALUES ('wa','web_analytics','configured','{"account_id":4}');
            INSERT INTO connections VALUES ('sc','search_console','configured','{');
            INSERT INTO connections VALUES ('bad','cloudflare','configured','{');
            INSERT INTO connections VALUES ('good','cloudflare','configured','{"domain":"valid.example","zone_id":"zone"}');
            """, path =>
        {
            Require(Read("ReadWebAnalyticsSource", path) is null, "Wrong-shape config must be skipped");
            Require(Read("ReadSearchConsoleSource", path) is null, "Malformed config must be skipped");
            Require(((IList)Read("ReadCloudflareSourceConnections", path)!).Count == 1,
                "Malformed Cloudflare config must not discard valid neighboring rows");
        });
    }

    public static void ReadsLegacyAccountWithoutOptionalMappingsAndPreservesSource()
    {
        WithSource("""
            CREATE TABLE connections (id TEXT, source TEXT, status TEXT, config TEXT);
            INSERT INTO connections VALUES ('wa','web_analytics','connected','{"account_id":"account"}');
            """, path =>
        {
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var account = Read("ReadWebAnalyticsSource", path);
            Require(account is not null, "An account must remain importable without a legacy mapping table");
            Require(Convert.ToHexString(before) == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                "Read-only source inspection must preserve source bytes");
        });
    }

    public static void ReadsLegacySiteMappingMetadata()
    {
        WithSource("""
            CREATE TABLE connections (id TEXT, source TEXT, status TEXT, config TEXT);
            CREATE TABLE web_analytics_sites (domain TEXT, site_tag TEXT, discovered_at TEXT);
            INSERT INTO connections VALUES ('wa','web_analytics','connected','{"account_id":"account"}');
            INSERT INTO web_analytics_sites VALUES ('legacy.example','site-tag','2026-01-02');
            """, path =>
        {
            var account = Read("ReadWebAnalyticsSource", path)!;
            var sites = (IList)account.GetType().GetProperty("Sites")!.GetValue(account)!;
            Require(sites.Count == 1, "Dapper must materialize the saved legacy site mapping");
            var site = sites[0]!;
            Require((string)site.GetType().GetProperty("Domain")!.GetValue(site)! == "legacy.example"
                && (string)site.GetType().GetProperty("SiteTag")!.GetValue(site)! == "site-tag"
                && (string)site.GetType().GetProperty("DiscoveredAt")!.GetValue(site)! == "2026-01-02",
                "Reflection-populated properties must preserve site identity and discovery time");
        });
    }

    public static void NormalizesMockStatusAndPreservesConnected()
    {
        foreach (var status in new[] { "mock", " mock ", "MOCK", "" })
            Require((string)Read("NormalizeStatus", status)! == "configured", "Legacy mock state must not remain active");
        Require((string)Read("NormalizeStatus", "connected")! == "connected", "Connected status must remain connected");
    }

    public static void SkipsInvalidDomainsWithoutDiscardingValidImports()
    {
        WithSource("""
            CREATE TABLE connections (id TEXT, source TEXT, status TEXT, config TEXT);
            CREATE TABLE web_analytics_sites (domain TEXT, site_tag TEXT, discovered_at TEXT);
            INSERT INTO connections VALUES ('wa','web_analytics','connected','{"account_id":"account"}');
            INSERT INTO connections VALUES ('bad','cloudflare','configured','{"domain":"-bad.example","zone_id":"bad-zone"}');
            INSERT INTO connections VALUES ('good','cloudflare','connected','{"domain":"VALID.EXAMPLE","zone_id":"valid-zone"}');
            INSERT INTO web_analytics_sites VALUES ('-bad.example','bad-tag','2026-01-02'), ('VALID.EXAMPLE','valid-tag','2026-01-02');
            """, path =>
        {
            using var database = (Numeris.Services.Database.SqliteDatabase)typeof(DatabaseReviewRegressionTests)
                .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
            var vault = CredentialVaultRegressionTests.CreateWritableVault();
            vault.SetWebAnalyticsToken("account", "synthetic-token");
            vault.SetCloudflareToken("valid.example", "synthetic-token");
            var migration = new LegacyDataMigrationService(database, vault);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                foreach (var method in new[] { "ImportWebAnalyticsAsync", "ImportCloudflareAsync" })
                {
                    var import = typeof(LegacyDataMigrationService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(string) }, null)!;
                    ((System.Threading.Tasks.Task)import.Invoke(migration, new object[] { path })!).GetAwaiter().GetResult();
                }
            }
            Require(database.ReadAsync(connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM connections WHERE id='wa' OR id='cloudflare:valid.example'"))
                .GetAwaiter().GetResult() == 2, "Valid account and Cloudflare rows must survive invalid neighbors and repeated import");
            Require(database.ReadAsync(connection => connection.ExecuteScalar<string>("SELECT domain FROM web_analytics_sites"))
                .GetAwaiter().GetResult() == "valid.example", "Only valid normalized site mappings must be imported");
        });
    }

    private static object? Read(string name, string path)
    {
        try { return typeof(LegacyDataMigrationService).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { path }); }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
    }

    private static void WithSource(string sql, Action<string> inspect)
    {
        var path = Path.Combine(Path.GetTempPath(), $"numeris-legacy-review-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                connection.Execute(sql);
            }
            inspect(path);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
