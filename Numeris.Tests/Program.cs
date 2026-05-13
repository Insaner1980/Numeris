using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Performance;
using Numeris.ViewModels;
using Numeris.ViewModels.Sources;

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

    Contains(migrations, "CurrentSchemaVersion = 4");
    Contains(migrations, "PRAGMA user_version");
    Contains(migrations, "RunPendingMigrations");
});

Run("application does not seed mock data on startup", () =>
{
    var root = FindRepositoryRoot();
    var app = File.ReadAllText(Path.Combine(root, "Numeris", "App.xaml.cs"));
    var servicesDir = Path.Combine(root, "Numeris", "Services");

    NotContains(app, "MockSeeder");
    NotContains(app, "SeedAsync");
    if (Directory.Exists(Path.Combine(servicesDir, "MockData")))
    {
        var files = Directory.EnumerateFiles(Path.Combine(servicesDir, "MockData"), "*", SearchOption.AllDirectories);
        if (files.Any())
        {
            throw new InvalidOperationException("MockData service files should not exist");
        }
    }
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

Run("raw API payload storage redacts PageSpeed detail arrays without reparenting nodes", () =>
{
    var raw = """
        {
          "type": "table",
          "items": [
            {
              "url": "https://example.test/?key=api-secret",
              "node": {
                "type": "node",
                "snippet": "<script src=\"https://example.test/app.js?token=token-secret\"></script>"
              }
            }
          ]
        }
        """;

    var stored = RawJsonStoragePolicy.TrimDetailsJson(raw) ?? "";

    Contains(stored, RawJsonStoragePolicy.RedactedSecret);
    NotContains(stored, "api-secret");
    NotContains(stored, "token-secret");
});

Run("CrUX test treats NotFound as missing field data instead of key failure", () =>
{
    var root = FindRepositoryRoot();
    var performanceSync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "PerformanceSyncService.cs"));

    Contains(performanceSync, "catch (ApiRequestException ex) when (ex.IsNotFound)");
    Contains(performanceSync, "no CrUX field data");
    Contains(performanceSync, "CrUX API key works");
});

Run("Performance source sync status explains missing CrUX field data", () =>
{
    var status = PerformanceSourceViewModel.FormatSyncStatus(new()
    {
        UrlsSynced = 2,
        CruxMetricPoints = 0,
        CruxSkipped = 16,
        PageSpeedRuns = 4,
        PageSpeedAudits = 596,
        PageSpeedErrors = 0,
    });

    Contains(status, "Synced 2 URL(s)");
    Contains(status, "CrUX: no field data for configured URLs");
    Contains(status, "PageSpeed: 4 run(s), 596 audit row(s), 0 error(s)");
    NotContains(status, "skipped");
    NotContains(status, "lookup");
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

Run("Bing and Performance pages are first-class navigation routes", () =>
{
    var root = FindRepositoryRoot();
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var mainWindowCode = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml.cs"));
    var shellViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "ShellViewModel.cs"));
    var appCode = File.ReadAllText(Path.Combine(root, "Numeris", "App.xaml.cs"));

    Contains(mainWindow, "Content=\"Google Search\"");
    Contains(mainWindow, "Tag=\"bing\" Content=\"Bing\"");
    Contains(mainWindow, "Tag=\"performance\" Content=\"Performance\"");
    Contains(mainWindowCode, "[\"bing\"] = typeof(BingPage)");
    Contains(mainWindowCode, "[\"performance\"] = typeof(PerformancePage)");
    Contains(shellViewModel, "or \"bing\" or \"performance\"");
    Contains(appCode, "services.AddTransient<BingViewModel>();");
    Contains(appCode, "services.AddTransient<PerformanceViewModel>();");
    Contains(appCode, "services.AddTransient<BingPage>();");
    Contains(appCode, "services.AddTransient<PerformancePage>();");
});

Run("navigation items use packaged filled image icons", () =>
{
    var root = FindRepositoryRoot();
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var projectFile = File.ReadAllText(Path.Combine(root, "Numeris", "Numeris.csproj"));
    var iconDir = Path.Combine(root, "Numeris", "Assets", "Icons");
    var icons = new[]
    {
        "overview-filled.png",
        "cloudflare-filled.png",
        "google-search-filled.png",
        "bing-filled.png",
        "performance-filled.png",
        "health-filled.png",
        "sources-filled.png"
    };

    Contains(mainWindow, "Width=\"20\"");
    Contains(mainWindow, "Height=\"20\"");
    foreach (var icon in icons)
    {
        Contains(mainWindow, $"Source=\"ms-appx:///Assets/Icons/{icon}\"");
        Contains(projectFile, $"Assets\\Icons\\{icon}");
        if (!File.Exists(Path.Combine(iconDir, icon)))
        {
            throw new InvalidOperationException($"Navigation icon asset is missing from Numeris/Assets/Icons: {icon}");
        }
    }

    if (mainWindow.Contains("<FontIcon", StringComparison.Ordinal) || mainWindow.Contains("-outline.png", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Navigation should use one filled ImageIcon style instead of mixing FontIcon and outline assets.");
    }
});

Run("Bing and Performance reports read through repositories", () =>
{
    var root = FindRepositoryRoot();
    var bingRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "BingRepository.cs"));
    var performanceRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "PerformanceRepository.cs"));
    var bingViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "BingViewModel.cs"));
    var performanceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "PerformanceViewModel.cs"));

    Contains(bingRepository, "GetTrafficDailyAsync");
    Contains(bingRepository, "GetQueriesAsync");
    Contains(bingRepository, "GetPagesAsync");
    Contains(bingRepository, "GetRawMethodSummaryAsync");
    Contains(bingRepository, "GetCrawlIssueItemsAsync");
    Contains(performanceRepository, "GetLatestCruxCoreVitalsAsync");
    Contains(performanceRepository, "GetCruxTrendAsync");
    Contains(performanceRepository, "GetLatestPageSpeedRunsAsync");
    Contains(performanceRepository, "GetPageSpeedScoreTrendAsync");
    Contains(performanceRepository, "GetPageSpeedAuditIssuesAsync");
    NotContains(bingViewModel, "SqliteDatabase");
    NotContains(performanceViewModel, "SqliteDatabase");
});

