using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Numeris.Helpers;
using Numeris.Services.Database;
using Numeris.Services.Performance;

Run("normalizes domain origin and home page into one site identity", () =>
{
    var fromDomain = SiteIdentity.FromDomainOrUrl("finnvek.com");
    var fromOrigin = SiteIdentity.FromDomainOrUrl("https://finnvek.com");
    var fromHome = SiteIdentity.FromDomainOrUrl("https://finnvek.com/");

    Equal("finnvek.com", fromDomain.Domain);
    Equal(fromDomain.Domain, fromOrigin.Domain);
    Equal(fromDomain.Domain, fromHome.Domain);
    Equal("https://finnvek.com", fromDomain.OriginUrl);
    Equal("https://finnvek.com/", fromDomain.HomePageUrl);
});

Run("rejects invalid site identity input", () =>
{
    Throws<ArgumentException>(() => SiteIdentity.FromDomainOrUrl("not a valid host name"));
});

Run("normalizes home page url to origin without trailing slash", () =>
{
    Equal("https://finnvek.com", PerformanceUrl.NormalizeOrigin("https://finnvek.com/"));
    Equal("https://knittoolsapp.com", PerformanceUrl.NormalizeOrigin("https://knittoolsapp.com/"));
});

Run("keeps page url with trailing slash for PageSpeed", () =>
{
    Equal("https://finnvek.com/", PerformanceUrl.NormalizePageUrl("https://finnvek.com"));
    Equal("https://knittoolsapp.com/tools/", PerformanceUrl.NormalizePageUrl(" https://knittoolsapp.com/tools "));
});

Run("viewmodels do not use ObservableProperty fields", () =>
{
    var root = FindRepositoryRoot();
    var viewModelDir = Path.Combine(root, "Numeris", "ViewModels");
    foreach (var file in Directory.EnumerateFiles(viewModelDir, "*.cs"))
    {
        var text = File.ReadAllText(file);
        if (text.Contains("[ObservableProperty] private", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} still uses ObservableProperty on a private field");
        }
    }
});

Run("SourcesViewModel delegates source behavior to child viewmodels", () =>
{
    var root = FindRepositoryRoot();
    var sourceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "SourcesViewModel.cs"));

    if (sourceViewModel.Contains("CloudflareGraphqlClient", StringComparison.Ordinal)
        || sourceViewModel.Contains("CloudflareRumClient", StringComparison.Ordinal)
        || sourceViewModel.Contains("SearchConsoleClient", StringComparison.Ordinal)
        || sourceViewModel.Contains("CredentialVault", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SourcesViewModel still depends on low-level integration clients or secrets");
    }
});

Run("sync services write through repositories instead of raw database access", () =>
{
    var root = FindRepositoryRoot();
    var syncDir = Path.Combine(root, "Numeris", "Services", "Sync");
    foreach (var file in Directory.EnumerateFiles(syncDir, "*.cs"))
    {
        var text = File.ReadAllText(file);
        if (text.Contains("SqliteDatabase", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} still depends on SqliteDatabase");
        }
    }
});

Run("connection registry tracks CrUX and PageSpeed independently", () =>
{
    var root = FindRepositoryRoot();
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));
    if (!migrations.Contains("('crux', 'crux'", StringComparison.Ordinal)
        || !migrations.Contains("('pagespeed', 'pagespeed'", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("connections defaults must include separate crux and pagespeed rows");
    }
});

Run("database migrations use explicit schema versions", () =>
{
    var root = FindRepositoryRoot();
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));

    Contains(migrations, "CurrentSchemaVersion = 2");
    Contains(migrations, "PRAGMA user_version");
    Contains(migrations, "RunPendingMigrations");
});

Run("repository batches can run inside SQLite transactions", () =>
{
    var root = FindRepositoryRoot();
    var database = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "SqliteDatabase.cs"));
    var performanceRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "PerformanceRepository.cs"));

    Contains(database, "WriteTransactionAsync");
    Contains(database, "BeginTransaction");
    Contains(performanceRepository, "WriteTransactionAsync");
});

Run("raw API payload storage is centrally bounded", () =>
{
    var root = FindRepositoryRoot();
    var policyPath = Path.Combine(root, "Numeris", "Services", "Database", "RawJsonStoragePolicy.cs");
    if (!File.Exists(policyPath))
    {
        throw new InvalidOperationException("RawJsonStoragePolicy.cs is missing");
    }

    var policy = File.ReadAllText(policyPath);
    var performanceSync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "PerformanceSyncService.cs"));
    var bingSync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "BingWebmasterSyncService.cs"));

    Contains(policy, "MaxRawJsonChars");
    Contains(policy, "TrimRawJson");
    Contains(performanceSync, "RawJsonStoragePolicy.TrimRawJson");
    Contains(bingSync, "RawJsonStoragePolicy.TrimRawJson");
});

