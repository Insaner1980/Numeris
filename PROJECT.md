# Numeris Project Description

Last verified from code: 2026-05-15.

This document describes the current Numeris codebase as it exists in this repository. It is intended to be precise enough for code review questions, architecture checks, and regression review. When this document conflicts with the code, the code is the source of truth.

## Executive Summary

Numeris is a native Windows desktop analytics application built with WinUI 3 and the Windows App SDK. The app collects, stores, and visualizes website, search, performance, video, and health data for Finnvek-owned properties, currently centered on `knittoolsapp.com` and `finnvek.com`.

The application is not a web app and not a Tauri app. It is the native successor/import target for the old Numeris Tauri application. At startup, Numeris initializes a local SQLite database, runs schema migrations, attempts a cautious legacy Numeris import, and opens a WinUI shell with report pages and a Sources configuration page.

The core design is a layered desktop architecture:

- WinUI pages render the user interface and wire UI events.
- ViewModels own UI state, loading state, chart series, filtering, sorting, and report refresh behavior.
- Sync services orchestrate credential lookup, remote API calls, date windows, and repository writes.
- API clients call external provider endpoints and sanitize upstream failures.
- Repositories are the only layer that performs SQLite reads and writes.
- `SqliteDatabase` owns the single open SQLite connection, serialization gate, migrations, and transaction helper.
- `CredentialVault` owns all API keys, access secrets, refresh tokens, and client secrets through Windows PasswordVault.

## Repository Layout

- `Numeris.slnx` is the solution file. It contains the WinUI app project and the custom test harness project.
- `Numeris/` contains the production WinUI application.
- `Numeris.Tests/` contains a console-based architectural and behavioral test harness in `Program.cs`.
- `tools/lint-check.ps1` and `tools/security-check.ps1` write diagnostic reports under `reports/`.
- `memory/MEMORY.md` and `AGENTS.md` contain project memory and agent instructions.
- `migration-plan.md`, `BING-WEBMASTER-SYNC.md`, and the root implementation-plan documents capture planning history and constraints.
- `reports/` is ignored and must not be committed.

## Technology Stack

The production app is `Numeris/Numeris.csproj`.

- Language/runtime: C# on .NET 8, targeting `net8.0-windows10.0.19041.0`.
- Minimum target platform: Windows 10 build `10.0.17763.0`.
- UI framework: WinUI 3 through Windows App SDK.
- Windows App SDK package: `Microsoft.WindowsAppSDK` version `2.0.1`.
- Windows SDK build tools: `Microsoft.Windows.SDK.BuildTools` version `10.0.28000.1839`.
- MVVM helper package: `CommunityToolkit.Mvvm` version `8.4.0`.
- WinUI settings controls package: `CommunityToolkit.WinUI.Controls.SettingsControls` version `8.2.251219`.
- Dependency injection: `Microsoft.Extensions.DependencyInjection` version `8.0.1`.
- SQLite provider: `Microsoft.Data.Sqlite` version `8.0.10`.
- SQL mapping/helper library: `Dapper` version `2.1.66`.
- Charts: `LiveChartsCore.SkiaSharpView.WinUI` version `2.0.2`.
- Nullable reference types are enabled.
- `LangVersion` is `preview`, used by the CommunityToolkit.Mvvm partial-property model.

The app supports x86, x64, and ARM64 runtime identifiers. The solution configuration currently maps the app to x64.

## Packaging and Launch Model

The project is configured as a WinUI desktop executable:

- `OutputType` is `WinExe`.
- `UseWinUI` is `true`.
- `WindowsPackageType` is `None`, which enables unpackaged project execution through Windows App SDK auto-initialization.
- `EnableMsixTooling` is `true`, and `Package.appxmanifest` plus publish profiles are present for packaged publishing workflows.
- `launchSettings.json` exposes both `Numeris (Package)` using `MsixPackage` and `Numeris (Unpackaged)` using `Project`.
- Publish settings enable ReadyToRun and trimming outside Debug builds.

The code uses Windows APIs directly, including `Windows.Storage.ApplicationData`, `Windows.Security.Credentials.PasswordVault`, WinUI windowing APIs, and WinUI/XAML resources.

## Application Startup

Startup is defined in `Numeris/App.xaml.cs`.