Run("YouTube schema and connection registry are first-class", () =>
{
    var root = FindRepositoryRoot();
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));
    var sqliteDatabase = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "SqliteDatabase.cs"));

    Contains(migrations, "CurrentSchemaVersion = 4");
    Contains(migrations, "CREATE TABLE IF NOT EXISTS youtube_channels");
    Contains(migrations, "CREATE TABLE IF NOT EXISTS youtube_videos");
    Contains(migrations, "CREATE TABLE IF NOT EXISTS youtube_daily");
    Contains(migrations, "CREATE TABLE IF NOT EXISTS youtube_video_stats");
    Contains(migrations, "CREATE TABLE IF NOT EXISTS youtube_countries");
    Contains(migrations, "CREATE TABLE IF NOT EXISTS youtube_traffic_sources");
    Contains(migrations, "CREATE TABLE IF NOT EXISTS youtube_devices");
    Contains(migrations, "CREATE TABLE IF NOT EXISTS youtube_retention_points");
    Contains(migrations, "('youtube', 'youtube', 'disconnected')");
    Contains(sqliteDatabase, "DELETE FROM youtube_daily;");
    Contains(sqliteDatabase, "DELETE FROM youtube_retention_points;");
});

Run("Google OAuth token handling is centralized for Search Console and YouTube", () =>
{
    var root = FindRepositoryRoot();
    var oauthClient = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Auth", "GoogleOAuthClient.cs"));
    var oauthFlow = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Auth", "GoogleOAuthFlow.cs"));
    var searchClient = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "SearchConsoleClient.cs"));
    var youtubeSync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "YouTubeSyncService.cs"));

    Contains(oauthClient, "BuildAuthUrl");
    Contains(oauthClient, "ExchangeCodeAsync");
    Contains(oauthClient, "RefreshAccessTokenAsync");
    Contains(oauthFlow, "IReadOnlyList<string> scopes");
    Contains(oauthFlow, "BuildAuthUrl(clientId, redirectUri, state, scopes)");
    Contains(youtubeSync, "GoogleOAuthClient");
    NotContains(searchClient, "TokenEndpoint");
    NotContains(searchClient, "ExchangeCodeAsync");
    NotContains(searchClient, "RefreshAccessTokenAsync");
});

Run("YouTube sync and reporting follow repository and visual architecture", () =>
{
    var root = FindRepositoryRoot();
    var appCode = File.ReadAllText(Path.Combine(root, "Numeris", "App.xaml.cs"));
    var sync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "YouTubeSyncService.cs"));
    var repository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "YouTubeRepository.cs"));
    var viewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "YouTubeViewModel.cs"));
    var page = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "YouTubePage.xaml"));
    var pageCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "YouTubePage.xaml.cs"));

    Contains(appCode, "services.AddSingleton<YouTubeRepository>();");
    Contains(appCode, "services.AddSingleton<YouTubeDataClient>();");
    Contains(appCode, "services.AddSingleton<YouTubeAnalyticsClient>();");
    Contains(appCode, "services.AddSingleton<YouTubeSyncService>();");
    Contains(appCode, "services.AddTransient<YouTubeViewModel>();");
    Contains(appCode, "services.AddTransient<YouTubePage>();");
    NotContains(sync, "SqliteDatabase");
    Contains(sync, "ApiErrorMessage.Sanitize");
    Contains(sync, "retentionVideos = videos.Take(25)");
    Contains(repository, "WriteTransactionAsync");
    Contains(viewModel, "ChartPalette.Accent");
    Contains(viewModel, "ChartPalette.Secondary");
    Contains(page, "Text=\"YouTube\"");
    Contains(page, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(page, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(page, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(page, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(page, "Style=\"{StaticResource DataListViewStyle}\"");
    Contains(page, "Overview");
    Contains(page, "Videos");
    Contains(page, "Geography");
    Contains(page, "Traffic");
    Contains(page, "Devices");
    Contains(page, "Retention");
    Contains(pageCode, "ChartTheme.CreateCartesianChart()");
    Contains(pageCode, "ChartTheme.CreateChartSurface(chart)");
});

Run("YouTube is available from navigation and Sources", () =>
{
    var root = FindRepositoryRoot();
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var mainWindowCode = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml.cs"));
    var shellViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "ShellViewModel.cs"));
    var sourcesViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "SourcesViewModel.cs"));
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));
    var sourcesCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml.cs"));
    var projectFile = File.ReadAllText(Path.Combine(root, "Numeris", "Numeris.csproj"));

    Contains(mainWindow, "Tag=\"youtube\" Content=\"YouTube\"");
    Contains(mainWindow, "Source=\"ms-appx:///Assets/Icons/youtube-filled.png\"");
    Contains(mainWindowCode, "[\"youtube\"] = typeof(YouTubePage)");
    Contains(shellViewModel, "or \"youtube\"");
    Contains(sourcesViewModel, "YouTubeSourceViewModel YouTube");
    Contains(sourcesPage, "YouTube Analytics");
    Contains(sourcesPage, "Uses the existing Google OAuth client when Search Console is configured.");
    Contains(sourcesPage, "Advanced: override YouTube OAuth credentials");
    Contains(sourcesPage, "ViewModel.YouTube.StatusMessage");
    Contains(sourcesCode, "SaveYouTubeButton_Click");
    Contains(sourcesCode, "ConnectYouTubeButton_Click");
    Contains(projectFile, "Assets\\Icons\\youtube-filled.png");
});

