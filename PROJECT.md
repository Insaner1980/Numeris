# Numeris Project Description

Last verified from code: 2026-06-09.

This document describes the current Numeris checkout. The code, project files, migrations, and tests remain the source of truth when this document and implementation diverge.

## Executive Summary

Numeris is a native Windows desktop analytics application built with WinUI 3 and the Windows App SDK. It is the native successor/import target for the old Numeris Tauri application, not a web app and not a Tauri UI.

The app initializes a local SQLite database, runs schema migrations, imports legacy Numeris configuration when present, and opens a WinUI shell for reporting and source configuration. It does not seed demo or mock analytics data at startup.

Current live reporting areas are:

- Overview dashboard with deterministic Insights.
- Cloudflare Zone Analytics and Cloudflare Web Analytics.
- Google Search Console.
- Bing Webmaster Tools.
- Web Performance through CrUX and PageSpeed.
- Google Analytics 4 through the Google Analytics Data API.
- Health through uptime checks and sitemap discovery.
- Sources configuration for credentials, provider metadata, and URL/site targets.

YouTube is not a live integration in this checkout. The active code removed YouTube navigation, pages, ViewModels, source cards, API clients, sync service, repository, models, asset references, and seeded connection rows. The v6 migration removes legacy YouTube connection and data tables from existing databases.

## Repository Layout

- `Numeris.slnx` is the solution file.
- `Numeris/` contains the production WinUI application.
- `Numeris.Tests/` contains the console-based architectural and behavioral test harness.
- `tools/lint-check.ps1` and `tools/security-check.ps1` write diagnostics under ignored `reports/`.
- `AGENTS.md`, `memory/MEMORY.md`, and planning documents capture project memory and implementation constraints.
- `migration-plan.md`, `BING-WEBMASTER-SYNC.md`, `CORE-COMPLETION-PLAN.md`, `TESTING_AND_MAINTAINABILITY.md`, and root implementation-plan files are supporting planning artifacts, not stronger truth than source.

## Technology Stack

The production app is `Numeris/Numeris.csproj`.

- Language/runtime: C# on .NET 8, targeting `net8.0-windows10.0.19041.0`.
- Minimum target platform: Windows 10 build `10.0.17763.0`.
- UI framework: WinUI 3 through Windows App SDK.
- Windows App SDK package: `Microsoft.WindowsAppSDK` version `2.0.1`.
- Windows SDK build tools: `Microsoft.Windows.SDK.BuildTools` version `10.0.28000.1839`.
- MVVM helper: `CommunityToolkit.Mvvm` version `8.4.0`.
- WinUI settings controls: `CommunityToolkit.WinUI.Controls.SettingsControls` version `8.2.251219`.
- Dependency injection: `Microsoft.Extensions.DependencyInjection` version `8.0.1`.
- SQLite provider: `Microsoft.Data.Sqlite` version `8.0.10`.
- SQL helper library: `Dapper` version `2.1.66`.
- Charts: `LiveChartsCore.SkiaSharpView.WinUI` version `2.0.2`.
- Nullable reference types are enabled.
- `LangVersion` is `preview` for the CommunityToolkit.Mvvm partial-property model.

The app declares x86, x64, and ARM64 runtime identifiers. `AnyCPU` is mapped to x64 in the project file.

## Packaging and Launch Model

The app is configured as a WinUI desktop executable:

- `OutputType` is `WinExe`.
- `UseWinUI` is `true`.
- `SelfContained` is `false`.
- `WindowsPackageType` is `None`, so unpackaged project execution uses Windows App SDK auto-initialization.
- `EnableMsixTooling` is `true`; `Package.appxmanifest` and publish profiles remain for packaged publishing workflows.
- Publish settings enable ReadyToRun and trimming outside Debug builds.

The code uses Windows APIs directly, including `Windows.Storage.ApplicationData`, `Windows.Security.Credentials.PasswordVault`, WinUI windowing APIs, and XAML resources.

## Startup and Dependency Injection

Startup is defined in `Numeris/App.xaml.cs`.

1. `App` builds the dependency injection container in its constructor.
2. `OnLaunched` resolves `SqliteDatabase`, opening or creating the SQLite database and running migrations.
3. `OnLaunched` resolves `LegacyDataMigrationService` and runs `ImportAllAsync()`.
4. `OnLaunched` resolves `MainWindow` and activates it.