1. `App` constructs the dependency injection container in its constructor.
2. `OnLaunched` resolves `SqliteDatabase`, which opens/creates the local SQLite database and runs migrations.
3. `OnLaunched` resolves `LegacyDataMigrationService` and synchronously calls `ImportAllAsync()`.
4. `OnLaunched` resolves `MainWindow` from DI and activates it.

There is no startup mock-data seeding path. The test harness explicitly checks that `MockSeeder`, `SeedAsync`, and a `Services/MockData` directory are not present.

## Dependency Injection Composition

`App.ConfigureServices()` registers the application graph.

Singletons:

- `SqliteDatabase`
- `LegacyDataMigrationService`
- `SettingsStore`
- all repositories
- all API clients
- Google OAuth services
- `CredentialVault`
- all sync services
- `ShellViewModel`
- `MainWindow`

Transient ViewModels:

- `DashboardViewModel`
- `CloudflareViewModel`
- `SearchConsoleViewModel`
- `BingViewModel`
- `PerformanceViewModel`
- `YouTubeViewModel`
- `HealthViewModel`
- each source-specific ViewModel
- `SourcesViewModel`

Transient pages:

- `DashboardPage`
- `CloudflarePage`
- `SearchConsolePage`
- `BingPage`
- `PerformancePage`
- `YouTubePage`
- `HealthPage`
- `SourcesPage`

The app uses the static `App.Services` provider to resolve ViewModels and shared shell state inside pages.

## Shell and Navigation

`MainWindow.xaml` defines the desktop shell.

- The window title is `Numeris`.
- The shell uses `<MicaBackdrop />` as the Windows system backdrop.
- A global WebP bitmap background is decoded from `Assets/AppBackdrop.webp`.
- A scrim from `AppBackdropScrimBrush` darkens the bitmap for readability.
- Navigation is a left `NavigationView` with an always-open pane.
- The pane toggle and back button are disabled.
- The title bar is custom: `ExtendsContentIntoTitleBar = true` and `SetTitleBar(AppTitleBar)`.
- Title bar colors are read from design-token resources.

Navigation routes are centralized in `MainWindow.xaml.cs`:

- `dashboard` -> `DashboardPage`
- `cloudflare` -> `CloudflarePage`
- `search` -> `SearchConsolePage`
- `bing` -> `BingPage`
- `performance` -> `PerformancePage`
- `youtube` -> `YouTubePage`
- `health` -> `HealthPage`
- `sources` -> `SourcesPage`

`ShellViewModel` persists three shell-level settings:

- selected period
- selected domain
- last selected page

The known domain choices are exactly:

- `all`
- `knittoolsapp.com`
- `finnvek.com`

The known period choices are:

- Last 7 days
- Last 30 days
- Last 90 days
- All time

`Period.All` is implemented as 365 days, not an unbounded all-time query.

## Persistent Local Files

`AppPaths` chooses storage paths depending on packaged state.

For packaged execution:

- data lives in `ApplicationData.Current.LocalFolder`.

For unpackaged execution:

- data lives in `%LocalAppData%\Numeris`.

Derived paths:

- SQLite database: `numeris.db`
- shell settings JSON: `settings.json`
- logs directory: `logs`

The code creates the base data directory automatically.

## Settings Persistence

`SettingsStore` reads and writes a JSON file at `AppPaths.SettingsPath`.

Stored shell settings:

- `SelectedPeriod`
- `SelectedDomain`
- `LastPage`

Invalid persisted domains fall back to `all`. Invalid persisted page tags fall back to `dashboard`.

## SQLite Architecture

`SqliteDatabase` owns one open `SqliteConnection` for the app lifetime.

Important behavior:

- database file is opened with `ReadWriteCreate`
- shared cache is enabled
- foreign keys are enabled in the connection string
- migrations run immediately after opening
- a `SemaphoreSlim` serializes reads and writes through one gate
- `ReadAsync`, `WriteAsync`, `WriteAsync<T>`, and `WriteTransactionAsync` are the supported access helpers
- multi-row logical writes are expected to use `WriteTransactionAsync`
- `ClearAllData()` deletes report data and resets connection statuses but leaves the schema in place

Sync services must not write SQLite directly. Tests enforce that sync services do not depend on `SqliteDatabase`.

## Schema Versioning

`Migrations.CurrentSchemaVersion` is `5`.

Versioning is tracked in both:

- `PRAGMA user_version`
- `meta.schema_version`

`Migrations.RunAll()` performs:

1. WAL mode and foreign-key setup.
2. creation of the current full schema through `CREATE TABLE IF NOT EXISTS`
3. pending version migrations
4. insertion of default connection rows and default URL/site rows

Migration highlights:

- v2 preserves dated Bing page stats by adding a `date` component to the `bing_page_stats` primary key.
- v3 removes earlier mock/demo analytics data and converts `mock` connection status to `disconnected`.
- v4 adds the YouTube connection row.
- v5 adds Cloudflare page breakdown storage.

## Database Tables

Cloudflare Zone Analytics:

- `cloudflare_traffic`
- `cloudflare_countries`
- `cloudflare_pages`
- `cloudflare_status_codes`

Google Search Console:

- `search_console`
- `search_devices`
- `search_page_queries`
- `sitemap_urls`

Legacy/unused Play Store analytics schema currently exists but has no active UI or sync service in the current app:

- `play_installs`
- `play_ratings`
- `play_revenue`
- `play_crashes`

Connection registry:

- `connections`

Cloudflare Web Analytics:

- `web_analytics_sites`
- `web_analytics_daily`
- `web_analytics_referrers`
- `web_analytics_pages`
- `web_analytics_countries`

Web performance:

- `performance_urls`
- `crux_metric_points`
- `pagespeed_runs`
- `pagespeed_audits`

Bing Webmaster:

- `bing_sites`
- `bing_rank_traffic`
- `bing_query_stats`
- `bing_page_stats`
- `bing_raw_items`

YouTube:

- `youtube_channels`
- `youtube_videos`
- `youtube_daily`
- `youtube_video_stats`
- `youtube_countries`
- `youtube_traffic_sources`
- `youtube_devices`
- `youtube_retention_points`

Health:

- `uptime_checks`

Metadata:

- `meta`

## Default Seeded Registry Data

The app inserts default disconnected registry rows:

- `cf` / `cloudflare`
- `sc` / `search_console`
- `ps` / `play_store`
- `wa` / `web_analytics`
- `perf` / `performance`
- `crux` / `crux`
- `pagespeed` / `pagespeed`
- `bing` / `bing_webmaster`
- `youtube` / `youtube`

The app also inserts default performance URLs:

- `https://finnvek.com/`
- `https://knittoolsapp.com/`

The app also inserts default Bing sites:

- `https://finnvek.com/`
- `https://knittoolsapp.com/`

These defaults are real configuration targets, not mock metrics.

## Secrets and Credentials

All active secrets belong in `CredentialVault`, which wraps Windows `PasswordVault`.

SQLite `connections.config` stores metadata only. It must not store new plain text secrets.

Credential resources:

- Cloudflare Zone Analytics token: `Numeris.Cloudflare`
- Cloudflare Web Analytics token: `Numeris.WebAnalytics`
- Search Console client secret: `Numeris.SearchConsole.ClientSecret`
- Search Console refresh token: `Numeris.SearchConsole.RefreshToken`
- CrUX API key: `Numeris.Crux.ApiKey`
- PageSpeed API key: `Numeris.PageSpeed.ApiKey`
- Bing Webmaster API key: `Numeris.BingWebmaster.ApiKey`
- YouTube client secret: `Numeris.YouTube.ClientSecret`
- YouTube refresh token: `Numeris.YouTube.RefreshToken`

Cloudflare tokens are stored per normalized domain. Web Analytics tokens are stored per account ID. Google OAuth client secrets and refresh tokens are stored per client ID. CrUX, PageSpeed, and Bing keys use the `default` user key.

Deleting a credential removes the PasswordVault entry. It does not write an empty password.

## Legacy Numeris Import

`LegacyDataMigrationService` imports connection configuration from the old Numeris Tauri app.

Legacy source:

- app data folder: `%AppData%\com.finnvek.numeris`
- database file: `numeris.db`
- legacy credential service suffix: `Numeris`
- import tag written into config: `legacy-numeris-tauri`

Imported integrations:

- Cloudflare Zone Analytics connections
- Cloudflare Web Analytics account and site tags
- Google Search Console OAuth configuration

The importer:

- skips if the legacy database is missing
- reads legacy SQLite in read-only mode
- preserves existing Numeris secrets if they already exist
- reads old keyring values from Windows Credential Manager when available
- falls back to legacy plain text config only when no Numeris secret exists
- normalizes Cloudflare bearer tokens by removing a leading `Bearer `
- converts missing or `mock` legacy status to `configured`
- does not delete legacy credentials