Run("YouTube source reuses existing Google OAuth credentials by default", () =>
{
    var root = FindRepositoryRoot();
    var sourceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "Sources", "YouTubeSourceViewModel.cs"));
    var sync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "YouTubeSyncService.cs"));

    Contains(sourceViewModel, "EnsureConfiguredFromGoogleDefaultsAsync");
    Contains(sourceViewModel, "GetSearchConsoleAsync()");
    Contains(sourceViewModel, "ResolveClientSecret");
    Contains(sourceViewModel, "GetSearchConsoleClientSecret(clientId)");
    Contains(sync, "GetSearchConsoleClientSecret(clientId)");
    NotContains(sourceViewModel, "Save YouTube client ID and secret first");
});

Run("YouTube videos list falls back to Data API metadata when Analytics has no rows", () =>
{
    var root = FindRepositoryRoot();
    var repository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "YouTubeRepository.cs"));

    Contains(repository, "FROM youtube_videos v");
    Contains(repository, "LEFT JOIN youtube_video_stats s ON s.video_id = v.video_id");
    Contains(repository, "COALESCE(s.views, v.view_count, 0) AS Views");
    Contains(repository, "COALESCE(s.likes, v.like_count, 0) AS Likes");
    Contains(repository, "COALESCE(s.comments, v.comment_count, 0) AS Comments");
});

Run("Performance page formats PageSpeed query window from DateOnly safely", () =>
{
    var root = FindRepositoryRoot();
    var performanceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "PerformanceViewModel.cs"));

    NotContains(performanceViewModel, "range.Start.ToString(\"yyyy-MM-ddTHH:mm:ss\"");
    NotContains(performanceViewModel, "range.End.AddDays(1).ToString(\"yyyy-MM-ddTHH:mm:ss\"");
    Contains(performanceViewModel, "ToDateTime(TimeOnly.MinValue)");
    Contains(performanceViewModel, "ToDateTime(TimeOnly.MaxValue)");
});

Run("SLNX keeps Visual Studio project configuration mappings simple", () =>
{
    var root = FindRepositoryRoot();
    var solution = File.ReadAllText(Path.Combine(root, "Numeris.slnx"));

    Contains(solution, "<Platform Name=\"x64\" />");
    Contains(solution, "<Project Path=\"Numeris/Numeris.csproj\"");
    Contains(solution, "<Project Path=\"Numeris.Tests/Numeris.Tests.csproj\"");
    NotContains(solution, "Solution=\"*|x64\"");
});

Run("Bing sync keeps narrow Webmaster API scope", () =>
{
    var root = FindRepositoryRoot();
    var sync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "BingWebmasterSyncService.cs"));

    Contains(sync, "\"GetUserSites\"");
    Contains(sync, "\"GetRankAndTrafficStats\"");
    Contains(sync, "\"GetQueryStats\"");
    Contains(sync, "\"GetPageStats\"");
    Contains(sync, "\"GetCrawlStats\"");
    Contains(sync, "\"GetCrawlIssues\"");
    NotContains(sync, "GetChildrenUrlInfo");
    NotContains(sync, "GetUrlLinks");
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

Run("Google OAuth token errors are actionable and sanitized", () =>
{
    var message = ApiErrorMessage.FromBody(
        """
        {
          "error": "invalid_grant",
          "error_description": "Bad Request",
          "refresh_token": "refresh-secret",
          "client_secret": "client-secret"
        }
        """);

    Contains(message ?? "", "invalid_grant");
    Contains(message ?? "", "Connect Google account again");
    NotContains(message ?? "", "refresh-secret");
    NotContains(message ?? "", "client-secret");
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

Run("WinUI visual system uses Mica shell and centralized surface tokens", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var dashboard = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var kpiCard = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "KpiCard.xaml"));

    Contains(mainWindow, "<MicaBackdrop");
    NotContains(mainWindow, "<DesktopAcrylicBackdrop");
    Contains(mainWindow, "Background=\"Transparent\"");
    Contains(mainWindow, "PaneDisplayMode=\"Left\"");
    Contains(mainWindow, "IsPaneToggleButtonVisible=\"False\"");
    Contains(tokens, "AppBackgroundColor\">#444444");
    Contains(tokens, "NavigationLayerBrush");
    Contains(tokens, "NavigationViewDefaultPaneBackground");
    Contains(tokens, "ContentLayerBrush");
    Contains(tokens, "CardSurfaceBrush");
    Contains(tokens, "ChartPanelBrush");
    Contains(tokens, "NumerisCardBorderBrush");
    Contains(tokens, "Segoe UI Variable");
    Contains(dashboard, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(dashboard, "Style=\"{StaticResource StatusStripBorderStyle}\"");
    Contains(kpiCard, "Style=\"{StaticResource KpiCardSurfaceStyle}\"");
});