Singleton services include:

- `SqliteDatabase`, `LegacyDataMigrationService`, `SettingsStore`.
- Repositories: `SummaryRepository`, `CloudflareRepository`, `SearchConsoleRepository`, `SitemapRepository`, `WebAnalyticsRepository`, `HealthRepository`, `ConnectionsRepository`, `PerformanceRepository`, `BingRepository`, `GoogleAnalyticsRepository`, `InsightMetricsRepository`.
- `InsightEngine`.
- API clients: `UptimeClient`, `SitemapClient`, `CloudflareGraphqlClient`, `CloudflareRumClient`, `SearchConsoleClient`, `CruxClient`, `PageSpeedClient`, `BingWebmasterClient`, `GoogleAnalyticsClient`.
- Google OAuth services: `GoogleOAuthClient`, `GoogleOAuthFlow`.
- `CredentialVault`.
- Sync services: `CloudflareSyncService`, `WebAnalyticsSyncService`, `SearchConsoleSyncService`, `PerformanceSyncService`, `BingWebmasterSyncService`, `GoogleAnalyticsSyncService`.
- `ShellViewModel` and `MainWindow`.

Transient ViewModels include:

- `DashboardViewModel`
- `CloudflareViewModel`
- `SearchConsoleViewModel`
- `BingViewModel`
- `PerformanceViewModel`
- `GoogleAnalyticsViewModel`
- `HealthViewModel`
- source ViewModels: `CloudflareSourceViewModel`, `WebAnalyticsSourceViewModel`, `SearchConsoleSourceViewModel`, `PerformanceSourceViewModel`, `GoogleAnalyticsSourceViewModel`, `BingSourceViewModel`
- `SourcesViewModel`

Transient pages include:

- `DashboardPage`
- `CloudflarePage`
- `SearchConsolePage`
- `BingPage`
- `PerformancePage`
- `GoogleAnalyticsPage`
- `HealthPage`
- `SourcesPage`

The app uses static `App.Services` resolution inside pages for ViewModels and shared shell state.

## Shell and Navigation

`MainWindow.xaml` defines the shell.

- The window title is `Numeris`.
- The shell uses `<MicaBackdrop />`.
- The visible app background is a single matte `AppBackgroundBrush` color.
- The root grid does not load an `AppBackdrop` bitmap, bitmap scrim, page-specific dashboard hero image, gradient orb, or Tauri-derived UI backdrop.
- Navigation is an always-open left `NavigationView`.
- Pane toggle and back button are disabled.
- Navigation icons are 20 x 20 PNG `ImageIcon` assets from `Numeris/Assets/Icons`.
- The title bar is custom: `ExtendsContentIntoTitleBar = true` and `SetTitleBar(AppTitleBar)`.
- Native caption button colors are read from token resources.

Navigation routes are centralized in `MainWindow.xaml.cs`:

- `dashboard` -> `DashboardPage`
- `cloudflare` -> `CloudflarePage`
- `search` -> `SearchConsolePage`
- `bing` -> `BingPage`
- `performance` -> `PerformancePage`
- `analytics` -> `GoogleAnalyticsPage`
- `health` -> `HealthPage`
- `sources` -> `SourcesPage`

`ShellViewModel` persists:

- selected period
- selected domain
- last selected page

Known domain choices:

- `all`
- `knittoolsapp.com`
- `finnvek.com`

Known period choices:

- `Last7Days` / `7d` / `Last 7 days`
- `Last30Days` / `30d` / `Last 30 days`
- `Last90Days` / `90d` / `Last 90 days`
- `All` / `All` / `All time`

`Period.All` means 365 days, not an unbounded all-time query.

## Local Files

`AppPaths` selects storage paths by package state.

Packaged execution:

- base data directory: `ApplicationData.Current.LocalFolder.Path`

Unpackaged execution:

- base data directory: `%LocalAppData%\Numeris`

Derived paths:

- SQLite database: `numeris.db`
- shell settings JSON: `settings.json`
- logs directory: `logs`
- legacy Numeris import directory: `legacy\numeris`
- legacy Numeris database path: `legacy\numeris\numeris.db`

The base data directory and legacy import directory are created automatically when resolved.

## SQLite Architecture

`SqliteDatabase` owns one open `SqliteConnection` for the app lifetime.

Important behavior:

- database file is opened with `ReadWriteCreate`
- shared cache is enabled
- foreign keys are enabled in the connection string
- migrations run immediately after opening
- a `SemaphoreSlim` serializes reads and writes
- supported access helpers are `ReadAsync`, `WriteAsync`, `WriteAsync<T>`, and `WriteTransactionAsync`
- multi-row logical writes should use `WriteTransactionAsync`
- `ClearAllData()` deletes report data and resets connection statuses but keeps the schema

Sync services must not write SQLite directly. API clients fetch data, sync services orchestrate credentials and time windows, repositories perform persistence, and ViewModels own UI state.

## Schema Versioning

`Migrations.CurrentSchemaVersion` is `7`.

Versioning is tracked in both:

- `PRAGMA user_version`
- `meta.schema_version`

`Migrations.RunAll()` performs:

1. WAL mode and foreign-key setup.
2. full current schema creation through `CREATE TABLE IF NOT EXISTS`.
3. pending version migrations.
4. default connection, URL, site, and metadata insertion.

Migration highlights:

- v2 preserves dated Bing page stats by adding `date` to the `bing_page_stats` primary key.
- v3 removes earlier mock/demo analytics data and converts `mock` connection statuses to `disconnected`.
- v4 now only advances schema metadata; it no longer seeds YouTube.
- v5 adds Cloudflare page breakdown storage.
- v6 adds Google Analytics tables, seeds `ga4`, deletes the old `youtube` connection, and drops old YouTube tables.
- v7 adds `google_analytics_pages.engaged_sessions` when missing and backfills it from sessions and engagement rate.

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

Legacy/unused Play Store schema remains present without active Play Store client, sync service, page, or ViewModel:

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

Google Analytics:

- `google_analytics_daily`
- `google_analytics_pages`
- `google_analytics_sources`
- `google_analytics_events`
- `google_analytics_devices`

Health:

- `uptime_checks`

Metadata:

- `meta`

There are no current YouTube tables in the full schema.

## Default Registry Data

The app inserts default disconnected registry rows:

- `cf` / `cloudflare`
- `sc` / `search_console`
- `ps` / `play_store`
- `wa` / `web_analytics`
- `perf` / `performance`
- `crux` / `crux`
- `pagespeed` / `pagespeed`
- `bing` / `bing_webmaster`
- `ga4` / `google_analytics`

The app inserts default performance URLs:

- `https://finnvek.com/`
- `https://knittoolsapp.com/`

The app inserts default Bing sites:

- `https://finnvek.com/`
- `https://knittoolsapp.com/`

These defaults are configuration targets, not mock metrics.

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
- Google Analytics client secret: `Numeris.GoogleAnalytics.ClientSecret`
- Google Analytics refresh token: `Numeris.GoogleAnalytics.RefreshToken`

Cloudflare tokens are stored per normalized domain. Web Analytics tokens are stored per account ID. Search Console and Google Analytics OAuth secrets are stored per client ID. CrUX, PageSpeed, and Bing keys use the `default` user key.

Deleting a credential removes the PasswordVault entry. It does not write an empty password.

## Legacy Numeris Import

`LegacyDataMigrationService` imports configuration from the old Numeris Tauri app.

Current legacy source path:

- `%LocalAppData%\Numeris\legacy\numeris\numeris.db` in unpackaged execution.

Imported integrations:

- Cloudflare Zone Analytics connections.
- Cloudflare Web Analytics account and site tags.
- Google Search Console OAuth configuration.

The importer:

- skips when the legacy database is missing
- reads legacy SQLite in read-only mode
- preserves existing Numeris secrets
- reads old Credential Manager values using the Rust keyring target model `<username>.Numeris`
- falls back to same-`<username>.` target discovery when exact legacy targets are absent
- copies found legacy secrets into Numeris `CredentialVault` entries
- falls back to legacy plain text config only when no Numeris secret exists
- normalizes Cloudflare bearer tokens by removing a leading `Bearer `
- converts missing or `mock` legacy status to `configured`
- does not delete legacy credentials

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

`PerformanceUrl` remains the PageSpeed-facing facade and delegates normalization to `SiteIdentity`.

## Raw JSON and API Error Policy

`RawJsonStoragePolicy` centralizes raw upstream payload storage.

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
- API keys and similarly named fields
- query-string secrets
- bearer tokens