Legacy credential target names include values such as `cloudflare:example.com.Numeris`.

## Domain and URL Normalization

`SiteIdentity` is the central normalization type for domains, origins, and home-page URLs.

It accepts:

- bare domains
- absolute `http://` URLs
- absolute `https://` URLs

It rejects:

- empty input
- non-HTTP schemes
- URLs without hosts
- bare domain input containing path separators or ports
- invalid host names

Normalized outputs:

- `Domain`: lowercase bare host
- `OriginUrl`: `https://{domain}`
- `HomePageUrl`: `https://{domain}/`

`PerformanceUrl` remains the PageSpeed-facing facade but delegates normalization to `SiteIdentity`.

## Raw JSON Storage Policy

`RawJsonStoragePolicy` centralizes storage rules for raw upstream payloads.

Limits:

- raw JSON max: 200,000 characters
- PageSpeed audit details max: 40,000 characters
- PageSpeed retention: 30 runs per URL and strategy
- Bing raw retention: 180 days

Before raw JSON is stored, the policy redacts:

- tokens
- secrets
- passwords
- credentials
- authorization values
- API keys and similar key names
- query-string secrets
- bearer tokens

If JSON exceeds the configured size, it is replaced by a bounded JSON object containing truncation metadata and a prefix.

## API Error Handling

External API failures should use `ApiRequestException` and `ApiErrorMessage`.

`ApiRequestException` records:

- provider
- operation
- HTTP status code
- optional upstream message

It exposes convenience flags:

- `IsNotFound`
- `IsRateLimited`
- `IsTransient`

`ApiErrorMessage`:

- extracts concise messages from JSON error bodies
- handles OAuth errors such as `invalid_grant`, `invalid_client`, and `deleted_client`
- omits non-JSON upstream bodies
- redacts query-string secrets and bearer tokens
- collapses whitespace
- limits user-facing messages to 300 characters

API clients and ViewModels should not show raw response bodies, request URLs containing keys, or bearer values.

## External Integrations

### Cloudflare Zone Analytics

Main files:

- `CloudflareGraphqlClient`
- `CloudflareSyncService`
- `CloudflareRepository`
- `CloudflareViewModel`
- `CloudflarePage`
- `CloudflareSourceViewModel`

API base:

- REST zone validation: `https://api.cloudflare.com/client/v4`
- GraphQL analytics: `https://api.cloudflare.com/client/v4/graphql`

Stored data:

- daily pageviews, visitors, requests, cache, bandwidth, threats
- daily status codes
- daily country visitor breakdown
- daily path/request breakdown

Sync flow:

1. resolve Cloudflare connection metadata from `connections`
2. load the token for the normalized domain from `CredentialVault`
3. call Cloudflare GraphQL for the selected date window
4. upsert daily traffic, countries, pages, and status codes in a single repository transaction
5. update the matching Cloudflare connection `last_sync`

Cloudflare report tabs:

- Traffic
- Cache
- Security
- Status
- Web Analytics

### Cloudflare Web Analytics

Main files:

- `CloudflareRumClient`
- `WebAnalyticsSyncService`
- `WebAnalyticsRepository`
- `CloudflareViewModel`
- `CloudflarePage`
- `WebAnalyticsSourceViewModel`

API base:

- `https://api.cloudflare.com/client/v4`
- `https://api.cloudflare.com/client/v4/graphql`

Stored data:

- configured site tag mappings
- daily visits and page views
- referrers
- pages
- countries

The Sources page can discover Cloudflare Web Analytics sites for an account, but manual domain-to-site-tag mappings are also supported. Sync requires at least one saved site tag mapping.

### Google Search Console

Main files:

- `GoogleOAuthClient`
- `GoogleOAuthFlow`
- `SearchConsoleClient`
- `SearchConsoleSyncService`
- `SearchConsoleRepository`
- `SitemapRepository`
- `SearchConsoleViewModel`
- `SearchConsolePage`
- `SearchConsoleSourceViewModel`

API endpoints:

- OAuth authorize: `https://accounts.google.com/o/oauth2/v2/auth`
- OAuth token: `https://oauth2.googleapis.com/token`
- sites list: `https://www.googleapis.com/webmasters/v3/sites`
- URL Inspection: `https://searchconsole.googleapis.com/v1/urlInspection/index:inspect`