Run("Shell uses a global branded bitmap backdrop", () =>
{
    var root = FindRepositoryRoot();
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var dashboard = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var project = File.ReadAllText(Path.Combine(root, "Numeris", "Numeris.csproj"));
    var backdropAssetPath = Path.Combine(root, "Numeris", "Assets", "AppBackdrop.png");

    if (!File.Exists(backdropAssetPath))
    {
        throw new InvalidOperationException("AppBackdrop.png is missing");
    }

    var header = File.ReadAllBytes(backdropAssetPath).Take(8).ToArray();
    var expectedPngHeader = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
    if (!header.SequenceEqual(expectedPngHeader))
    {
        throw new InvalidOperationException("AppBackdrop.png must be a PNG image");
    }

    Contains(project, "Assets\\AppBackdrop.png");
    Contains(mainWindow, "x:Name=\"AppBackdropImage\"");
    Contains(mainWindow, "Source=\"ms-appx:///Assets/AppBackdrop.png\"");
    Contains(mainWindow, "Stretch=\"UniformToFill\"");
    Contains(mainWindow, "Opacity=\"{StaticResource AppBackdropOpacity}\"");
    Contains(mainWindow, "x:Name=\"AppBackdropScrim\"");
    Contains(tokens, "AppBackdropOpacity");
    Contains(tokens, "AppBackdropScrimBrush");
    NotContains(project, "Assets\\OverviewHeroBackdrop.png");
    NotContains(tokens, "OverviewHeroBackdrop");
    NotContains(tokens, "OverviewHeroFade");
    NotContains(dashboard, "OverviewHeroBackdrop.png");
    NotContains(dashboard, "HeroBackdropImage");
});

Run("Global backdrop is subdued behind readable Fluent surfaces", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var mainWindowCode = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml.cs"));
    var chartPalette = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "ChartPalette.cs"));

    Contains(tokens, "AppBackdropOpacity\">0.72");
    Contains(tokens, "AppBackdropScrimColor\">#B3000000");
    Contains(tokens, "ContentLayerColor\">#00000000");
    Contains(tokens, "NavigationLayerColor\">#00000000");
    Contains(tokens, "CardSurfaceColor\">#52000000");
    Contains(tokens, "ControlSurfaceColor\">#38000000");
    Contains(tokens, "ChartPanelColor\">#54000000");
    Contains(tokens, "NumerisCardBorderColor\">#48FFFFFF");
    Contains(tokens, "ChartGridLineColor\">#38FFFFFF");
    Contains(tokens, "ChartMutedColor");
    Contains(chartPalette, "ChartMutedColor");
    Contains(tokens, "NavigationViewContentBackground");
    Contains(tokens, "NavigationViewContentGridBorderBrush");
    Contains(tokens, "NavigationViewContentGridBorderThickness\">0");
    Contains(mainWindowCode, "ConfigureTitleBarColors");
    Contains(mainWindowCode, "AppWindowTitleBar.IsCustomizationSupported");
    Contains(mainWindow, "Background=\"Transparent\"");
});

Run("main window uses a custom transparent title bar integrated into the shell", () =>
{
    var root = FindRepositoryRoot();
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var mainWindowCode = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml.cs"));
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));

    Contains(mainWindow, "x:Name=\"AppTitleBar\"");
    Contains(mainWindow, "Text=\"Numeris\"");
    Contains(mainWindow, "Style=\"{StaticResource AppTitleTextBlockStyle}\"");
    Contains(mainWindow, "Background=\"Transparent\"");
    Contains(mainWindowCode, "ConfigureCustomTitleBar");
    Contains(mainWindowCode, "ExtendsContentIntoTitleBar = true");
    Contains(mainWindowCode, "SetTitleBar(AppTitleBar)");
    Contains(mainWindowCode, "ReadColorResource");
    Contains(tokens, "AppTitleBarHeight");
    Contains(tokens, "AppTitleTextBlockStyle");
});

Run("Pages use unified header surfaces and page-level scrolling", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var dashboardPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var cloudflarePage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml"));
    var searchPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SearchConsolePage.xaml"));
    var healthPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "HealthPage.xaml"));

    Contains(tokens, "PageHeaderBorderStyle");
    Contains(tokens, "PageHeaderMargin");
    Contains(tokens, "<Thickness x:Key=\"PageHeaderPadding\">0,20,0,18</Thickness>");
    Contains(tokens, "PageScrollContentPadding");
    Contains(tokens, """
    <Style x:Key="PageHeaderBorderStyle" TargetType="Border">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderBrush" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
""");
    Contains(tokens, "PrimaryActionButtonStyle");
    Contains(tokens, "TopTabSelectorBarStyle");
    Contains(dashboardPage, "Style=\"{StaticResource PageHeaderBorderStyle}\"");
    Contains(cloudflarePage, "Style=\"{StaticResource PageHeaderBorderStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource PageHeaderBorderStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource PageHeaderBorderStyle}\"");
    Contains(cloudflarePage, "x:Name=\"PageScrollViewer\"");
    Contains(cloudflarePage, "Margin=\"{StaticResource PageHeaderMargin}\"");
    Contains(cloudflarePage, "Padding=\"{StaticResource PageScrollContentPadding}\"");
    Contains(cloudflarePage, "HorizontalScrollBarVisibility=\"Disabled\"");
    Contains(cloudflarePage, "TrafficPanel");
    NotContains(cloudflarePage, "<ScrollViewer Grid.Row=\"2\"");
});