External API failures should use `ApiRequestException` and `ApiErrorMessage`. API clients and ViewModels should not show raw response bodies, request URLs containing keys, or bearer values.

## External Integrations

### Cloudflare Zone Analytics

Main files:

- `CloudflareGraphqlClient`
- `CloudflareSyncService`
- `CloudflareRepository`
- `CloudflareViewModel`
- `CloudflarePage`
- `CloudflareSourceViewModel`

API endpoints:

- REST validation: `https://api.cloudflare.com/client/v4`
- GraphQL analytics: `https://api.cloudflare.com/client/v4/graphql`

Stored data:

- daily pageviews, unique visitors, requests, cache, bandwidth, threats
- daily status codes
- daily country visitor breakdown
- daily path/request breakdown

Refresh flow:

1. `CloudflareViewModel.RefreshAsync()` calls `CloudflareSyncService.SyncConfiguredAsync(selectedDomain, days)`.
2. Cloudflare sync loads connection metadata from `connections`.
3. The token is loaded from `CredentialVault`.
4. `CloudflareGraphqlClient` queries Cloudflare GraphQL.
5. `CloudflareRepository.UpsertTrafficAsync()` writes the logical result in one repository transaction.
6. The connection `last_sync` is updated and the ViewModel reloads local repository data.

Cloudflare report tabs:

- Traffic
- Cache
- Security
- Status codes
- Web Analytics

### Cloudflare Web Analytics

Main files:

- `CloudflareRumClient`
- `WebAnalyticsSyncService`
- `WebAnalyticsRepository`
- `CloudflareViewModel`
- `CloudflarePage`
- `WebAnalyticsSourceViewModel`

API endpoints:

- `https://api.cloudflare.com/client/v4`
- `https://api.cloudflare.com/client/v4/graphql`

Stored data:

- configured domain/site-tag mappings
- daily visits and page views
- referrers
- pages
- countries

The Sources page can discover Cloudflare Web Analytics sites for an account, and manual domain-to-site-tag mappings are supported. Sync requires at least one saved site-tag mapping.

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

Search Analytics data is stored in `search_console`, `search_devices`, and `search_page_queries`.

Indexing data comes from the URL Inspection API, not from Search Analytics rows or sitemap presence alone. On the Indexing tab, the page refresh path also runs `SearchConsoleSyncService.InspectSitemapUrlsAsync()` for active sitemap URLs. The separate indexing refresh button is not used for the main report refresh path.

Search Console report tabs:

- Overview
- Queries
- Pages
- Devices
- Indexing

### Google Analytics 4

Main files:

- `GoogleAnalyticsClient`
- `GoogleAnalyticsSyncService`
- `GoogleAnalyticsRepository`
- `GoogleAnalyticsViewModel`
- `GoogleAnalyticsPage`
- `GoogleAnalyticsSourceViewModel`

API endpoint:

- `https://analyticsdata.googleapis.com/v1beta/properties/{propertyId}:runReport`

OAuth scope:

- `https://www.googleapis.com/auth/analytics.readonly`

Stored data:

- `google_analytics_daily`: active users, sessions, page views, engaged sessions, event count, engagement rate by day.
- `google_analytics_pages`: page rows for the selected period, including engaged sessions and engagement rate.
- `google_analytics_sources`: acquisition rows by source/medium.
- `google_analytics_events`: event rows and key-event counts.
- `google_analytics_devices`: device category rows by day.

Sync flow:

1. `GoogleAnalyticsViewModel.RefreshAsync()` calls `GoogleAnalyticsSyncService.SyncConfiguredAsync(days)`.
2. The sync service loads the `ga4` connection.
3. Client secret and refresh token are loaded from `CredentialVault`.
4. `GoogleOAuthClient` refreshes an access token.
5. `GoogleAnalyticsClient.RunReportAsync()` calls Data API `properties.runReport`.
6. `GoogleAnalyticsRepository.UpsertRollupAsync()` writes the rollup transactionally.
7. `ConnectionsRepository.UpdateGoogleAnalyticsLastSyncAsync()` updates metadata and `last_sync`.

Google Analytics report tabs:

- Overview
- Pages
- Acquisition
- Events
- Devices

### Web Performance

Main files:

- `CruxClient`
- `PageSpeedClient`
- `PerformanceSyncService`
- `PerformanceRepository`
- `PerformanceViewModel`
- `PerformancePage`
- `PerformanceSourceViewModel`
- `PerformanceUrl`