OAuth scope:

- `https://www.googleapis.com/auth/webmasters.readonly`

Search Console sync is currently hard-coded to sync these domains:

- `knittoolsapp.com`
- `finnvek.com`

For each available Google property, the sync service pulls:

- daily search metrics
- query metrics
- page metrics
- country metrics
- device metrics
- page-query mappings for a 28-day window

Search report tabs:

- Overview
- Queries
- Pages
- Devices
- Indexing

The Indexing tab uses the URL Inspection API against active sitemap URLs. On the Indexing tab, refresh first runs the normal Search Console sync, reloads local report data, and then runs URL inspection.

### CrUX and PageSpeed

Main files:

- `CruxClient`
- `PageSpeedClient`
- `PerformanceSyncService`
- `PerformanceRepository`
- `PerformanceViewModel`
- `PerformancePage`
- `PerformanceSourceViewModel`

API endpoints:

- CrUX history: `https://chromeuxreport.googleapis.com/v1/records:queryHistoryRecord`
- PageSpeed: `https://pagespeedonline.googleapis.com/pagespeedonline/v5/runPagespeed`

CrUX sync targets:

- each distinct origin from enabled `performance_urls`
- each enabled URL
- form factors `ALL`, `PHONE`, `DESKTOP`, and `TABLET`

PageSpeed sync targets:

- each enabled URL
- strategies `MOBILE` and `DESKTOP`

PageSpeed sync is serialized through a semaphore, spaces requests by one second, and retries transient/rate-limited/network/timeout failures up to three attempts with exponential backoff.

Performance report tabs:

- Overview
- CrUX
- PageSpeed
- URLs

CrUX `404 NotFound` is treated as missing field data rather than API-key failure.

### Bing Webmaster

Main files:

- `BingWebmasterClient`
- `BingWebmasterSyncService`
- `BingRepository`
- `BingViewModel`
- `BingPage`
- `BingSourceViewModel`

API endpoint:

- `https://ssl.bing.com/webmaster/api.svc/json/`

Active sync methods:

- `GetUserSites`
- `GetCrawlIssues`
- `GetCrawlStats`
- `GetRankAndTrafficStats`
- `GetQueryStats`
- `GetPageStats`

Stored data:

- enabled Bing site URLs
- dated traffic rows
- dated query rows
- dated page rows
- raw method items, including sanitized error records

Bing report tabs:

- Overview
- Queries
- Pages
- Crawl

The project intentionally avoids mass-adding every Bing API detail method. New methods should be checked against `BING-WEBMASTER-SYNC.md`.

### YouTube

Main files:

- `YouTubeDataClient`
- `YouTubeAnalyticsClient`
- `YouTubeSyncService`
- `YouTubeRepository`
- `YouTubeViewModel`
- `YouTubePage`
- `YouTubeSourceViewModel`

API endpoints:

- channels: `https://www.googleapis.com/youtube/v3/channels`
- playlist items: `https://www.googleapis.com/youtube/v3/playlistItems`
- videos: `https://www.googleapis.com/youtube/v3/videos`
- analytics reports: `https://youtubeanalytics.googleapis.com/v2/reports`

OAuth scopes:

- `https://www.googleapis.com/auth/youtube.readonly`
- `https://www.googleapis.com/auth/yt-analytics.readonly`

YouTube sync:

1. refreshes access using a YouTube refresh token and client secret
2. falls back to the Search Console client secret for the same client ID when a YouTube-specific client secret is absent
3. fetches the authenticated user's channel
4. lists upload playlist video IDs
5. fetches public video metadata and public counters
6. fetches channel daily analytics
7. fetches top video analytics
8. fetches country, traffic source, and device breakdowns
9. attempts retention data for up to 25 selected videos
10. writes channel/video metadata and analytics through repository methods

YouTube report tabs:

- Overview
- Videos
- Geography
- Traffic
- Devices
- Retention

The app models one authenticated channel. It does not currently model multiple YouTube channels or app/domain separation for YouTube.

### Health, Uptime, and Sitemaps

Main files:

- `UptimeClient`
- `SitemapClient`
- `HealthRepository`
- `SitemapRepository`
- `HealthViewModel`
- `HealthPage`

Uptime:

- probes `https://{normalized-domain}`
- uses HTTP status and response time
- stores rows in `uptime_checks`

Sitemaps:

- discovers URLs from `https://{domain}/sitemap-index.xml`
- parses sitemap index and URL-set XML using the sitemap namespace
- stores sitemap URLs in `sitemap_urls`
- inactive/removed URLs can be represented through `removed_at`

Health report tabs:

- Uptime
- Sitemap

## Google OAuth Design

Google OAuth is centralized in `GoogleOAuthClient` and `GoogleOAuthFlow`.

`GoogleOAuthClient` builds authorization URLs, exchanges authorization codes, and refreshes access tokens.

`GoogleOAuthFlow`:

- starts a loopback `TcpListener` on `127.0.0.1` with an ephemeral port
- builds a redirect URI ending in `/oauth2callback`
- generates a state value
- opens the browser through a delegate supplied by the caller
- waits up to two minutes by default
- verifies returned OAuth state
- returns `OAuthTokens`
- sends a small local HTML completion response to the browser

Search Console and YouTube both use this shared OAuth path.

## ViewModel Responsibilities

Report ViewModels:

- subscribe to `ShellViewModel.PropertyChanged`
- reload when selected domain or period changes
- expose `CanRefresh` to disable refresh while busy
- read reporting data only through repositories
- call `SyncConfiguredAsync` on refresh before reloading repository data
- build LiveCharts series and axes from repository rows
- use `ChartPalette` and `ChartTheme` instead of hard-coded chart colors

Source ViewModels:

- own provider-specific configuration form state
- expose `CanRun => !IsBusy`
- expose `HasStatusMessage`
- save secrets to `CredentialVault`
- save non-secret config to `ConnectionsRepository`
- test saved connections through sync services or API clients
- delete connection metadata and matching secrets

`SourcesViewModel` is only a coordinator. It delegates actual provider behavior to:

- `CloudflareSourceViewModel`
- `WebAnalyticsSourceViewModel`
- `SearchConsoleSourceViewModel`
- `PerformanceSourceViewModel`
- `YouTubeSourceViewModel`
- `BingSourceViewModel`

Tests explicitly prevent `SourcesViewModel` from depending on low-level clients or the vault.

## Report Pages

All report pages follow the same UI pattern:

- a header using shared page-title styles
- a domain selector where applicable
- a period selector
- a refresh button bound to `ViewModel.CanRefresh`
- `AutomationProperties.Name` for domain, period, and refresh controls
- local view switching through `SelectorBar`
- responsive `VisualStateManager` states for narrow and wide layouts
- charts created in code-behind through `ChartTheme.CreateCartesianChart()`
- chart surfaces wrapped through `ChartTheme.CreateChartSurface(chart)`

The report pages using `SelectorBar` are:

- Cloudflare
- Google Search
- Bing
- Performance
- Health
- YouTube

Nested `NavigationView` controls are not used for report-local tabs.

## Sources Page

`SourcesPage` is a configuration page, not a reporting page and not a live-sync launcher.

Provider groups shown on the page:

- Cloudflare
- Google
- YouTube
- Bing

Provider sections include:

- Cloudflare Zone Analytics
- Cloudflare Web Analytics
- Google Search Console
- Google Web Performance API keys
- YouTube Analytics
- Bing Webmaster

The page uses:

- shared card surfaces
- provider icons reused from navigation
- `InfoBar` status surfaces
- collapsed `Expander` editors for credential forms
- guarded actions through `CanRun`
- `DangerActionButtonStyle` for delete actions

The page intentionally does not expose per-integration `Sync` buttons. Live refresh belongs on report pages.

## Design System

The central design-token file is `Numeris/Themes/Tokens.xaml`.

Token groups include:

- spacing
- page padding
- card padding
- title bar dimensions
- toolbar dimensions
- chart sizes and mesh colors
- text colors
- surface colors
- status colors
- shared styles for headers, KPI cards, chart cards, content cards, status strips, buttons, data tables, InfoBars, and ListViews

The visible shell background is:

- `AppBackgroundBrush` fallback color
- global `AppBackdrop.webp` bitmap
- `AppBackdropScrimBrush` overlay
- transparent NavigationView pane/content layers

Cards and charts use dark translucent black-glass surfaces:

- `CardSurfaceColor`
- `ControlSurfaceColor`
- `ChartPanelColor`
- `ContentLayerColor`

Navigation and content layer colors are transparent so the global shell backdrop remains continuous.

## Charting

Chart construction is centralized in `ChartTheme`.