Run("Overview charts use a shared Fluent chart theme", () =>
{
    var root = FindRepositoryRoot();
    var chartThemePath = Path.Combine(root, "Numeris", "Themes", "ChartTheme.cs");
    if (!File.Exists(chartThemePath))
    {
        throw new InvalidOperationException("ChartTheme.cs is missing");
    }

    var dashboardPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml.cs"));
    var dashboardXaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var chartTheme = File.ReadAllText(chartThemePath);

    Contains(dashboardPage, "ChartTheme.CreateCartesianChart()");
    Contains(dashboardPage, "ChartTheme.CreateChartSurface(chart)");
    Contains(dashboardXaml, "ChartCardGrid");
    Contains(chartTheme, "new SolidColorPaint(ChartPalette.GridLine)");
    Contains(chartTheme, "new SolidColorPaint(ChartPalette.AxisText)");
    Contains(chartTheme, "CreateChartSurface");
    Contains(chartTheme, "ChartMeshOpacity");
    Contains(chartTheme, "Polyline");
    Contains(chartTheme, "Ellipse");
    Contains(chartTheme, "LegendTextPaint");
    NotContains(chartTheme, "DrawMarginFrame = new DrawMarginFrame");
});

Run("Overview KPI and status surfaces carry context instead of empty boxes", () =>
{
    var root = FindRepositoryRoot();
    var dashboardPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var kpiCard = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "KpiCard.xaml"));
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));

    Contains(kpiCard, "MetricContent");
    Contains(kpiCard, "Detail");
    Contains(dashboardPage, "Detail=\"Unique visitors\"");
    Contains(dashboardPage, "Detail=\"Google Search\"");
    Contains(dashboardPage, "Label=\"Bing Clicks\"");
    Contains(dashboardPage, "Label=\"Web Vitals\"");
    Contains(dashboardPage, "PageSpeed mobile");
    Contains(dashboardPage, "StatusValueTextBlockStyle");
    Contains(tokens, "StatusValueTextBlockStyle");
    NotContains(kpiCard, "AccentIndicator");
    NotContains(dashboardPage, "StatusValueBadgeBorderStyle");
});

Run("Overview summary KPIs honor selected domain", () =>
{
    var root = FindRepositoryRoot();
    var dashboardViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "DashboardViewModel.cs"));
    var summaryRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "SummaryRepository.cs"));

    Contains(dashboardViewModel, "GetSummaryAsync(domain, range.Days)");
    Contains(summaryRepository, "GetSummaryAsync(string domainOrAll, int days)");
    Contains(summaryRepository, "SummaryDomainWhereClause(domainOrAll, \"domain\")");
    Contains(summaryRepository, "SummaryDomainWhereClause(domainOrAll, \"site_url\")");
    Contains(summaryRepository, "SiteIdentity.NormalizeDomain(domainOrAll)");
});

Run("English UI uses English-facing numeric formatting", () =>
{
    var root = FindRepositoryRoot();
    var dashboardViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "DashboardViewModel.cs"));
    var kpiCardCode = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "KpiCard.xaml.cs"));

    Contains(dashboardViewModel, "EnglishCulture");
    Contains(dashboardViewModel, "CultureInfo.InvariantCulture");
    Contains(kpiCardCode, "CultureInfo.InvariantCulture");
    Contains(kpiCardCode, "vs previous period");
});

Run("Overview controls use user-facing period labels and polished health states", () =>
{
    var root = FindRepositoryRoot();
    var dashboardPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var dashboardCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml.cs"));
    var cloudflareCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml.cs"));
    var searchCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SearchConsolePage.xaml.cs"));
    var healthCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "HealthPage.xaml.cs"));
    var periodModel = File.ReadAllText(Path.Combine(root, "Numeris", "Models", "Period.cs"));
    var converters = File.ReadAllText(Path.Combine(root, "Numeris", "Converters", "NumberConverters.cs"));
    var healthPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "HealthPage.xaml"));

    Contains(dashboardPage, "LastSyncText");
    Contains(dashboardPage, "PageSpeed mobile");
    Contains(periodModel, "DisplayLabel");
    Contains(periodModel, "Last 7 days");
    Contains(dashboardCode, "PeriodOptions.All");
    Contains(cloudflareCode, "PeriodOptions.All");
    Contains(searchCode, "PeriodOptions.All");
    Contains(healthCode, "PeriodOptions.All");
    Contains(converters, "CultureInfo.InvariantCulture");
    Contains(healthPage, "PrimaryActionButtonStyle");
    NotContains(dashboardPage, "Text=\"Pending\"");
    NotContains(cloudflareCode, "new[] { Period.Last7Days");
    NotContains(searchCode, "new[] { Period.Last7Days");
    NotContains(healthCode, "new[] { Period.Last7Days");
    NotContains(healthPage, "AccentButtonStyle");
});

Run("Dashboard chart colors come from the shared visual palette", () =>
{
    var root = FindRepositoryRoot();
    var dashboardViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "DashboardViewModel.cs"));
    var palettePath = Path.Combine(root, "Numeris", "Themes", "ChartPalette.cs");

    if (!File.Exists(palettePath))
    {
        throw new InvalidOperationException("ChartPalette.cs is missing");
    }

    var palette = File.ReadAllText(palettePath);
    Contains(palette, "NumerisAccentColor");
    Contains(palette, "ChartSecondaryColor");
    Contains(palette, "ChartPanelColor");
    Contains(palette, "NumerisTextTertiaryColor");
    Contains(dashboardViewModel, "ChartPalette.Accent");
    Contains(dashboardViewModel, "ChartPalette.Secondary");
    NotContains(dashboardViewModel, "SKColor.Parse(\"#D9A24E\")");
    NotContains(dashboardViewModel, "SKColor.Parse(\"#C97B6A\")");
});