Stored data:

- configured URLs in `performance_urls`
- CrUX metric points in `crux_metric_points`
- PageSpeed runs in `pagespeed_runs`
- PageSpeed audits in `pagespeed_audits`

Performance sync uses enabled URLs and runs both mobile and desktop PageSpeed strategies. PageSpeed sync is serialized, spaces requests, and retries transient or rate-limited failures. CrUX `404 NotFound` is treated as missing field data rather than API-key failure.

Performance report tabs:

- Overview
- CrUX
- PageSpeed
- URLs

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
- sanitized raw method items and error records

Bing report tabs:

- Overview
- Queries
- Pages
- Crawl

New Bing methods should be checked against `BING-WEBMASTER-SYNC.md`; the project intentionally avoids mass-adding every Bing detail method.

### Health, Uptime, and Sitemaps

Main files:

- `UptimeClient`
- `SitemapClient`
- `HealthRepository`
- `SitemapRepository`
- `HealthViewModel`
- `HealthPage`

Uptime checks:

- probe `https://{normalized-domain}`
- store status, status code, response time, and error text in `uptime_checks`

Sitemaps:

- discover URLs from `https://{domain}/sitemap-index.xml`
- parse sitemap index and URL-set XML using the sitemap namespace
- store active and removed URLs in `sitemap_urls`

Health report tabs:

- Uptime
- Sitemap

## Google OAuth Design

Google OAuth is centralized in `GoogleOAuthClient` and `GoogleOAuthFlow`.

`GoogleOAuthFlow`:

- starts a loopback `TcpListener` on `127.0.0.1` with an ephemeral port
- builds a redirect URI ending in `/oauth2callback`
- generates and verifies state
- opens the browser through a delegate supplied by the caller
- waits up to two minutes by default
- returns `OAuthTokens`
- sends a small local HTML completion response to the browser

Search Console and Google Analytics both use this shared OAuth path with source-specific scopes.

## ViewModel Responsibilities

Report ViewModels:

- subscribe to `ShellViewModel.PropertyChanged`
- reload when selected domain or period changes
- expose `CanRefresh` to disable refresh while busy
- read reporting data through repositories
- call their saved-configuration sync path before reloading local data when refresh should fetch live provider data
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

`SourcesViewModel` is only a coordinator. It delegates source-specific behavior to:

- `CloudflareSourceViewModel`
- `WebAnalyticsSourceViewModel`
- `SearchConsoleSourceViewModel`
- `GoogleAnalyticsSourceViewModel`
- `PerformanceSourceViewModel`
- `BingSourceViewModel`

Tests explicitly prevent `SourcesViewModel` from depending on low-level clients or the vault.

## Refresh and Sync Behavior

Overview:

- refresh reloads local summary/report data
- it does not call all provider sync services
- Insights are generated from local SQLite metrics through `InsightMetricsRepository` and deterministic `InsightEngine`
- Insights do not use AI models, prompts, new external API calls, new mock data, or new schema tables

Cloudflare:

- refresh calls `CloudflareSyncService.SyncConfiguredAsync(selectedDomain, days)`
- refresh also calls `WebAnalyticsSyncService.SyncConfiguredAsync(days)`
- then it reloads local repository data

Search Console:

- refresh calls `SearchConsoleSyncService.SyncConfiguredAsync(days)`
- if the active tab is `indexing`, refresh also inspects active sitemap URLs
- then it reloads local repository data

Google Analytics:

- refresh calls `GoogleAnalyticsSyncService.SyncConfiguredAsync(days)`
- then it reloads local repository data

Bing:

- refresh calls `BingWebmasterSyncService.SyncConfiguredAsync()`
- then it reloads local repository data

Performance:

- refresh calls `PerformanceSyncService.SyncConfiguredAsync()`
- then it reloads local repository data

Health:

- supports uptime checks and sitemap refresh through its own ViewModel methods

Sources:

- saves, tests, connects, discovers, adds, and deletes configuration
- does not expose live sync buttons

## Overview Insights

Overview Insights are deterministic and local.

Main files:

- `Numeris/Models/InsightRows.cs`
- `Numeris/Services/Insights/Trend.cs`
- `Numeris/Services/Insights/InsightEngine.cs`
- `Numeris/Services/Database/Repositories/InsightMetricsRepository.cs`
- `Numeris/ViewModels/DashboardViewModel.cs`
- `Numeris/Views/DashboardPage.xaml`