Run("raw API payload storage redacts secrets before persistence", () =>
{
    var raw = """
        {
          "access_token": "access-secret",
          "refreshToken": "refresh-secret",
          "nested": {
            "client_secret": "client-secret",
            "url": "https://example.test/path?api_key=api-secret&safe=value",
            "header": "Authorization: Bearer bearer-secret"
          }
        }
        """;

    var stored = RawJsonStoragePolicy.TrimRawJson(raw);

    Contains(stored, RawJsonStoragePolicy.RedactedSecret);
    NotContains(stored, "access-secret");
    NotContains(stored, "refresh-secret");
    NotContains(stored, "client-secret");
    NotContains(stored, "api-secret");
    NotContains(stored, "bearer-secret");
});

Run("Bing page and raw stats keep dated history", () =>
{
    var root = FindRepositoryRoot();
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));
    var repository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "BingRepository.cs"));
    var sync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "BingWebmasterSyncService.cs"));

    Contains(migrations, "CREATE TABLE IF NOT EXISTS bing_page_stats");
    Contains(migrations, "PRIMARY KEY (site_url, page_url, date)");
    Contains(repository, "INSERT INTO bing_page_stats (site_url, page_url, date, clicks, impressions, raw_json, fetched_at)");
    Contains(repository, "ON CONFLICT(site_url, page_url, date)");
    Contains(sync, "BuildRawItemKey(query, date)");
    Contains(sync, "BuildRawItemKey(page, date)");
});

Run("Windows check scripts and command shims are documented", () =>
{
    var root = FindRepositoryRoot();
    var lintScript = Path.Combine(root, "tools", "lint-check.ps1");
    var securityScript = Path.Combine(root, "tools", "security-check.ps1");
    if (!File.Exists(lintScript) || !File.Exists(securityScript))
    {
        throw new InvalidOperationException("Windows check scripts are missing");
    }

    var lint = File.ReadAllText(lintScript);
    var security = File.ReadAllText(securityScript);
    var agents = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
    var gitignore = File.ReadAllText(Path.Combine(root, ".gitignore"));

    Contains(lint, "reports/ktlint.txt");
    Contains(lint, "reports/detekt.txt");
    Contains(lint, "reports/lint.txt");
    Contains(lint, "Write-CheckSummary");
    Contains(lint, "lint-check summary");
    Contains(lint, "Tee-Object -FilePath $reportPath -Append | Out-Host");
    Contains(security, "reports/security-code.txt");
    Contains(security, "reports/security-deps.txt");
    Contains(security, "--config $semgrepConfig --error --metrics=off");
    Contains(security, "Write-CheckSummary");
    Contains(security, "security-check summary");
    Contains(security, "Tee-Object -FilePath $reportPath -Append | Out-Host");
    Contains(agents, "function lc");
    Contains(agents, "function sc");
    Contains(gitignore, "reports/");
});

Run("Windows check scripts resolve repository root from child directories", () =>
{
    var root = FindRepositoryRoot();
    var childDir = Path.Combine(root, "Numeris");

    Equal(root, RunPowerShellScript(Path.Combine(root, "tools", "lint-check.ps1"), childDir, "-ResolveOnly"));
    Equal(root, RunPowerShellScript(Path.Combine(root, "tools", "security-check.ps1"), childDir, "-ResolveOnly"));
});

Run("SQL scalar helpers stay parameterized for security scans", () =>
{
    var root = FindRepositoryRoot();
    var summaryRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "SummaryRepository.cs"));
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));

    if (summaryRepository.Contains("cmd.CommandText = sql", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SummaryRepository must not use raw CommandText scalar helpers");
    }
    Contains(summaryRepository, "ExecuteScalar<long>");
    Contains(summaryRepository, "(\"$package\", Domains.PlayStorePackage)");
    Contains(migrations, "FROM pragma_table_info(@table)");
    Contains(migrations, "cmd.Parameters.AddWithValue(\"@column\", column)");
});