Run("Cloudflare page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var cloudflarePage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml"));
    var cloudflareCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml.cs"));
    var cloudflareViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "CloudflareViewModel.cs"));
    var horizontalBars = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "HorizontalBars.xaml.cs"));
    var projectFile = File.ReadAllText(Path.Combine(root, "Numeris", "Numeris.csproj"));
    var countryMapXamlPath = Path.Combine(root, "Numeris", "Controls", "CountryTrafficMap.xaml");
    var countryMapCodePath = Path.Combine(root, "Numeris", "Controls", "CountryTrafficMap.xaml.cs");
    var mapAssetsPath = Path.Combine(root, "Numeris", "Assets", "Maps");

    Contains(cloudflarePage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(cloudflarePage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(cloudflarePage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(cloudflarePage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(cloudflarePage, "x:Name=\"TrafficListsGrid\"");
    Contains(cloudflarePage, "x:Name=\"TopCountriesCard\"");
    Contains(cloudflarePage, "x:Name=\"TrafficCountryBars\"");
    Contains(cloudflarePage, "Items=\"{x:Bind ViewModel.TrafficCountries, Mode=OneWay}\"");
    Contains(cloudflarePage, "x:Name=\"TopPagesCard\"");
    Contains(cloudflareViewModel, "ChartPalette.Accent");
    Contains(cloudflareViewModel, "ChartPalette.Secondary");
    Contains(cloudflareViewModel, "ChartPalette.Success");
    Contains(horizontalBars, "HorizontalBarTrackBrush");
    Contains(horizontalBars, "NumerisTextTertiaryBrush");
    NotContains(cloudflarePage, "controls:CountryTrafficMap");
    NotContains(cloudflarePage, "x:Name=\"TopCountriesOverlay\"");
    NotContains(cloudflarePage, "Canvas.ZIndex");
    NotContains(cloudflarePage, "CountryMapButton_Click");
    NotContains(cloudflarePage, "CountryBarsButton_Click");
    NotContains(cloudflarePage, "TrafficCountryBars\"\r\n                                                         Margin=\"0,64,0,0\"\r\n                                                         Visibility=\"Collapsed\"");
    NotContains(cloudflareCode, "ShowCountryMap");
    NotContains(cloudflareCode, "TrafficCountryMap");
    NotContains(cloudflareCode, "CountryMapButton_Click");
    NotContains(cloudflareCode, "CountryBarsButton_Click");
    NotContains(projectFile, "Assets\\Maps\\");
    if (File.Exists(countryMapXamlPath) || File.Exists(countryMapCodePath))
    {
        throw new InvalidOperationException("CountryTrafficMap control should be removed; country traffic uses HorizontalBars only.");
    }
    if (Directory.Exists(mapAssetsPath))
    {
        throw new InvalidOperationException("Map assets directory should be removed; country traffic uses HorizontalBars only.");
    }
    NotContains(cloudflarePage, "Background=\"{StaticResource ControlSurfaceBrush}\"");
    NotContains(projectFile, "Assets\\Maps\\d3-geo.min.js");
    NotContains(projectFile, "Assets\\Maps\\d3-array.min.js");
    NotContains(cloudflareViewModel, "SKColor.Parse(\"#D9A24E\")");
    NotContains(cloudflareViewModel, "SKColor.Parse(\"#C97B6A\")");
});

Run("Cloudflare status codes are summarized into understandable HTTP groups", () =>
{
    var rows = new[]
    {
        new StatusCodeDay { Date = "2026-05-01", StatusCode = 200, Requests = 900 },
        new StatusCodeDay { Date = "2026-05-01", StatusCode = 304, Requests = 50 },
        new StatusCodeDay { Date = "2026-05-01", StatusCode = 404, Requests = 25 },
        new StatusCodeDay { Date = "2026-05-01", StatusCode = 522, Requests = 5 },
        new StatusCodeDay { Date = "2026-05-02", StatusCode = 200, Requests = 100 },
        new StatusCodeDay { Date = "2026-05-02", StatusCode = 301, Requests = 50 },
        new StatusCodeDay { Date = "2026-05-02", StatusCode = 499, Requests = 20 },
        new StatusCodeDay { Date = "2026-05-02", StatusCode = 530, Requests = 5 },
    };

    var insight = CloudflareViewModel.BuildStatusCodeInsight(rows);

    Equal("86.6%", insight.SuccessRateText);
    Equal("45 (3.9%)", insight.ClientErrorsText);
    Equal("10 (0.9%)", insight.ServerErrorsText);
    Equal("404 Not Found - 25 requests", insight.TopIssueText);
    Equal("2xx Success", insight.Groups[0].Label);
    Equal("3xx Redirects", insight.Groups[1].Label);
    Equal("4xx Client errors", insight.Groups[2].Label);
    Equal("5xx Server / edge errors", insight.Groups[3].Label);
    Equal("404", insight.TopCodes[0].Code);
    Equal("Not Found", insight.TopCodes[0].Description);
    Equal("2.2%", insight.TopCodes[0].ShareText);
});

Run("Cloudflare status codes tab explains numbers instead of exposing a raw code legend", () =>
{
    var root = FindRepositoryRoot();
    var cloudflarePage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml"));
    var cloudflareViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "CloudflareViewModel.cs"));
    var chartPalette = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "ChartPalette.cs"));

    Contains(cloudflarePage, "Text=\"HTTP responses by status group\"");
    Contains(cloudflarePage, "Text=\"Top status codes\"");
    Contains(cloudflarePage, "StatusTopCodes");
    Contains(cloudflarePage, "StatusSuccessRateText");
    Contains(cloudflarePage, "StatusTopIssueText");
    Contains(cloudflareViewModel, "BuildStatusCodeInsight");
    Contains(cloudflareViewModel, "StatusCodeGroup.Success");
    Contains(cloudflareViewModel, "StatusCodeDescription");
    Contains(chartPalette, "Warning");
    Contains(chartPalette, "Danger");
    Contains(chartPalette, "Info");
    NotContains(cloudflareViewModel, "Name = group.Key.ToString(CultureInfo.InvariantCulture)");
});