The metrics repository reads from existing local tables:

- Cloudflare visitors, cache ratio, threats, and status codes.
- GA4 users, engagement rate, and key events.
- Search Console impressions, clicks, mobile clicks/impressions, declining pages, and new queries.
- PageSpeed mobile performance score.
- Bing impressions and clicks.
- Sitemap URL Inspection summary.
- Source freshness from connected rows in `connections`.

The rule engine emits at most four cards, sorted by severity and priority. It uses ratio math internally and formats ratios as user-facing percentages. Activity from zero is represented as `TrendState.NewActivity`, not artificial `+100%`.

## Report Pages and UI Patterns

Report pages use a shared pattern:

- `PageHeaderBorderStyle` for the header layout.
- domain selector where applicable.
- `PeriodSelector` for period selection.
- refresh button styled with `ToolbarRefreshButtonStyle`.
- `AutomationProperties.Name` on domain, period, refresh, and filter controls.
- local report tabs through `SelectorBar` and `TopTabSelectorBarStyle`.
- responsive `VisualStateManager` states.
- chart creation through `ChartTheme.CreateCartesianChart()`.
- chart surfaces through `ChartTheme.CreateChartSurface(chart)`.

Pages with `SelectorBar`:

- Cloudflare
- Google Search
- Analytics
- Bing
- Performance
- Health

Nested `NavigationView` controls are not used for report-local tabs.

## Sources Page

`SourcesPage` is a configuration page, not a reporting page and not a live-sync launcher.

Provider groups currently shown:

- Cloudflare
- Google
- Bing

Provider sections currently include:

- Cloudflare Zone Analytics
- Cloudflare Web Analytics
- Google Search Console
- Google Analytics
- Google Web Performance API keys
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

Current visual baseline:

- `AppBackgroundColor`: `#151419`
- `NavigationLayerColor`: `#00000000`
- `ContentLayerColor`: `#00000000`
- `CardSurfaceColor`: `#F017181D`
- `ControlSurfaceColor`: `#F0222526`
- `ChartPanelColor`: `#E8151419`
- primary text: `#FBFBFB`
- secondary text: `#DAD4CC`
- tertiary text: `#BFBFBF`
- accent: `NumerisAccentColor #F56E0F`
- accent foreground: `#151419`
- chart secondary copper: `#E09145`
- warm platinum highlight: `#FCD9B8`

Design-system ownership:

- Page, card, chart, status, action-button, data-table, InfoBar, ListView, navigation, and period-selector resources live in `Tokens.xaml`.
- Page headers are transparent and borderless layout surfaces, not cards.
- Navigation and content layers are transparent so the shell reads as one continuous matte background.
- Toolbar/action buttons are flat tokenized surfaces; hover and pressed states change color only.
- `PeriodSelector` segments are transparent except for the selected lava/accent fill.
- Table-like headers use `DataTableHeaderBorderStyle`.
- Horizontal bar visualizations use `HorizontalBars`.

## Charting

LiveCharts construction is centralized in `Numeris.Themes.ChartTheme` and color access in `Numeris.Themes.ChartPalette`.

`ChartTheme` owns:

- `CreateCartesianChart()`
- `CreateChartSurface()`
- `StyleXAxis()`
- `StyleYAxis()`
- `CreateMatteColumnSeries()`
- `CreateMutedColumnSeries()`
- `StyleStackedColumnSeries()`

ViewModels may build series from repository rows, but chart controls, chart surfaces, axes, legend/tooltip paints, and shared palette colors should go through the central helpers.

## Connection Registry Semantics

`connections` is the integration registry.

Rows contain:

- `id`
- `source`
- `status`
- `config`
- `last_sync`

`config` is JSON metadata. It must not contain new secret values.

Status values observed in code:

- `disconnected`
- `configured`
- `connected`

Cloudflare Zone Analytics uses per-domain IDs such as `cloudflare:{domain}`.

Singleton-style IDs:

- `wa`
- `sc`
- `perf`
- `crux`
- `pagespeed`
- `bing`
- `ga4`

`ConnectionsRepository.GetPerformanceAsync()` combines `perf`, `crux`, and `pagespeed` into one UI-facing performance connection status.

## Tests

`Numeris.Tests` is a console executable, not an xUnit/NUnit/MSTest project.