`ChartTheme.CreateCartesianChart()` sets:

- transparent chart background
- bottom legend
- tooltip position
- legend/tooltip paints from `ChartPalette`
- default text sizes

`ChartTheme.CreateChartSurface()` wraps a chart with a subtle mesh backdrop.

`ChartTheme.StyleXAxis()` and `ChartTheme.StyleYAxis()` centralize axis label and separator styling.

ViewModels build the data series, but chart surfaces and axes should use the central helpers.

## Repositories

Repositories are the persistence boundary.

Important repositories:

- `SummaryRepository` builds overview aggregates.
- `InsightMetricsRepository` builds deterministic Overview Insights metric windows from existing local SQLite rows.
- `CloudflareRepository` reads/writes Cloudflare traffic, cache, security, status, countries, and pages.
- `SearchConsoleRepository` reads/writes Search Console daily, query, page, country, device, and page-query rows.
- `SitemapRepository` reads sitemap URLs and writes URL Inspection results.
- `WebAnalyticsRepository` reads/writes Cloudflare Web Analytics daily and breakdown rows.
- `PerformanceRepository` reads/writes CrUX and PageSpeed data, URLs, audit issues, trends, and retention.
- `BingRepository` reads/writes Bing sites, traffic, query/page stats, raw items, crawl summaries, and raw retention.
- `YouTubeRepository` reads/writes channel, video, daily, video-level, breakdown, and retention data.
- `HealthRepository` reads/writes uptime and sitemap health data.
- `ConnectionsRepository` reads/writes provider connection metadata and status while checking secret presence through `CredentialVault`.

Where a logical result spans multiple rows, repositories should use `SqliteDatabase.WriteTransactionAsync`.

## Connection Registry Semantics

`connections` is the integration registry.

Rows contain:

- `id`
- `source`
- `status`
- `config`
- `last_sync`

`config` is JSON metadata. It should not contain new secret values.

Status values observed in code:

- `disconnected`
- `configured`
- `connected`

Cloudflare Zone Analytics uses one row per domain with IDs like `cloudflare:{domain}`.

Singleton-style integrations use stable IDs:

- `wa`
- `sc`
- `perf`
- `crux`
- `pagespeed`
- `bing`
- `youtube`

`ConnectionsRepository.GetPerformanceAsync()` combines `perf`, `crux`, and `pagespeed` into one UI-facing performance connection status.

## Refresh and Sync Behavior

Dashboard:

- loads local summary/report data only
- does not call sync services from its refresh button
- loads deterministic Insights through `InsightMetricsRepository` and `InsightEngine`
- does not use AI models, prompts, new API calls, new mock data, or schema changes for Insights

Cloudflare:

- refresh calls `CloudflareSyncService.SyncConfiguredAsync(selectedDomain, days)`
- then reloads local data

Search Console:

- refresh calls `SearchConsoleSyncService.SyncConfiguredAsync(days)`
- then reloads local data
- if active tab is `indexing`, it also calls URL Inspection for active sitemap URLs

Bing:

- refresh calls `BingWebmasterSyncService.SyncConfiguredAsync()`
- then reloads local data

Performance:

- refresh calls `PerformanceSyncService.SyncConfiguredAsync()`
- then reloads local data

YouTube:

- refresh calls `YouTubeSyncService.SyncConfiguredAsync(days)`
- then reloads local data

Health:

- supports uptime checks and sitemap refreshes through its own ViewModel paths

Sources:

- saves, tests, connects, discovers, adds, and deletes configuration
- does not expose sync actions

## Tests

`Numeris.Tests` is a console executable, not an xUnit/NUnit/MSTest project.

`Numeris.Tests/Program.cs` defines a lightweight `Run(name, Action)` test harness and exits non-zero on failure.

The test harness checks many architecture and regression rules, including:

- domain normalization through `SiteIdentity`
- PageSpeed URL normalization through `PerformanceUrl`
- no old private-field CommunityToolkit `[ObservableProperty]` style in ViewModels
- `SourcesViewModel` remains a coordinator
- sync services do not write through `SqliteDatabase`
- CrUX and PageSpeed have separate registry rows
- explicit migration versioning
- no startup mock data seeding
- repository transaction support
- bounded and redacted raw payload storage
- CrUX 404 handling
- dated Bing page/raw stats
- first-class Bing, Performance, and YouTube navigation
- packaged filled navigation icons
- report pages read through repositories
- YouTube schema, sync, reporting, and Sources availability
- centralized Google OAuth token handling
- visual-system usage across report pages
- Sources page provider grouping, hidden credential editors, guarded actions, status bars, and no sync buttons
- SelectorBar usage for local report tabs
- automation names and loading guards on report toolbars
- report refresh buttons call sync before reload where expected
- responsive visual states