Run("Search page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var searchPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SearchConsolePage.xaml"));
    var searchViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "SearchConsoleViewModel.cs"));

    Contains(searchPage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource DataListViewStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource DataTableHeaderTextBlockStyle}\"");
    Contains(searchViewModel, "ChartPalette.Accent");
    Contains(searchViewModel, "ChartPalette.Secondary");
    NotContains(searchViewModel, "SKColor.Parse(\"#D9A24E\")");
    NotContains(searchViewModel, "SKColor.Parse(\"#C97B6A\")");
});

Run("Bing page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var bingPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "BingPage.xaml"));
    var bingPageCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "BingPage.xaml.cs"));
    var bingViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "BingViewModel.cs"));

    Contains(bingPage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(bingPage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(bingPage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(bingPage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(bingPage, "Style=\"{StaticResource DataListViewStyle}\"");
    Contains(bingPageCode, "ChartTheme.CreateCartesianChart()");
    Contains(bingPageCode, "ChartTheme.CreateChartSurface(chart)");
    Contains(bingViewModel, "ChartPalette.Accent");
    Contains(bingViewModel, "ChartPalette.Secondary");
    NotContains(bingViewModel, "SKColor.Parse(\"#D9A24E\")");
});

Run("Performance page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var performancePage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "PerformancePage.xaml"));
    var performancePageCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "PerformancePage.xaml.cs"));
    var performanceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "PerformanceViewModel.cs"));

    Contains(performancePage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(performancePage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(performancePage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(performancePage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(performancePage, "Style=\"{StaticResource DataListViewStyle}\"");
    Contains(performancePageCode, "ChartTheme.CreateCartesianChart()");
    Contains(performancePageCode, "ChartTheme.CreateChartSurface(chart)");
    Contains(performanceViewModel, "ChartPalette.Accent");
    Contains(performanceViewModel, "ChartPalette.Secondary");
    NotContains(performanceViewModel, "SKColor.Parse(\"#D9A24E\")");
});

Run("Health page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var healthPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "HealthPage.xaml"));
    var healthViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "HealthViewModel.cs"));

    Contains(healthPage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource DataListViewStyle}\"");
    Contains(healthPage, "HorizontalScrollBarVisibility=\"Disabled\"");
    Contains(healthPage, "Margin=\"{StaticResource PageHeaderMargin}\"");
    Contains(healthPage, "Padding=\"{StaticResource PageScrollContentPadding}\"");
    Contains(healthPage, "UptimeOverviewText");
    Contains(healthPage, "SitemapLastUpdatedText");
    Contains(healthPage, "Last seen");
    Contains(healthPage, "IsoDateTimeDisplayConverter");
    Contains(healthViewModel, "ChartPalette.Accent");
    Contains(healthViewModel, "ChartPalette.Secondary");
    Contains(healthViewModel, "BuildResponseAxisMax");
    Contains(healthViewModel, "SitemapLastUpdatedText");
    NotContains(healthViewModel, "SKColor.Parse(\"#D9A24E\")");
    NotContains(healthViewModel, "SKColor.Parse(\"#C97B6A\")");
});

Run("Sources page presents integrations as shared settings cards", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));

    Contains(sourcesPage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(sourcesPage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(sourcesPage, "HorizontalScrollBarVisibility=\"Disabled\"");
    NotContains(sourcesPage, "MaxWidth=\"980\"");
    Contains(sourcesPage, "<Style TargetType=\"Expander\">");
    Contains(sourcesPage, "<Setter Property=\"HorizontalAlignment\" Value=\"Stretch\" />");
    Contains(sourcesPage, "<Setter Property=\"HorizontalContentAlignment\" Value=\"Stretch\" />");
    Contains(sourcesPage, "Traffic, cache, security events");
    Contains(sourcesPage, "Clicks, impressions, pages, indexing");
    Contains(sourcesPage, "Core Web Vitals and PageSpeed lab data");
    Contains(sourcesPage, "Search visibility and indexing data");
    Contains(sourcesPage, "StatusMessage");
    NotContains(sourcesPage, "Without configuration, mock data is used");
});

Run("Sources page groups settings by provider", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));

    Equal("3", CountOccurrences(sourcesPage, "Style=\"{StaticResource ContentCardBorderStyle}\"").ToString());
    Contains(sourcesPage, "Text=\"Cloudflare\"");
    Contains(sourcesPage, "Text=\"Google\"");
    Contains(sourcesPage, "Text=\"Bing\"");
    Contains(sourcesPage, "Zone Analytics");
    Contains(sourcesPage, "Web Analytics");
    Contains(sourcesPage, "Search Console");
    Contains(sourcesPage, "Web Performance");
    Contains(sourcesPage, "Bing Webmaster");
    NotContains(sourcesPage, "Text=\"Cloudflare Web Analytics\"");
    NotContains(sourcesPage, "Text=\"Google Search Console\"");
    NotContains(sourcesPage, "Text=\"Microsoft Bing Webmaster Tools\"");
});