`Numeris.Tests/Program.cs` defines the custom test harness and exits non-zero on failure. The harness verifies architecture and regression rules including:

- domain and URL normalization
- PageSpeed URL normalization
- no old private-field CommunityToolkit `[ObservableProperty]` style in ViewModels
- `SourcesViewModel` remains a coordinator
- sync services do not write through `SqliteDatabase`
- CrUX and PageSpeed separate registry rows
- explicit migration versioning
- no startup mock data seeding
- repository transaction support
- bounded and redacted raw payload storage
- sanitized API error handling
- CrUX 404 handling
- dated Bing page/raw stats
- active Bing, Performance, Analytics, Health, Sources, and Overview navigation
- no active YouTube navigation, source card, asset, schema seed, or project asset reference
- Google Analytics schema, sync, repository, source ViewModel, reporting page, OAuth scope, and icon packaging
- centralized Google OAuth token handling
- visual-system usage across report pages
- Sources page provider grouping, hidden credential editors, guarded actions, status bars, and no sync buttons
- `SelectorBar` usage for local report tabs
- `PeriodSelector` usage for report periods
- automation names and loading guards on report toolbars
- report refresh buttons call sync before reload where expected
- deterministic Overview Insights behavior and formatting

Because this is a custom executable harness, reviewers should check `Numeris.Tests/Program.cs` and the project file instead of assuming a standard .NET test framework.

## Lint and Security Scripts

The repository contains helper scripts:

- `tools/lint-check.ps1`
- `tools/security-check.ps1`

Project instructions say the user runs these through PowerShell profile functions:

- `lc`
- `sc`

The assistant should not run those scripts itself. The scripts write reports under ignored `reports/`.

For this repository, lint fallback can use `.NET` checks such as `dotnet format` against the solution. Security fallback can use supported dependency audit commands such as `dotnet list package --vulnerable --include-transitive` when OWASP dependency-check is unavailable.

## Security-Critical Invariants

- New secrets must not be serialized into `connections.config`.
- API keys, OAuth client secrets, and refresh tokens belong in `CredentialVault`.
- API error messages shown to UI or stored as raw error items must be sanitized.
- Raw provider payloads must pass through `RawJsonStoragePolicy`.
- Sync services must not directly mutate SQLite.
- Multi-row logical repository writes should be transactional.
- Legacy import must preserve existing Numeris secrets.
- Legacy credentials must not be deleted automatically.
- `mock` status must not survive as an active connection state except as an import/migration cleanup input.
- Sources page must not become a sync launcher.
- Report refresh operations must guard against concurrent refresh through `CanRefresh` and busy flags.
- YouTube must not be reintroduced as a live integration without a new explicit design and migration plan.

## Current Non-Goals and Gaps Visible in Code

- Play Store tables and a default `play_store` registry row still exist, but there is no active Play Store API client, sync service, page, or ViewModel.
- `Period.All` means 365 days, not full database history.
- Dashboard/Overview refresh reloads local summary data only; it does not trigger all provider syncs.
- Search Console sync still works through the current fixed site/domain logic rather than a fully user-managed arbitrary site list.
- Sources page can save, connect, and test credentials, but report pages are where live sync is triggered.
- The app stores bounded raw JSON for diagnostics and reporting; raw storage is limited and redacted, not eliminated.

## Code Review Checklist

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
- Does any change reintroduce active YouTube code or seeded registry rows without an explicit new architecture decision?

## External Documentation Checked

Official references checked on 2026-06-09:

- WinUI 3: https://learn.microsoft.com/windows/apps/winui/
- Windows App SDK: https://learn.microsoft.com/windows/apps/windows-app-sdk/
- Unpackaged WinUI apps: https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app
- Windows App SDK runtime for unpackaged apps: https://learn.microsoft.com/windows/apps/windows-app-sdk/use-windows-app-sdk-run-time
- System backdrops: https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops
- Google Analytics Data API `properties.runReport`: https://developers.google.com/analytics/devguides/reporting/data/v1/rest/v1beta/properties/runReport
- Cloudflare GraphQL Analytics API: https://developers.cloudflare.com/analytics/graphql-api/
- Bing Webmaster API: https://learn.microsoft.com/bingwebmaster/
- Bing Webmaster supported API protocols: https://learn.microsoft.com/bingwebmaster/api-protocols