Because this is a custom executable harness, reviewers should check the project file and actual command behavior rather than assuming a standard test framework.

## Lint and Security Scripts

The repository contains PowerShell helper scripts:

- `tools/lint-check.ps1`
- `tools/security-check.ps1`

Project instructions say the user runs these through shell aliases/functions:

- `lc`
- `sc`

The assistant should not run those scripts itself. The scripts write reports under `reports/`, which is ignored.

When there is no Gradle project, `lint-check.ps1` falls back to `.NET` or other available project checks. For this repository, it can use `dotnet format` against the solution.

When OWASP dependency-check is unavailable, `security-check.ps1` falls back to supported dependency audit commands such as `dotnet list package --vulnerable --include-transitive`.

## Security-Critical Invariants

- New secrets must not be serialized into `connections.config`.
- API keys, OAuth client secrets, and refresh tokens belong in `CredentialVault`.
- API error messages shown to UI or stored as raw error items must be sanitized.
- Raw provider payloads must pass through `RawJsonStoragePolicy` before SQLite storage.
- Sync services must not directly mutate SQLite.
- Multi-row logical repository writes should be transactional.
- Legacy import must preserve existing Numeris secrets.
- Legacy credentials must not be deleted automatically.
- `mock` status must not survive as an active connection state except as an import/migration cleanup input.
- Sources page must not become a sync launcher.
- Report refresh operations must guard against concurrent refresh through `CanRefresh` and busy flags.

## Current Non-Goals and Gaps Visible in Code

- The current code has Play Store tables and a default `play_store` registry row, but no active Play Store API client, sync service, page, or ViewModel.
- YouTube supports one authenticated channel, not multiple channel profiles.
- `Period.All` means 365 days, not full database history.
- Dashboard refresh reloads local summary data only; it does not trigger all provider syncs.
- Search Console sync currently targets the fixed `Domains.All` pair through explicit code, not arbitrary user-managed sites.
- Sources page can save and test credentials, but report pages are where live sync is triggered.
- The app stores some bounded raw JSON for diagnostics and reporting; raw storage is intentionally limited and redacted, not eliminated.

## Code Review Checklist

Use this checklist for high-signal review questions:

- Does this change keep the UI -> ViewModel -> SyncService -> API client/Repository layering intact?
- Does any new persistence path bypass a repository?
- Does any new sync path depend on `SqliteDatabase` directly?
- Does any multi-row write need `WriteTransactionAsync`?
- Does any new secret accidentally enter SQLite config, logs, raw JSON, error messages, or UI text?
- Does any new raw payload pass through `RawJsonStoragePolicy`?
- Does any new API error use `ApiRequestException` and `ApiErrorMessage`?
- Does any new domain, origin, or homepage URL use `SiteIdentity` or the `PerformanceUrl` facade?
- Does any schema change increment `CurrentSchemaVersion`, add a migration path, and update both schema-version mechanisms?
- Does any report-page refresh call the right `SyncConfiguredAsync` path before reloading local data?
- Does any Sources page action accidentally introduce a visible `Sync` command?
- Does any chart bypass `ChartTheme` or hard-code palette colors?
- Does any new UI hard-code design tokens already present in `Tokens.xaml`?
- Does any navigation or report-local tab use the expected route or `SelectorBar` pattern?
- Does any change reintroduce mock/demo analytics data at startup?

## External Documentation Checked

These official Microsoft references were checked on 2026-05-15 to align terminology around WinUI 3 and Windows App SDK behavior:

- WinUI 3 overview: https://learn.microsoft.com/windows/apps/winui/
- NavigationView: https://learn.microsoft.com/windows/apps/develop/ui/controls/navigationview
- System backdrops: https://learn.microsoft.com/windows/apps/windows-app-sdk/system-backdrop-controller
- Unpackaged WinUI apps: https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app
- Windows App SDK runtime for unpackaged apps: https://learn.microsoft.com/windows/apps/windows-app-sdk/use-windows-app-sdk-run-time