Run("Sources page hides credential forms behind collapsed editors", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));

    Contains(sourcesPage, "Header=\"Add Cloudflare Zone Analytics connection\"");
    Contains(sourcesPage, "Header=\"Edit Cloudflare Web Analytics credentials\"");
    Contains(sourcesPage, "Header=\"Edit Google Search Console credentials\"");
    Contains(sourcesPage, "Header=\"Edit Google Web Performance API keys\"");
    Contains(sourcesPage, "Header=\"Edit Bing Webmaster API key\"");
    Equal("6", CountOccurrences(sourcesPage, "IsExpanded=\"False\"").ToString());
    Contains(sourcesPage, "Content=\"Test CrUX\"");
    Contains(sourcesPage, "Content=\"Test PageSpeed\"");
    Contains(sourcesPage, "Header=\"Advanced: override YouTube OAuth credentials\"");
    Contains(sourcesPage, "Content=\"Sync\"");
    Contains(sourcesPage, "Content=\"Delete\"");
    Contains(sourcesPage, "API key saved");
    Contains(sourcesPage, "OAuth credentials saved");
});

Run("Sources page provider headers reuse navigation brand icons", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));

    Contains(sourcesPage, "Source=\"ms-appx:///Assets/Icons/cloudflare-filled.png\"");
    Contains(sourcesPage, "Source=\"ms-appx:///Assets/Icons/google-search-filled.png\"");
    Contains(sourcesPage, "Source=\"ms-appx:///Assets/Icons/bing-filled.png\"");
    Contains(sourcesPage, "<ImageIcon Grid.Column=\"0\"");
    Contains(sourcesPage, "Height=\"20\"");
    NotContains(sourcesPage, "<FontIcon Grid.Column=\"0\"");
});

Run("UI improvement implementation plan is saved at repository root", () =>
{
    var root = FindRepositoryRoot();
    var planPath = Path.Combine(root, "UI-IMPROVEMENT-IMPLEMENTATION-PLAN.md");
    if (!File.Exists(planPath))
    {
        throw new InvalidOperationException("UI improvement implementation plan is missing from repository root");
    }

    var plan = File.ReadAllText(planPath);
    Contains(plan, "SelectorBar");
    Contains(plan, "AutomationProperties");
    Contains(plan, "VisualStateManager");
    Contains(plan, "InfoBar");
    Contains(plan, "DataTable");
    Contains(plan, "Sources");
});

Run("Report pages use SelectorBar for local view switching", () =>
{
    var root = FindRepositoryRoot();
    var pages = new[]
    {
        "CloudflarePage.xaml",
        "SearchConsolePage.xaml",
        "BingPage.xaml",
        "PerformancePage.xaml",
        "HealthPage.xaml",
        "YouTubePage.xaml",
    };

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "<SelectorBar");
        Contains(xaml, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
        NotContains(xaml, "<NavigationView Grid.Row=\"1\"");
    }

    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    Contains(tokens, "TopTabSelectorBarStyle");
    NotContains(tokens, "TopTabNavigationViewStyle");
});

Run("Report toolbars expose automation names and loading guards", () =>
{
    var root = FindRepositoryRoot();
    var pages = new[]
    {
        "DashboardPage.xaml",
        "CloudflarePage.xaml",
        "SearchConsolePage.xaml",
        "BingPage.xaml",
        "PerformancePage.xaml",
        "HealthPage.xaml",
        "YouTubePage.xaml",
    };

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "AutomationProperties.Name=\"Period\"");
        Contains(xaml, "AutomationProperties.Name=\"Refresh current report\"");
        Contains(xaml, "IsEnabled=\"{x:Bind ViewModel.CanRefresh, Mode=OneWay}\"");
    }

    foreach (var page in pages.Where(page => page != "YouTubePage.xaml"))
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "AutomationProperties.Name=\"Domain\"");
    }
});

Run("Report pages define responsive visual states", () =>
{
    var root = FindRepositoryRoot();
    var pages = new[]
    {
        "DashboardPage.xaml",
        "CloudflarePage.xaml",
        "SearchConsolePage.xaml",
        "BingPage.xaml",
        "PerformancePage.xaml",
        "HealthPage.xaml",
        "YouTubePage.xaml",
    };

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "<VisualStateManager.VisualStateGroups>");
        Contains(xaml, "x:Name=\"NarrowLayout\"");
        Contains(xaml, "x:Name=\"WideLayout\"");
    }
});

Run("Sources page exposes status bars and guarded actions", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));
    var sourceViewModels = Directory.EnumerateFiles(Path.Combine(root, "Numeris", "ViewModels", "Sources"), "*SourceViewModel.cs")
        .Select(File.ReadAllText)
        .ToArray();

    Contains(sourcesPage, "<InfoBar");
    Contains(sourcesPage, "AutomationProperties.Name=\"Cloudflare status\"");
    Contains(sourcesPage, "AutomationProperties.Name=\"Google Search Console status\"");
    Contains(sourcesPage, "AutomationProperties.Name=\"Web Performance status\"");
    Contains(sourcesPage, "AutomationProperties.Name=\"YouTube status\"");
    Contains(sourcesPage, "AutomationProperties.Name=\"Bing status\"");
    Contains(sourcesPage, "Style=\"{StaticResource DangerActionButtonStyle}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.Cloudflare.CanRun, Mode=OneWay}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.WebAnalytics.CanRun, Mode=OneWay}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.SearchConsole.CanRun, Mode=OneWay}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.Performance.CanRun, Mode=OneWay}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.YouTube.CanRun, Mode=OneWay}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.Bing.CanRun, Mode=OneWay}\"");

    foreach (var viewModel in sourceViewModels)
    {
        Contains(viewModel, "public bool CanRun => !IsBusy;");
        Contains(viewModel, "OnPropertyChanged(nameof(CanRun))");
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

static int CountOccurrences(string text, string value)
{
    var count = 0;
    var start = 0;
    while (start < text.Length)
    {
        var index = text.IndexOf(value, start, StringComparison.Ordinal);
        if (index < 0)
        {
            return count;
        }

        count++;
        start = index + value.Length;
    }

    return count;
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