Run("legacy import uses Numeris service names and central source aliases", () =>
{
    var root = FindRepositoryRoot();
    var migrationDir = Path.Combine(root, "Numeris", "Services", "Migration");
    var files = Directory.EnumerateFiles(migrationDir, "*.cs").Select(Path.GetFileName).ToList();
    if (files.Any(file => file is not null && file.StartsWith("Pulse", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("Migration files should use neutral legacy names, not Pulse-prefixed service names");
    }

    var app = File.ReadAllText(Path.Combine(root, "Numeris", "App.xaml.cs"));
    var legacyMigration = File.ReadAllText(Path.Combine(migrationDir, "LegacyDataMigrationService.cs"));
    var legacyCredentials = File.ReadAllText(Path.Combine(migrationDir, "LegacyCredentialReader.cs"));

    Contains(app, "LegacyDataMigrationService");
    Contains(legacyMigration, "LegacyAppSource");
    Contains(legacyMigration, "AppDataFolder = \"com.finnvek.pulse\"");
    Contains(legacyMigration, "DatabaseFile = \"pulse.db\"");
    Contains(legacyMigration, "CredentialService = \"Pulse\"");
    Contains(legacyCredentials, "ReadKeyringPassword");
});

Run("legacy import preserves existing Numeris secrets", () =>
{
    var root = FindRepositoryRoot();
    var legacyMigration = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Migration", "LegacyDataMigrationService.cs"));

    Contains(legacyMigration, "string.IsNullOrWhiteSpace(_vault.GetWebAnalyticsToken(source.AccountId))");
    Contains(legacyMigration, "string.IsNullOrWhiteSpace(_vault.GetCloudflareToken(domain))");
    Contains(legacyMigration, "string.IsNullOrWhiteSpace(_vault.GetSearchConsoleClientSecret(clientId))");
    Contains(legacyMigration, "string.IsNullOrWhiteSpace(_vault.GetSearchConsoleRefreshToken(clientId))");
    Contains(legacyMigration, "LegacyCredentialReader.ReadKeyringPassword(");
});

Run("source deletes remove vault secrets instead of storing blanks", () =>
{
    var root = FindRepositoryRoot();
    var vault = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Secrets", "CredentialVault.cs"));
    var webAnalytics = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "Sources", "WebAnalyticsSourceViewModel.cs"));
    var searchConsole = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "Sources", "SearchConsoleSourceViewModel.cs"));
    var performance = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "Sources", "PerformanceSourceViewModel.cs"));
    var bing = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "Sources", "BingSourceViewModel.cs"));

    Contains(vault, "DeleteWebAnalyticsToken");
    Contains(vault, "DeleteSearchConsoleClientSecret");
    Contains(vault, "DeleteSearchConsoleRefreshToken");
    Contains(vault, "DeleteCruxApiKey");
    Contains(vault, "DeletePageSpeedApiKey");
    Contains(vault, "DeleteBingApiKey");
    Contains(webAnalytics, "_vault.DeleteWebAnalyticsToken");
    Contains(searchConsole, "_vault.DeleteSearchConsoleClientSecret");
    Contains(searchConsole, "_vault.DeleteSearchConsoleRefreshToken");
    Contains(performance, "_vault.DeleteCruxApiKey");
    Contains(performance, "_vault.DeletePageSpeedApiKey");
    Contains(bing, "_vault.DeleteBingApiKey");

    if (webAnalytics.Contains("SetWebAnalyticsToken(Connection.AccountId, \"\")", StringComparison.Ordinal)
        || searchConsole.Contains("SetSearchConsoleClientSecret(Connection.ClientId, \"\")", StringComparison.Ordinal)
        || searchConsole.Contains("SetSearchConsoleRefreshToken(Connection.ClientId, \"\")", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Delete paths must remove vault entries instead of saving blank secrets");
    }
});

Run("Search Console client omits raw API error bodies", () =>
{
    var root = FindRepositoryRoot();
    var client = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "SearchConsoleClient.cs"));

    Contains(client, "ApiRequestException");
    Contains(client, "ApiErrorMessage.FromBody(body)");
    if (client.Contains(": {body}", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SearchConsoleClient must not include raw response bodies in exception messages");
    }
});

Run("integration status messages sanitize upstream exceptions", () =>
{
    var root = FindRepositoryRoot();
    var sourcesDir = Path.Combine(root, "Numeris", "ViewModels", "Sources");
    var syncDir = Path.Combine(root, "Numeris", "Services", "Sync");
    foreach (var file in Directory.EnumerateFiles(sourcesDir, "*.cs").Concat(Directory.EnumerateFiles(syncDir, "*.cs")))
    {
        var text = File.ReadAllText(file);
        if (text.Contains("ex.Message", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} exposes raw exception messages");
        }
    }

    var apiErrors = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "ApiErrorMessage.cs"));
    var configs = File.ReadAllText(Path.Combine(root, "Numeris", "Models", "ConnectionConfigs.cs"));
    Contains(apiErrors, "BearerRegex");
    NotContains(configs, "StatusCode");
});

Run("GraphQL operation names use Numeris branding", () =>
{
    var root = FindRepositoryRoot();
    var cloudflareClient = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "CloudflareGraphqlClient.cs"));
    var rumClient = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "CloudflareRumClient.cs"));

    Contains(cloudflareClient, "query NumerisDailyTraffic");
    Contains(rumClient, "query NumerisWebAnalytics");
    if (cloudflareClient.Contains("query Pulse", StringComparison.Ordinal)
        || rumClient.Contains("query Pulse", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("GraphQL operation names still use Pulse branding");
    }
});

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static void Equal(string expected, string actual)
{
    if (!StringComparer.Ordinal.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'");
    }
}

static void Contains(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected text to contain '{expected}'");
    }
}

static void NotContains(string text, string unexpected)
{
    if (text.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected text not to contain '{unexpected}'");
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}");
}

static string RunPowerShellScript(string scriptPath, string root, string argument)
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = "pwsh",
        Arguments = $"-NoProfile -File \"{scriptPath}\" -Root \"{root}\" {argument}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    }) ?? throw new InvalidOperationException("Could not start pwsh");

    var output = process.StandardOutput.ReadToEnd().Trim();
    var error = process.StandardError.ReadToEnd().Trim();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"PowerShell script failed with {process.ExitCode}: {error}");
    }
    return output;
}

static string FindRepositoryRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (File.Exists(Path.Combine(dir, "Numeris.slnx")))
        {
            return dir;
        }
        dir = Directory.GetParent(dir)?.FullName;
    }
    throw new InvalidOperationException("Repository root not found");
}
