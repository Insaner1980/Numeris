# Numeris Project Description

Documentation review: **2026-10-07** (Europe/Helsinki).
Repository: **Insaner1980/Numeris**, identified from the configured Git origin.
Branch: `codex/numeris-core-continuation`.
Inspected HEAD: `96541b06988c788e2d4e6e8b4ebd42e2ccbdb651`.

**Provenance:** this describes the current working tree, including substantial uncommitted implementation and test changes and pre-existing edits to this document. HEAD alone does not reproduce that implementation. Production source, project configuration and test bodies were inspected; planning documents and project memory were checked as historical context. Source and configuration remain authoritative if they diverge from this reference.

**Verification boundary:** this is a repository-wide documentation review, not an execution certification. No application launch, build, restore, test executable, formatter, dependency audit, security scanner, provider request, OAuth flow, migration or import was run. The live analytics database and credential stores were not opened. Documentation checks do not establish visual correctness, accessibility, valid credentials, passing tests or working integrations. External references below retain their historical dates.

## Executive Summary

Numeris is a native Windows desktop analytics application built with WinUI 3 and the Windows App SDK. It is the native successor/import target for the old Numeris Tauri application, not a web app and not a Tauri UI.

The app initializes a local SQLite database, runs schema migrations, imports legacy Numeris configuration when present, and opens a WinUI shell for reporting and source configuration. It does not seed demo or mock analytics data at startup.

Current live reporting areas are:

- Overview dashboard with deterministic Insights.
- Cloudflare Zone Analytics and Cloudflare Web Analytics.
- Google Search Console.
- Bing Webmaster Tools.
- Web Performance through CrUX and PageSpeed.
- Health through uptime checks and sitemap discovery.
- Sources configuration for credentials, provider metadata, and URL/site targets.

Google Analytics/GA4 is removed from current navigation, DI, production pages, source editors, clients, sync, repositories and models. Schema v11 drops its connection and six possible report tables; startup removes only the credential resources `Numeris.GoogleAnalytics.ClientSecret` and `Numeris.GoogleAnalytics.RefreshToken`. Neither default configuration nor legacy import reintroduces it.

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

These are direct `PackageReference` declarations in `Numeris/Numeris.csproj`, not a fresh restore or a verified transitive dependency graph. No repository-owned central package management, Directory.Build props/targets, NuGet lockfile, global.json SDK pin or CI workflow was found. Generated build output is not the version authority for this review.

The app declares x86, x64 and ARM64 platforms and `win-x86;win-x64;win-arm64` runtime identifiers. `AnyCPU` maps to x64. `Numeris.slnx` exposes x64 and includes both projects, with Release deployment metadata for the app. The test executable targets x64 only.

## Packaging and Launch Model

The app is configured as a WinUI desktop executable:

- `OutputType` is `WinExe`.
- `UseWinUI` is `true`.
- `SelfContained` is `false`.
- `WindowsPackageType` is `None`, so unpackaged project execution uses Windows App SDK auto-initialization.
- `EnableMsixTooling` is `true`; `Package.appxmanifest` and publish profiles remain for packaged publishing workflows.
- `WinUISDKReferences=false` is explicitly declared.
- `PublishReadyToRun` and `PublishTrimmed` are false in Debug and true for other configurations. This does not prove trimmed publishing works.
- The three `Numeris/Properties/PublishProfiles/win-*.pubxml` files use FileSystem publishing, matching platforms/RIDs, `SelfContained=true` and `PublishSingleFile=False`. Thus publish profiles override the framework-dependent project default.
- `Numeris/Properties/launchSettings.json` declares “Numeris (Package)” (`MsixPackage`) and “Numeris (Unpackaged)” (`Project`). MSIX tooling and a packaged launch profile coexist with `WindowsPackageType=None`; an end-to-end package/signing process is not established by these files.
- `Numeris/app.manifest` declares Windows 10 compatibility and PerMonitorV2 DPI awareness. `Numeris/Package.appxmanifest` declares version 1.0.0.0, Windows.Universal and Windows.Desktop minimum 10.0.17763.0 and MaxVersionTested 10.0.26226.0. Those are manifest values, not this review's tested OS versions. Capabilities include `runFullTrust` and `systemAIModels`; no model execution was found in production source.

The code uses Windows APIs directly, including `Windows.Storage.ApplicationData`, `Windows.Security.Credentials.PasswordVault`, WinUI windowing APIs, and XAML resources.

## Startup and Dependency Injection

Startup is defined in `Numeris/App.xaml.cs`.

1. `App` builds the dependency injection container in its constructor.
2. `OnLaunched` resolves `SqliteDatabase`, opening or creating the SQLite database and running migrations.
3. `CredentialVault.RemoveRetiredCredentials()` removes GA4 client-secret and refresh-token resource entries.
4. `LegacyDataMigrationService.ImportAllAsync().GetAwaiter().GetResult()` imports legacy configuration synchronously before showing the window.
5. `MainWindow` and its shared `ShellViewModel` resolve; available domains and saved settings are loaded, the custom title bar is configured, and the window activates. NavigationView Loaded restores a known route or falls back to Overview.

Database initialization, retired-credential cleanup and import have no outer startup recovery handler here; failure can prevent window activation. No automatic provider refresh, timer or mock seeding occurs in this startup path.

Singleton services include:

- `SqliteDatabase`, `LegacyDataMigrationService`, `SettingsStore`.
- Repositories: `SummaryRepository`, `CloudflareRepository`, `SearchConsoleRepository`, `SitemapRepository`, `WebAnalyticsRepository`, `HealthRepository`, `ConnectionsRepository`, `PerformanceRepository`, `BingRepository`, `InsightMetricsRepository`.
- `InsightEngine`.
- API clients: `UptimeClient`, `SitemapClient`, `CloudflareGraphqlClient`, `CloudflareRumClient`, `SearchConsoleClient`, `CruxClient`, `PageSpeedClient`, `BingWebmasterClient`.
- Google OAuth services: `GoogleOAuthClient`, `GoogleOAuthFlow`.
- `CredentialVault`.
- Sync services: `CloudflareSyncService`, `WebAnalyticsSyncService`, `SearchConsoleSyncService`, `PerformanceSyncService`, `BingWebmasterSyncService`.
- `ShellViewModel` and `MainWindow`.

Transient ViewModels include:

- `DashboardViewModel`
- `CloudflareViewModel`
- `SearchConsoleViewModel`
- `BingViewModel`
- `PerformanceViewModel`
- `HealthViewModel`
- source ViewModels: `CloudflareSourceViewModel`, `WebAnalyticsSourceViewModel`, `SearchConsoleSourceViewModel`, `PerformanceSourceViewModel`, `BingSourceViewModel`
- `SourcesViewModel`

Transient pages include:

- `DashboardPage`
- `CloudflarePage`
- `SearchConsolePage`
- `BingPage`
- `PerformancePage`
- `HealthPage`
- `SourcesPage`

Pages resolve transient ViewModels and the singleton shell through static `App.Services`; `Frame.Navigate` creates pages by route type. CommunityToolkit observable partial properties and relay commands coexist with explicit code-behind event handlers and `x:Bind`.

API clients reuse static HttpClient instances. Report ViewModels subscribe to shell changes and implement `IDisposable` to unsubscribe; page Unloaded handlers detach chart-update subscriptions and dispose their ViewModel subscriptions. This does not cancel pending network work or dispose chart controls explicitly. The container and database are process-lifetime owners; App does not explicitly dispose its ServiceProvider on window close.

The database gate serializes connection access, not whole synchronization workflows. Busy flags guard each report/source instance, while the singleton PageSpeed limiter serializes its retry path. Shell changes start unawaited loads; there is no shared refresh lock, request generation check, or general cancellation token threaded from pages through all providers. Separate ViewModels or navigation can therefore outlive/overlap work. The database cancellation token only cancels waiting for its gate; SQL delegates are synchronous.

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
- `health` -> `HealthPage`
- `sources` -> `SourcesPage`

`ShellViewModel` persists:

- selected period
- selected domain
- last selected page

Domain choices are **not restricted to two sites**. `ShellViewModel.AvailableDomains` starts with `all`, `knittoolsapp.com` and `finnvek.com`, then adds normalized configured domains. `ConnectionsRepository.ListConfiguredDomainsAsync()` unions JSON config `domain` and `sites`, enabled performance URL origins, enabled Bing sites, and Web Analytics mappings. It strips `sc-domain:`, ignores invalid identities and deduplicates. The two baseline domains remain even when their configuration is deleted.

Every report page refreshes this list on Loaded; Cloudflare and Performance source loaders also refresh it. Web Analytics/Bing changes are picked up on subsequent report load rather than through a universal live collection notification. A saved domain no longer present falls back to `all`; retired/unknown routes fall back to `dashboard`. Tabs, query filters, sort and CrUX selectors are transient, not settings.json preferences.

Known period choices:

- `Last7Days` / `7d` / `Last 7 days`
- `Last30Days` / `30d` / `Last 30 days`
- `Last90Days` / `90d` / `Last 90 days`
- `All` / `All` / `All time`

Periods end on local `DateTime.Today` and include exactly the selected number of calendar dates (`start = end - (days - 1)`). `Period.All` means 365 days, not an unbounded all-time query. Previous comparison windows are the immediately preceding equal number of dates. Provider fetch limits and latest-snapshot widgets can differ from this display range; see integration and metric sections.

Page/tab inventory:

| Route / page under `Numeris/Views/` | Local tabs and major report content |
| --- | --- |
| `dashboard` / `DashboardPage` | Overview only: visitor hero chart, Google/Bing clicks, CrUX status, mobile PageSpeed, Insights, cache/indexing/last-sync strip, combined search chart |
| `cloudflare` / `CloudflarePage` | Traffic (visitors/pageviews, top countries/pages), Cache (hit ratio/bytes saved), Security (threat counts), Status codes (HTTP groups/issues), Web Analytics (visits/pageviews/referrers/pages/countries) |
| `search` / `SearchConsolePage` | Overview, Queries (text filter and sort; new queries), Pages (top/declining), Devices (clicks), Indexing (active sitemap URLs and inspection state) |
| `bing` / `BingPage` | Overview (clicks/impressions/CTR), Queries (sort), Pages, Crawl (raw method summaries and issue JSON) |
| `performance` / `PerformancePage` | Overview (latest vitals/scores), CrUX (metric/form-factor trend), PageSpeed (latest runs, trends, audits), URLs (read-only configured target list) |
| `health` / `HealthPage` | Uptime (domain statuses, response/incident charts and Check now), Sitemap (active URLs and Refresh sitemap) |
| `sources` / `SourcesPage` | Provider cards and collapsed credential editors; detailed actions below |

No custom ContentDialog workflow is implemented in these pages. Delete actions execute directly; OAuth opens the external browser and a local callback completion page. There is no settings navigation item, export screen or generic report drill-down dialog in the current route map.

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

The base data directory and legacy import directory are created when resolved. `LogsDir` is a path helper only: no application log-file writer or telemetry initialization was found. SQLite uses WAL; its sidecar files may accompany the database. Settings are a plain JSON file: Load catches missing/malformed-file errors and defaults; Save writes directly, without an atomic replacement or catch. Source review does not verify actual files on a user's installation.

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
- `ClearAllData()` is an unexposed helper: no production UI caller was found. It deletes report data and Web Analytics mappings, resets connection statuses/last_sync, but keeps schema, connection configs, performance URLs, Bing sites, settings and credentials. It uses the gate but no explicit transaction.

Sync services do not write SQLite directly. API clients fetch data, sync services orchestrate credentials and time windows, repositories perform persistence, and ViewModels own UI state. HealthViewModel directly orchestrates its clients/repositories without a SyncService.

**Transaction coverage is not universal.** Cloudflare rollups, Web Analytics rollups/discovery, Health sitemap replacement, PageSpeed run+audit writes/retention and Bing normalized-row+raw-item pairs use `WriteTransactionAsync`. Search Console's delete-and-reinsert methods use only `WriteAsync`; legacy site import and multi-row performance registry updates also lack an explicit transaction. The project rule requires atomic logical writes, but the current implementation does not satisfy that rule everywhere. Provider-wide sync is never one atomic transaction; earlier successful writes can survive later failures.

Foreign-key enforcement is enabled, but current schema SQL defines no relational FOREIGN KEY constraints; removal/retention relationships are handled explicitly.

## Schema Versioning

`Migrations.CurrentSchemaVersion` is `11`.

Versioning is tracked in both:

- `PRAGMA user_version`
- `meta.schema_version`

`Migrations.RunAll()` performs:

1. WAL mode and foreign-key setup.
2. full current schema creation through `CREATE TABLE IF NOT EXISTS`.
3. pending version migrations.
4. default connection, URL, site, and metadata insertion.

Migration highlights:

- v2 adds `date` to the `bing_page_stats` primary key to support dated history; older rows without dates are not thereby given their original provider dates.
- v3 removes earlier mock/demo analytics data and converts `mock` connection statuses to `disconnected`.
- v4 now only advances schema metadata; it no longer seeds YouTube.
- v5 adds Cloudflare page breakdown storage.
- v6 deletes the old YouTube connection and tables.
- v8 normalizes legacy Bing dates.
- v9 clears old overlapping Cloudflare and Web Analytics breakdown caches.
- v11 removes the retired GA4 connection and all six possible tables: daily, active_users, pages, sources, events and devices. Startup separately removes its two credential resources.

There are no `RunV7Migration` or `RunV10Migration` branches in current source; do not infer missing historical functionality from numeric gaps. Version comes from user_version, falling back to meta only when zero. Pending decisions use the version read at entry. Several individual migrations write CurrentSchemaVersion rather than their own step number, and the overall migration sequence is not one transaction. There is no explicit rejection of a database newer than this code. These are source-visible recovery/versioning limits, not exercised migration failures.

Defaults use INSERT OR IGNORE on every startup. Deleting a default performance URL or Bing site allows that target to be reinserted on next startup. No metric rows are generated by defaults.

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

Health:

- `uptime_checks`

Metadata:

- `meta`

There are no current YouTube or GA4 tables in the full schema.

Significant identities and owners:

| Tables | Identity / ownership |
| --- | --- |
| `cloudflare_traffic` | unique domain/date; `CloudflareRepository` |
| `cloudflare_countries`, `cloudflare_pages`, `cloudflare_status_codes` | domain/date plus country, path or status code; same owner |
| `search_console` | surrogate ID, site/date/kind indexes, no unique dimensional key; replaced per domain/kind/date range by `SearchConsoleRepository` |
| `search_devices`; `search_page_queries` | site/date/device; site/period_start/period_end/page/query |
| `sitemap_urls` | domain/URL; discovery lifecycle in `HealthRepository`, inspection in `SitemapRepository` |
| `web_analytics_sites`; daily/category tables | unique domain; domain/date plus category where applicable; `WebAnalyticsRepository` |
| `performance_urls` | surrogate ID, unique normalized URL, origin, source, enabled; `PerformanceRepository` |
| `crux_metric_points` | target_type/target/form_factor/collection_end/metric; nullable p75 and histogram densities |
| `pagespeed_runs`; `pagespeed_audits` | URL/strategy/analysis_utc, plus audit_id for audits |
| `bing_sites`; rank/query/page stats | site_url; site/date plus query or page_url; `BingRepository` |
| `bing_raw_items` | method/site/item_key; query/page keys include provider date, while other keys can be overwritten on subsequent fetches |
| `uptime_checks` | unique domain/checked_at; status, code, milliseconds, error; `HealthRepository` |
| `play_*` | package_name/date unique; dormant schema and SummaryRepository reads |
| `connections`; `meta` | connection ID; metadata key |

Search Console report `site_url` values are normalized bare domains, whereas Bing stores full site URLs. CrUX stores origin and URL targets separately; these are not interchangeable join keys. Deleting a connection or target generally leaves historical metrics. Most report tables have no age retention; the specific raw/PageSpeed policies below do not bound the whole database.

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

Cloudflare tokens are stored per normalized domain. Web Analytics tokens are stored per account ID. Search Console OAuth secrets are stored per client ID. CrUX, PageSpeed, and Bing keys use the `default` user key.

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

This is configuration/credential import, not historical analytics import. It runs on every launch with an existing legacy file; there is no completed-import marker. Secrets already in Numeris are preserved, and a connected status is retained, but imported config JSON and conflicting Web Analytics site tags are overwritten by legacy values. It can therefore restore deleted connections or replace newer metadata while the legacy source remains. There is no schema-shape preflight or general malformed-import recovery. GA4 and YouTube are not imported. The migration service is an explicit legacy exception to the repository-only persistence pattern.

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
- bare domain input containing internal path separators or ports (trailing slashes are stripped)
- invalid host names

Normalized outputs:

- `Domain`: lowercase bare host
- `OriginUrl`: `https://{domain}`
- `HomePageUrl`: `https://{domain}/`

`PerformanceUrl` remains the PageSpeed-facing facade and delegates to `SiteIdentity`. Page URLs require absolute HTTPS, remove fragments, preserve query strings and add a trailing slash to extensionless paths. Domain identities discard paths/ports when extracted from absolute URLs and reconstruct HTTPS origins/homepages. Bare hosts must match the DNS-style regex; localhost/IP literals are not accepted there.

Centralization is an intended invariant, with exceptions: uptime/sitemap clients, vault domain keys and parts of legacy import still use local trim/prefix/lowercase normalization. Bing AddSite accepts an HTTPS page URL, while report-domain filters reconstruct a homepage URL; non-homepage site entries may therefore sync without matching a single-domain report filter. Query strings are not a secret-safe storage boundary: avoid putting secret-bearing target URLs into configuration.

## Raw JSON and API Error Policy

`RawJsonStoragePolicy` centralizes raw upstream payload storage.

Limits:

- raw JSON input threshold: 200,000 characters
- PageSpeed audit/details input threshold: 40,000 characters
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

Over-limit strings become a JSON object containing `truncated`, `originalChars` and a shortened prefix. The prefix is serialized with JSON escaping; the final encoded wrapper is not guaranteed to stay below the nominal character threshold. These are storage-time controls, not response-download size limits.

PageSpeed retention removes older runs and matching audits in one transaction after sync. Bing retention deletes only `bing_raw_items` older than 180 days by fetched_at; embedded raw_json in normalized Bing tables is retained. CrUX raw responses are repeated with metric points and have no separate age policy.

`ApiRequestException` records provider, operation and HTTP status, classifying 404, 429, 408 and 5xx. `ApiErrorMessage` extracts selected JSON message fields, suppresses unparseable raw bodies, redacts listed query-key assignments and bearer text, collapses whitespace and truncates at 300 characters. OAuth errors add reconnect/client-configuration guidance. This is pattern-based sanitization, not arbitrary-secret detection or removal of all URLs.

The intent is that UI/database errors never disclose secrets. Current exceptions matter: Cloudflare clients throw provider message strings and depend on callers to sanitize; `UptimeClient` stores `ex.Message` through HealthRepository without this sanitizer; SitemapClient includes the failed URL in its exception, then HealthViewModel sanitizes that message. Raw payload policy applies to raw JSON, not all normalized URL/title fields. No blanket security certification follows from these helpers.

No app-owned usage telemetry/tracking pipeline was found. Network activity consists of configured provider/OAuth calls and user-triggered health fetches; provider-side logging is outside this repository's control. Local SQLite/settings are not encrypted by application code; Windows PasswordVault owns secrets separately.

## External Integrations

### Cloudflare Zone Analytics

Ownership: `CloudflarePage` -> `CloudflareViewModel` -> `CloudflareSyncService` -> `CloudflareGraphqlClient` / `CloudflareRepository`; configuration uses `CloudflareSourceViewModel`, `ConnectionsRepository` and `CredentialVault`. These files live under the corresponding Views, ViewModels, Services/Sync, Services/Api and Services/Database/Repositories directories shown in the source map.

Sources saves a normalized domain, user-entered zone ID and a per-domain bearer token; there is no automatic zone discovery. New-token Test calls GET `https://api.cloudflare.com/client/v4/zones/{zoneId}` and checks that the returned zone name matches. Saved Test also requests today's GraphQL analytics, returning a distinct successful-empty result without saving metrics.

POST `https://api.cloudflare.com/client/v4/graphql` requests:

- `httpRequests1dGroups`: date, sum.pageViews/requests/cachedRequests/cachedBytes/bytes/threats, responseStatusMap and uniq.uniques.
- Two `httpRequestsAdaptiveGroups` breakdowns by date+clientCountryName and date+clientRequestPath, with count and sum.visits, filtered to requestSource=eyeball.

Each group has limit 10,000 with no pagination. Sync clamps requested days to 1..3650; normal UI requests 7/30/90/365. Daily totals use that interval, while adaptive breakdowns cap to the latest 30 dates, with an exclusive next-day end. Provider entitlement/availability is not determined in source; no range chunking is implemented.

Sync selects saved per-domain configurations with a token and zone ID, or all matching configurations for all. Targets run sequentially. Each domain's daily totals, status codes and country/page breakdowns are written together by `UpsertTrafficAsync`, followed by a separate last_sync update. Country/page rows in the fetched breakdown interval are deleted and replaced; daily/status rows are upserted, so omitted old daily/status entries are not automatically removed. A later target failure leaves previous targets committed.

Traffic shows daily visitors/pageviews and top ten countries/pages, Cache shows ratios/bytes saved, Security shows threat totals and Status codes shows grouped responses and top issues. These are aggregate analytics, not a WAF-event explorer. Missing configuration, successful empty results and failures have separate refresh messages. No custom timeout, retry, rate limiter or caller cancellation is configured for this client; it uses the HttpClient default. Error message sanitization is applied by calling UI/test paths.

### Cloudflare Web Analytics

Ownership: the Cloudflare page/ViewModel's Web Analytics tab uses `WebAnalyticsSyncService`, `CloudflareRumClient`, `WebAnalyticsRepository` and `WebAnalyticsSourceViewModel`; DTOs include `WebAnalyticsRollup` and `WebAnalyticsSite`.

There is one account configuration and account-keyed token, separate from Zone Analytics credentials. GET `https://api.cloudflare.com/client/v4/accounts/{accountId}/rum/site_info/list` optionally discovers site tags/hosts. Parsing accepts host from a rule, standalone Host or Ruleset.ZoneName and deduplicates by host. Its error guidance distinguishes Account Settings: Read for discovery from Account Analytics: Read for GraphQL. Manual domain/site-tag mapping supports operation without discovery. Discovered mappings are upserted transactionally rather than replacing every previously saved mapping; mapping keys are domains, not account+domain.

POST to Cloudflare's GraphQL endpoint requests `rumPageloadEventsAdaptiveGroups`, filtered to siteTag and bot=0: daily visits and count (pageviews), date/refererHost visits, date/requestPath counts and date/countryName visits. Each group has limit 10,000, no pagination. Account sync clamps to 1..90 days and fetches every saved site mapping, irrespective of selected report domain. Timestamps are constructed from local calendar dates with a literal Z.

`UpsertRollupAsync` transactionally upserts daily rows and replaces all three breakdown date ranges using actual provider dates, then account last_sync advances after all mappings finish. Report queries sum the selected date range and show top ten categories. Test uses the first saved mapping and today's rollup without persisting analytics. No mappings is a configuration error, distinct from a successful empty dataset.

Deleting the account clears its config/status and matching credential but leaves site mappings and historical metrics; individual mapping deletion is separate. A switched account uses the globally saved mappings. HttpClient has no custom timeout/retry/cancellation policy; a failed site aborts later sites while earlier writes remain.

### Google Search Console

Ownership: `SearchConsolePage`, `SearchConsoleViewModel`, `SearchConsoleSourceViewModel`, `SearchConsoleSyncService`, `SearchConsoleClient`, `SearchConsoleRepository`, `SitemapRepository`, shared Google OAuth services and vault. Models are in `Numeris/Models/SearchConsoleRows.cs`.

One saved OAuth client ID identifies vault client-secret/refresh-token entries. Connect authorizes the read-only webmasters scope; Test refreshes access and lists properties. Neither action stores report data. Client metadata has a Sites field, which legacy import can populate; current Save does not preserve/discover that list. This is not a full arbitrary Google-property manager.

Sync refreshes access, GETs `https://www.googleapis.com/webmasters/v3/sites` and matches all configured domains. `PropertyForDomain` prefers sc-domain, then HTTPS/HTTP URL-prefix variants, then www variants. Unmatched domains are skipped; an otherwise completed sync can report success and update last_sync even when none matched.

POST `sites/{encodedProperty}/searchAnalytics/query` requests separate dimensions:

| Kind | Dimensions / interval |
| --- | --- |
| Daily | date |
| Query | date + query |
| Page | date + page |
| Country | date + country |
| Device | date + device |
| PageQuery | page + query, always the last 28 inclusive dates |

First five use selected days clamped to 1..365, ending today. Each request uses rowLimit=5000; no startRow pagination, search-type selector or data-state selector is sent. Provider caps/delay and truncated responses are not repaired by backfill. Data is stored under bare domain, not original property URL. The repository deletes/reinserts the requested kind/window; device rows also populate search_devices. Page/query pairs replace their exact 28-day window in search_page_queries. These replacements are serialized but **not transactional**. Country data and page/query pairs are fetched/stored but have no dedicated current report view.

Queries loads up to 100 rows, sorts by clicks/impressions/CTR/position and applies case-insensitive text filtering only to that loaded set. Pages loads 50; new queries (20) and declining pages (10, >=5 previous clicks) appear only for a single domain. Devices shows clicks by device. Aggregation details are in the metric section.

**Indexing is separate:** Health first discovers active sitemap URLs. On Search Indexing refresh, analytics sync/reload is followed by `InspectIndexingAsync`; all mode enumerates domains with active sitemap rows. `InspectSitemapUrlsAsync` reacquires OAuth access/properties for each domain and POSTs each active URL to `https://searchconsole.googleapis.com/v1/urlInspection/index:inspect`, supplying inspectionUrl and siteUrl. It stores verdict, coverage_state, indexing_state, robots_txt_state, page_fetch_state, crawled_as, last_crawl_time and last_inspected_at.

Inspection runs sequentially with a 15-second SendAsync timeout per URL. It reports per-URL progress/errors and stops after three consecutive failures only while no URL has yet succeeded. There is no daily quota cap, general retry/backoff or user cancellation. Normal Search/OAuth HTTP calls use default HttpClient timeout. The UI retains partial inspections and reports all-failed/partial states; sitemap presence or search clicks never substitute for an inspection result.

### Web Performance

Ownership: `PerformancePage`, `PerformanceViewModel`, `PerformanceSourceViewModel`, `PerformanceSyncService`, `CruxClient`, `PageSpeedClient`, `PerformanceRepository`, `PerformanceUrl` and `Numeris/Models/PerformanceRows.cs`.

Sources saves separate global CrUX/PageSpeed keys in the vault and metadata-only performance registry rows, and adds/deletes normalized HTTPS target URLs. Enabled targets in performance_urls drive sync; current UI provides add/delete rather than an enable toggle. No key is required merely to add a target. Deleting keys/targets leaves reports; startup reinserts missing baseline targets.

**CrUX:** POST `https://chromeuxreport.googleapis.com/v1/records:queryHistoryRecord` with API key query parameter and origin or URL plus optional formFactor. Sync requests each distinct origin and every enabled URL for ALL (omitted formFactor), PHONE, DESKTOP and TABLET. There is no explicit date-range, history-count or metric filter in the request: returned collection periods and metrics define available history. It stores each metric's p75 and good/needs-improvement/poor histogram densities by collection period, plus redacted raw response. Numeric strings are parsed invariantly; null/NaN strings become missing, malformed strings fail. Timeout is 30 seconds, without retry.

CrUX 404 skips that target/form-factor and does not write a “missing” row or delete old data. Test checks PHONE history for configured origins then URLs; if every target returns 404, the UI reports the key as working but no field data found. That message is an implementation policy, not proof that every 404 identifies valid authorization.

**PageSpeed:** GET `https://pagespeedonline.googleapis.com/pagespeedonline/v5/runPagespeed` sends target URL, key, MOBILE/DESKTOP strategy and PERFORMANCE, ACCESSIBILITY, BEST_PRACTICES, SEO categories. Timeout is 75 seconds. Every enabled URL gets both strategies; selected report period/domain does not constrain fetching. Results include analysis UTC, final URL, category scores, Lighthouse version, runtime errors/warnings, all returned audits (score/value/unit/display mode/details) and raw JSON. Repository writes a run and its audits atomically.

The sync-only PageSpeed path uses one semaphore, >=1 second between attempt starts and at most three attempts, backing off 2 then 4 seconds for 429/408/5xx, HttpRequestException or TaskCanceledException. It does not use Retry-After or caller cancellation. Exhausted transient failures increment PageSpeedErrors and continue; other API failures escape. Test calls the client directly for the first enabled URL/MOBILE and does not share retry/spacing.

Sync performs CrUX before PageSpeed. A non-404 CrUX error aborts the remaining workflow. After PageSpeed, retention keeps 30 runs per URL/strategy and deletes matching older audits; registry perf and configured subproviders receive the same sync timestamp captured near the start, even with skipped CrUX targets or counted PageSpeed errors.

Overview/CrUX/PageSpeed/URLs tabs expose latest core vitals, metric/form-factor trends, latest run tables, score trends and up to 50 audits with score <0.9 ordered by severity/value. Latest data can be older than the selected period. There is no performance URL crawler or automatic sitemap-to-performance import.

### Bing Webmaster

Ownership: `BingPage`, `BingViewModel`, `BingSourceViewModel`, `BingWebmasterSyncService`, `BingWebmasterClient`, `BingRepository` and `Numeris/Models/BingRows.cs`.

One global API key is stored in the vault. Sources manually adds/deletes HTTPS site URLs, maintains matching Sites config metadata, saves/deletes the key and tests GetUserSites. Test does not save/discover sync targets. Sync stores GetUserSites as raw data but does not automatically populate bing_sites from it; configured enabled sites remain authoritative.

GET `https://ssl.bing.com/webmaster/api.svc/json/{method}` uses an API-key query parameter. Implemented automatic operations are GetUserSites once, then GetCrawlIssues, GetCrawlStats, GetRankAndTrafficStats, GetQueryStats and GetPageStats for each enabled site with siteUrl. No selected-date parameters, pagination or detail-method expansion is sent. Provider-returned Date values, not the UI interval, determine stored history. Timeout is 30 seconds, without retry/rate limiter/caller cancellation.

WCF /Date(milliseconds[timezone])/ values, ISO dates and ISO timestamps normalize to UTC ISO date; malformed dates fail explicitly. Rank/query/page records and their associated sanitized raw item are committed as pairs. Query/page raw keys include date. The raw-only method path stores an error item then rethrows; failure is not counted as saved data. Earlier site/method results remain committed, while last_sync updates only on completed sync.

Overview displays clicks/impressions/CTR; Queries loads 100 sorted by clicks/impressions/CTR/position; Pages loads 50; Crawl displays method counts/fetched times and up to 50 GetCrawlIssues raw entries. Crawl is not filtered by selected period. Null counts aggregate to zero; position averages are traffic-weighted. Raw retention is 180 days by fetch time, not report date.

`BING-WEBMASTER-SYNC.md` deliberately limits scope. Additional detail methods require a separately verified request contract; do not infer support from a generic client method name.

### Health, Uptime, and Sitemaps

Ownership: `HealthPage` -> `HealthViewModel` -> `UptimeClient` / `SitemapClient` / `HealthRepository` / `SitemapRepository`. Health requires no provider credential and runs only on explicit actions, not continuous monitoring.

Uptime performs an HTTPS GET to the selected domain's root, allows up to five redirects and has a 12-second timeout. Final 2xx or 3xx is up; other codes or exceptions are down. Status/code/elapsed milliseconds and error_message are stored per domain/local timestamp; exceptions have null code/time. All mode checks the shell's available domains sequentially. No retry, scheduled probing, notification service or SLA calculation is implemented.

Sitemap discovery starts only at `https://{domain}/sitemap-index.xml`, extracts loc entries (standard sitemap namespace with unnamespaced fallback), fetches each child URL and deduplicates/sorts its loc entries. Each request has a 20-second timeout. It does not try robots.txt or sitemap.xml, recurse through nested indexes, enforce same-origin child targets or impose an explicit response-size/URL-count limit. Child URLs come from fetched XML.

A completed domain fetch calls `HealthRepository.RefreshSitemapAsync`, transactionally marking absent active URLs removed, refreshing last_seen_at and inserting/reactivating current URLs. Existing active rows retain inspection fields; reappearing removed rows are inserted/replaced and can lose old inspection fields. Failed fetches do not replace that domain's stored sitemap. All mode continues across per-domain sitemap failures. UI lists only active URLs, with counts and latest last_seen_at.

Sitemap discovery establishes URL membership, **not Google indexing**. URL Inspection is initiated by Search Console's Indexing refresh and persists its own fields in the same table. Neither health fetch has a user cancellation path.

## Google OAuth Design

Google OAuth is centralized in `Numeris/Services/Auth/GoogleOAuthClient.cs` and `GoogleOAuthFlow.cs`.

- Authorization uses `https://accounts.google.com/o/oauth2/v2/auth`, authorization-code response, caller-supplied scopes, `access_type=offline`, `prompt=consent` and a GUID-containing state.
- Search Console requests only `https://www.googleapis.com/auth/webmasters.readonly`.
- A TcpListener binds IPv4 loopback on an ephemeral port with redirect path `/oauth2callback`; Windows Launcher opens the browser.
- The default two-minute cancellation token covers accepting a connection and reading the callback line/headers. State mismatch, denial or a missing code rejects the callback. Browser text acknowledges receipt, not a completed token exchange.
- Code exchange and refresh post form data to `https://oauth2.googleapis.com/token`. A successful JSON response must contain a nonblank `access_token`; `refresh_token` is optional. Access tokens stay in memory; Connect stores a returned refresh token, otherwise an existing one remains.
- The listener closes in finally. Token HTTP calls use their HttpClient timeout, not the callback cancellation token.

Limits visible in source: no PKCE fields; only the first accepted callback connection is processed; request method/path/Host are not explicitly validated, and request headers have no explicit size/count bound. State checking is present but is not equivalent to those additional checks. Saving a client does not discover and persist Google properties; Test lists them only for its result message, and sync lists them for property matching.

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
- `PerformanceSourceViewModel`
- `BingSourceViewModel`

Tests explicitly prevent `SourcesViewModel` from depending on low-level clients or the vault.

## Refresh and Sync Behavior

Opening a report or changing shell domain/period calls `LoadAsync`, which reads local repositories. Explicit refresh uses the following separate paths:

| UI action | Actual fetch and reload behavior |
| --- | --- |
| Overview refresh | `DashboardViewModel.RefreshAsync` sequentially tries Cloudflare Zone Analytics, Google Search, Bing and Performance, catches each source failure, then reloads if any source reported an update. It does **not** sync Web Analytics, uptime, sitemaps or URL Inspection. |
| Cloudflare refresh | On `webanalytics`, only `WebAnalyticsSyncService.SyncConfiguredAsync(days)`; other tabs call only `CloudflareSyncService.SyncConfiguredAsync(selectedDomain, days)`. Reloads cached data after a result. |
| Google Search refresh | `SearchConsoleSyncService.SyncConfiguredAsync(days)`, local reload, then `InspectIndexingAsync` only when the active tab is `indexing`. Missing configuration prevents both sync and inspection. |
| Bing refresh | `BingWebmasterSyncService.SyncConfiguredAsync()` for all enabled sites, then local reload. |
| Performance refresh | `PerformanceSyncService.SyncConfiguredAsync()` for all enabled URLs and available keys, then local reload. |
| Health refresh | Active Uptime tab runs probes; Sitemap tab discovers URLs. Dedicated Check now / Refresh sitemap buttons call the same guarded actions. |
| Sources | Save/connect/test/discover/add/delete only. Tests make provider requests but do not persist report metrics; Web Analytics discovery does persist mappings. |

Zone Analytics and Health fetches directly respect the selected domain; URL Inspection also uses the selected domain or all domains with active sitemap URLs. Ordinary Search Analytics sync matches **all configured domains** against authorized Google properties. Web Analytics sync covers all saved mappings for its one account. Bing and Performance sync cover all enabled targets, irrespective of report domain/period. The report reload then applies display filters. Overview follows those same service scopes.

Report `CanRefresh` combines loading/refresh flags (plus inspection for Search); Health's three flags guard all its actions. Refresh handlers return early when busy and publish configuration warnings or sanitized failures via InfoBar/status text. Sources have per-source `CanRun` guards. These are instance/UI guards, not cancellation or a global workflow lock.

On failures, existing collections generally remain visible, but “stored data” is not a transactional snapshot guarantee: earlier provider writes may have committed, reload may not run after a thrown sync, and asynchronous selection loads can race. Health catches sitemap failures per site and can retain other successful sites. Overview separately reports updated, missing and failed sources; Performance also reports counted PageSpeed failures. A non-null/no-error result is not proof of data completeness.

Connection last_sync normally advances at the end of the service after its persistence calls, but its value can have been captured before network work. FormatNow uses local time without an offset, so it is not a guaranteed UTC completion instant. Zero-data successful requests can still advance it; Performance can advance it with no CrUX field data or counted PageSpeed errors. Overview's displayed last sync is the maximum over Cloudflare and selected search/performance/Bing registry rows, globally rather than domain-filtered; it excludes Web Analytics and health. Data coverage dates on traffic/search cards describe stored metric dates, which differ from sync timestamps.

## Overview Insights

Overview Insights remain deterministic and local even though Overview refresh now also fetches providers. There is no AI model, prompt, additional Insight API request or Insight table.

Main files are `Numeris/Models/InsightRows.cs`, `Numeris/Services/Insights/Trend.cs`, `Numeris/Services/Insights/InsightEngine.cs`, `Numeris/Services/Database/Repositories/InsightMetricsRepository.cs`, `Numeris/ViewModels/DashboardViewModel.cs` and `Numeris/Views/DashboardPage.xaml`.

The repository reads current and immediately preceding equal-day windows for Cloudflare visitors/threats/cache ratios, Google clicks/impressions/mobile metrics, Bing clicks/impressions and average mobile PageSpeed run scores. HTTP status errors use the current window. Sitemap inspection and source freshness are current snapshots. Declining pages/new queries are computed only for a single selected domain. No GA4 input remains.

`InsightEngine.Generate` evaluates eleven rules, sorts by descending severity then ascending numeric priority and takes at most four cards. Cards contain title, explanation, WhyShown evidence, NextStep text and severity; NextStep is advisory text, not a navigation command.

| Rule | Current source condition |
| --- | --- |
| Stale sources | Non-disconnected tracked sources have no last_sync or a timestamp older than 48 hours |
| Google visibility without clicks | Current impressions >=100, increase >=25%, clicks within +/-5% with a nonempty window |
| Declining page | Worst page had >=5 previous clicks and lost >=5 clicks or >=25% |
| New queries | Single-domain query absent from previous window has current clicks >0 |
| Mobile risk | Mobile Google clicks or impressions up >=20%, combined with mobile PageSpeed score down >=10% or >0 and <0.70 |
| Indexing issue | Active inspected URLs include >=3 non-indexed URLs or >=20% non-indexed |
| Missing indexing | >=5 active sitemap URLs with zero inspected URLs |
| HTTP errors | >=100 requests, then 5xx >=1% or >=10 responses gives Critical; otherwise 4xx >=5% gives Warning |
| Bing visibility without clicks | >=50 current impressions and zero clicks |
| Cache efficiency | >=100 requests, current hit ratio <0.50 and relative drop >=15% |
| Threat spike | >=10 threats and >=50% growth or NewActivity |

Math uses ratios, not percentage points: `Trend.ChangeRatio` is (current-previous)/previous when previous is nonzero. Zero-to-positive is `NewActivity`, both zero is `NoData`; neither invents +100%. `Trend.IsFlat` additionally checks its tolerance; Classify's intermediate Flat state alone is broader. Display rounds signed percentages with invariant formatting.

Limits: these metrics coalesce absent sums to zero and do not apply the Overview KPI completeness gate, so missing prior data can look like a new query or decline. Freshness is global and reads only IDs cf/wa/sc/crux/pagespeed/bing with status != disconnected; configured rows count, while per-domain cloudflare:* rows are omitted. A stale-source warning does not suppress other cards. The empty-state text “All connected sources look consistent...” only means no rule fired with some tracked data; it is not an integration or data-quality certificate.

### Metric semantics outside Insights

- Overview Cloudflare visitors sum daily uniques, including across selected sites; this is not deduplicated period-wide people. Cards report distinct stored dates and latest date. Change badges require both periods to contain exactly the requested number of distinct dates and previous total >0. This check does not prove every selected domain has complete rows.
- Google daily totals prefer kind=daily; a legacy fallback uses other kinds only when no daily rows exist in the domain scope, not per missing date. Queries/pages/countries are separate breakdowns and must not be added to daily totals. Position is impression-weighted; CTR is total clicks / total impressions. Bing position is weighted by clicks or impressions as appropriate, excluding null-position weights.
- Cloudflare cache ratio is sum(cached_requests)/sum(requests), not average daily percentages. Status codes are grouped into successful 2xx/3xx, client 4xx and server 5xx with counts/shares and top error codes. Country/page adaptive values prefer sum.visits when positive and otherwise count; despite storage/UI names, those values are not guaranteed to mean daily unique visitors or pageviews.
- Overview mobile PageSpeed is the single most recent matching MOBILE run. Performance summaries show the minimum-to-maximum score of latest runs per URL/strategy; scores stored as 0..1 display as 0..100. Missing scores remain nullable. PageSpeed trends group by exact analysis timestamp, using MIN across all domains and AVG for a single domain.
- Latest CrUX summaries use latest points per target/form-factor/metric and the worst known status, without period/freshness filtering. LCP thresholds are <=2500/<=4000 ms, INP <=200/<=500 ms, CLS <=0.10/<=0.25 for Pass/Warn, otherwise Fail; null is No data. CrUX trends use MAX(p75) per collection-end/form-factor/metric across matching targets, not a recomputed population percentile.
- Latest performance cards and audits ignore the selected period; trend queries use it. The URLs tab lists all configured URLs without the domain filter.
- Overview's indexing strip uses knittoolsapp.com when domain=all; Search Indexing and Insights use all matching active URLs. Inspected means an inspection status field is present; indexed means PASS or coverage text starting “Indexed”. This is not Google's complete Page indexing aggregate.
- Uptime domain percentages/incidents use all stored probes; charts use the selected period. A failed probe counts as an incident sample, not a distinct outage. Daily response averages coalesce missing milliseconds to zero and the all-domain chart averages domain/day averages without probe-count weighting.

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
- line-chart surfaces through `ChartTheme.CreateChartSurface(chart)` and bar surfaces through `CreateBarChartSurface(chart)`.

Loading flags drive progress UI and disable refresh; explicit refresh InfoBars distinguish missing configuration, failures and results. Overview has No data/Not synced states and nullable changes; HorizontalBars shows No data; CrUX uses No data; PageSpeed preserves null scores as dashes. Some detail tables/charts simply render empty collections or zero aggregate totals, so there is no universal missing-versus-zero contract.

Pages have adaptive narrow/wide states using a 1180 window-width breakpoint, scrolling, wrapping and changes to header/KPI grids. Tokens also declare a 760 breakpoint, but its presence alone is not proof every page uses it. Fixed-width filters, table columns and an always-open 220-wide navigation pane remain. Automation names exist on report selectors/refresh/filter/sort controls, status bars and Insight rows; PeriodSelector adds full-label tooltips and automation names to segment buttons. PerMonitorV2 is a manifest setting. Keyboard, screen reader, contrast, high-contrast, text scaling and small-window behavior were not exercised.

Pages with `SelectorBar`:

- Cloudflare
- Google Search
- Bing
- Performance
- Health

Nested `NavigationView` controls are not used for report-local tabs.

## Sources Page

`SourcesPage` is configuration, credential testing and target management, not a report page or live-report sync launcher. It groups Cloudflare (Zone Analytics and Web Analytics), Google (Search Console and Web Performance) and Bing in shared settings cards.

| Source ViewModel under `Numeris/ViewModels/Sources/` | Exposed actions |
| --- | --- |
| `CloudflareSourceViewModel` | Test unsaved token/zone/domain, save, test saved connection, delete per-domain config/token |
| `WebAnalyticsSourceViewModel` | Save account/token, test first mapping, optional discover sites, manual add/delete mapping, delete account credential/config |
| `SearchConsoleSourceViewModel` | Save OAuth client/secret, Connect Google account, test authorization, delete client metadata and its two secrets |
| `PerformanceSourceViewModel` | Save either/both keys, test CrUX/PageSpeed separately, add/delete URL, delete both keys and performance config |
| `BingSourceViewModel` | Save key, test GetUserSites, add/delete site and update config, delete key/config |

Collapsed Expanders contain credential forms and PasswordBoxes. Code-behind explicitly copies passwords into ViewModel fields before relevant actions and clears corresponding boxes after saves; saved secrets are not loaded back into form fields. Blank secret fields retain existing vault values during Save. Source operations use IsBusy guards, HasStatusMessage, InfoBar and DangerActionButtonStyle for deletes. Status text, rather than a universal structured severity model, distinguishes many source errors.

Save often sets LastValidatedAt to the save time without a validation call. “Configured”, “Authorized” and “Connected” are distinct from a completed data fetch. Test does not generally update registry status/last_sync. Credentials and SQLite writes span separate stores and are not atomic together; account/client changes can leave older credential entries. Delete removes the current matching secret/config, not all historical provider data. There is no confirmation dialog in this source path.

`SourcesViewModel` only coordinates five source ViewModels and concurrent local loads. It does not own low-level clients or credentials. Report refresh buttons own data fetching.

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
- Horizontal bar visualizations use `HorizontalBars`, which scales to the largest value, optionally highlights tied top values, supports compact sizing and rerenders on collection changes. It detaches the previous collection handler when Items changes.
- Shared typography uses Segoe UI Variable (page title 32, section title 18); spacing tokens run 4/8/12/16/20/24/32, page padding is 32,24,32,32, cards use radius 8 and title bar height 48.
- `KpiCard` renders label/value/detail and hides unavailable delta; `DeltaBadge` uses signed arrows, optional inverted positive/negative semantics and tokenized colors.
- Current navigation and provider headers use PNG ImageIcon assets, not Fluent PathIcon resources. The app project still packages the seven active *-filled.png icons.
- There is no theme preference or explicit RequestedTheme in App.xaml. The custom dark palette is a single resource dictionary, not separate light/high-contrast ThemeDictionaries. Native defaults and custom resources therefore still require runtime theme validation.
- Most UI copy is English, but number formatting is mixed: OneDecimalConverter and Insight formatting use invariant culture while some report totals and HorizontalBars use CurrentCulture.

## Charting

LiveCharts construction is centralized in `Numeris.Themes.ChartTheme` and color access in `Numeris.Themes.ChartPalette`.

`ChartTheme` owns:

- `CreateCartesianChart()`
- `CreateChartSurface()` and `CreateBarChartSurface()`
- `StyleXAxis()`
- `StyleYAxis()`
- `CreateMatteColumnSeries()`
- `CreateMutedColumnSeries()`
- `StyleStackedColumnSeries()`

ViewModels construct series from repository rows; chart controls, surfaces, axes, legend/tooltip paints and shared colors use central helpers. “Matte” does not mean gradient-free: central bar fills use three-stop gradients, rounded corners, no outline, width/padding tokens, optional highlights and a 420 ms CubicOut animation. Bar surfaces hide the legend and contain no mesh. Generic line-chart surfaces still include a subtle mesh backdrop (ChartMeshOpacity=0.05). That mesh is not a shell/background bitmap. Missing required brush/color resources throw rather than silently picking fallback colors.

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

`ConnectionsRepository.GetPerformanceAsync()` combines perf/crux/pagespeed: any connected wins, otherwise configured, otherwise disconnected; its last sync is the latest of those rows. Has-key flags are read from the vault. These states do not validate credentials on read.

Per-domain Cloudflare sync updates cloudflare:{domain}, not the seeded cf placeholder. Singleton upserts generally preserve earlier last_sync even when status becomes configured. Deletion resets singleton config and last_sync; Cloudflare deletes its domain row. Malformed connection JSON is skipped or read as absent config.

## Tests

`Numeris.Tests/Numeris.Tests.csproj` is still a **custom console executable**, with a direct project reference to the WinUI WinExe, the same net8.0-windows10.0.19041.0 target, preview language, nullable enabled, win-x64/x64 and framework-dependent unpackaged settings. There is no test SDK, xUnit, NUnit or MSTest declaration.

`Numeris.Tests/Program.cs` is the top-level entry point. Run(name, Action) prints PASS/FAIL, catches each failure and sets Environment.ExitCode=1 while continuing. Many checks read repository source text; others run pure functions, ViewModels or in-memory SQLite using reflection/uninitialized objects to bypass production constructors. This does not exercise normal startup, XAML rendering or live providers. `FindRepositoryRoot` walks up from executable output looking for Numeris.slnx; execution outside that layout is not supported by the source checks. Do not assume dotnet test runs this harness.

Coverage expressed in current sources:

| Source | Categories actually encoded |
| --- | --- |
| `Program.cs` | normalization, Trend/Insight rules and ranking, raw redaction/bounds, API errors, architecture/layering, migration markers, no mock/YouTube, OAuth ownership, UI resources/layout/automation conventions, refresh call paths, scripts |
| `AnalyticsRemovalRegressionTests.cs` | GA4 migration preserves other sources, data clearing remains usable, removed navigation/settings fallback |
| `DomainSelectionRegressionTests.cs` | configured-domain discovery, save/delete refresh, non-Cloudflare sites |
| `DailyBreakdownRegressionTests.cs` | actual breakdown dates/range replacement, Web Analytics rollback, discovery atomicity, v9 cleanup, weighted Search/Bing metrics |
| `CloudflareRefreshRegressionTests.cs` | standalone RUM host parsing, missing config/failure/busy refresh paths |
| `HealthRegressionTests.cs` | sitemap rollback/domain switching/batched collection updates, overlapping actions, all-site indexing failure handling, Bing failure propagation |
| `OAuthRegressionTests.cs` | local loopback callback timeout/incomplete request, valid state/denial/mismatch, token parsing/access-token requirement, readonly/offline authorization URL |
| `OverviewRegressionTests.cs` | exact date windows, missing/partial comparison data, Bing dates/migration, Cloudflare adaptive range, Overview refresh outcomes |
| `ReportRefreshRegressionTests.cs` | CrUX numeric parsing, nullable/ranged PageSpeed scores, Search/Bing/Performance failure/configuration/busy states |
| `SourceActionRegressionTests.cs` | source action overlap guards and delete-error reporting |
| `SourcesPersistenceRegressionTests.cs` | opt-in credential save/replace/reload/delete, URL/site persistence and metadata secret exclusion |

The optional `--sources-persistence` group runs **in addition to** default checks. It requires an isolated build whose vault resource starts with Numeris.QA.; ordinary production constants fail its guard. Its fixture uses real PasswordVault plus in-memory SQLite and SettingsStore; it is not a pure mock-only test. No repository build switch that creates this isolated credential configuration was found, so this document does not prescribe running that option against the normal checkout.

Default harness OAuth checks use local sockets, not Google consent. Program also invokes both helper scripts with `-ResolveOnly` through pwsh to check root resolution; this differs from executing scans but still matters where all script invocation is prohibited. The harness does not provide standard per-test filtering, coverage reports, test-adapter discovery or visual acceptance. Source-text assertions can prove only expected code patterns, not behavior under all failures.

**Evidence separation:** these are tests that exist, not tests run on this review. No CI workflow was found. `INSIGHTS-IMPLEMENTATION-PLAN.md` records historical 2026-06-08 build/harness claims; they were not rerun or accepted as proof for this dirty snapshot. Older maintainability notes propose a framework/Core-library split and include an obsolete mock-seeding smoke check; those are not current implementation requirements.

## Lint and Security Scripts

The user owns `tools/lint-check.ps1` and `tools/security-check.ps1` runs through PowerShell profile functions `function lc { lint-check @args }` and `function sc { security-check @args }`. The sc function avoids collision with Windows sc.exe. Do not infer installed profile shims from this repository; preserve the project restriction that the assistant does not run these checks on its own.

Scripts accept Root and ResolveOnly, resolve a Git repository from child directories and write ignored reports. Lint reports are ktlint.txt, detekt.txt and lint.txt under reports/. Without a Gradle executable/wrapper it skips Kotlin checks and selects dotnet format on the solution with --verify-no-changes --verbosity diagnostic. Its detection also accepts a global gradle command; the script is generic, not a dedicated WinUI validator.

Security selects Semgrep p/csharp for a solution with --error --metrics=off, then OWASP dependency-check if present; otherwise the .NET fallback is dotnet list <solution> package --vulnerable --include-transitive. It writes security-code.txt/security-deps.txt and any dependency-check outputs under reports/. Missing tools may be SKIPPED, not passed. No scanner or report generation occurred in this task.

## Development, Build and Publishing Workflows

Verified repository entry points, **listed only, not executed**:

| Purpose | Command or configured entry point |
| --- | --- |
| Solution build | `dotnet build Numeris.slnx -p:Platform=x64` |
| Custom harness | `dotnet run --project Numeris.Tests/Numeris.Tests.csproj -p:Platform=x64` |
| Unpackaged app | Visual Studio profile “Numeris (Unpackaged)” in `Numeris/Properties/launchSettings.json` |
| Packaged app | Visual Studio profile “Numeris (Package)” plus project MSIX tooling/manifest; runtime/package validation still required |
| FileSystem publish | `dotnet publish Numeris/Numeris.csproj -c Release -p:PublishProfile=win-x64`; corresponding win-x86/win-arm64 profiles exist |
| Documentation whitespace | `git diff --check -- PROJECT.md` |

Use Windows with .NET/WinUI build tooling capable of this target, preview C# partial properties and SLNX, plus Windows SDK tooling required by project references. There is no repository SDK pin from which to infer an exact minimum installed SDK/Visual Studio version. Framework-dependent execution requires compatible runtimes; FileSystem profiles explicitly request self-contained .NET publishing. That setting alone does not establish Windows App SDK runtime bundling. No signing certificate, validated Store release pipeline, automatic updater or CI distribution workflow is established by the repository.

Opening/building/publishing may restore dependencies and generate output; running the app initializes/migrates local storage and imports credentials. Those actions need their own authorized validation context and were outside this review.

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
- YouTube and GA4 must not be reintroduced without a new explicit decision and migration plan.

These are maintenance constraints, not assertions that every existing path fully enforces them; documented exceptions and limitations above remain part of the current implementation.

## Current Non-Goals and Gaps Visible in Code

- Play Store is dormant, not removed: four tables, ps registry row, Domains.PlayStorePackage and SummaryRepository calculations/DTO fields remain. No live client, sync service, source editor or page populates/displays it.
- YouTube and GA4 are removed, with distinct v6/v11 cleanup. Old planning/memory descriptions of their pages must not be treated as active scope.
- Insights is rule-based; `aivoiceaction-plan.md` is planning material, not evidence of implemented AI/voice actions. No corresponding DI/routes/model execution was found.
- Two default sites are baseline configuration, not a complete domain allowlist. Search Console still lacks a dedicated property-target manager; discovery during sync does not persist every authorized property as a configured domain.
- All time is bounded; display filters do not constrain every provider fetch, and performance/health snapshot cards are not uniformly period-scoped.
- Pagination, provider quotas, complete historical backfill, global sync cancellation and scheduled sync are not implemented generally. No amount of local source inspection validates account entitlement or API compatibility.
- Legacy import can overwrite metadata repeatedly; default deleted targets can return at startup. Data clearing and disconnection are not equivalent to deleting all local data or credentials.
- Repository-only writes and atomic logical updates are intended invariants with documented exceptions in legacy import, Search Console and registry helpers.
- Error/redaction helpers are partial safeguards; raw truncation thresholds are not strict final-byte limits, and uptime errors bypass the central sanitizer.
- Static UI states, automation names and DPI declarations do not prove accessibility, responsive behavior or rendering. Current source still has fixed dimensions, mixed locale formatting and no user theme setting.
- Historical documentation conflicts exist: memory describes past backdrop/icon/integration designs; TESTING_AND_MAINTAINABILITY suggests mock startup; older plans describe Sources sync. Current source and this document's qualified findings take precedence. No other documentation was edited to hide those conflicts.

## Source-of-Truth and Change-Impact Map

| Area | Authoritative repository-relative files / symbols |
| --- | --- |
| Startup / DI | `Numeris/App.xaml.cs`: OnLaunched, ConfigureServices; `Numeris/App.xaml` resources |
| Navigation / shared state | `Numeris/MainWindow.xaml`, `Numeris/MainWindow.xaml.cs`: Routes; `Numeris/ViewModels/ShellViewModel.cs`; `Numeris/Services/Settings/SettingsStore.cs` |
| Dates / domains | `Numeris/Models/Period.cs`; `Numeris/Helpers/SiteIdentity.cs`, `Numeris/Helpers/Domains.cs`; `Numeris/Services/Performance/PerformanceUrl.cs`; `Numeris/Services/Database/Repositories/ConnectionsRepository.cs`: ListConfiguredDomainsAsync |
| Storage / migrations | `Numeris/Helpers/AppPaths.cs`; `Numeris/Services/Database/SqliteDatabase.cs`, `Numeris/Services/Database/Migrations.cs`: RunAll, RunPendingMigrations, CurrentSchemaVersion |
| Credentials / legacy | `Numeris/Services/Secrets/CredentialVault.cs`; `Numeris/Services/Migration/LegacyDataMigrationService.cs`, `Numeris/Services/Migration/LegacyCredentialReader.cs`; `Numeris/Models/ConnectionConfigs.cs` |
| OAuth / error policy | `Numeris/Services/Auth/GoogleOAuthClient.cs`, `Numeris/Services/Auth/GoogleOAuthFlow.cs`; `Numeris/Services/Api/ApiRequestException.cs`, `Numeris/Services/Api/ApiErrorMessage.cs`; `Numeris/Services/Database/RawJsonStoragePolicy.cs` |
| Provider sync | `Numeris/Services/Sync/CloudflareSyncService.cs`, `WebAnalyticsSyncService.cs`, `SearchConsoleSyncService.cs`, `PerformanceSyncService.cs`, `BingWebmasterSyncService.cs` in that directory; corresponding clients in `Numeris/Services/Api/` and repositories in `Numeris/Services/Database/Repositories/` |
| Health / indexing | `Numeris/ViewModels/HealthViewModel.cs`; `Numeris/Services/Api/UptimeClient.cs`, `Numeris/Services/Api/SitemapClient.cs`; `Numeris/Services/Database/Repositories/HealthRepository.cs`, `Numeris/Services/Database/Repositories/SitemapRepository.cs`; SearchConsoleSyncService.InspectSitemapUrlsAsync |
| Metrics / Insights | `Numeris/Services/Database/Repositories/SummaryRepository.cs`, `Numeris/Services/Database/Repositories/InsightMetricsRepository.cs`; `Numeris/Services/Insights/InsightEngine.cs`, `Numeris/Services/Insights/Trend.cs`; `Numeris/Models/InsightRows.cs` |
| Refresh / forms | `Numeris/ViewModels/DashboardViewModel.cs` and other report ViewModels; `Numeris/ViewModels/SourcesViewModel.cs`, `Numeris/ViewModels/Sources/`; `Numeris/Views/` XAML and code-behind |
| Visuals / controls | `Numeris/Themes/Tokens.xaml`, `Numeris/Themes/ChartTheme.cs`, `Numeris/Themes/ChartPalette.cs`; `Numeris/Controls/PeriodSelector.xaml.cs`, `Numeris/Controls/HorizontalBars.xaml.cs`, `Numeris/Controls/KpiCard.xaml.cs`, `Numeris/Controls/DeltaBadge.xaml.cs`; `Numeris/Converters/NumberConverters.cs` |
| Tests / packaging | `Numeris.Tests/Program.cs`, `Numeris.Tests/Numeris.Tests.csproj`; `Numeris.slnx`, `Numeris/Numeris.csproj`, `Numeris/Package.appxmanifest`, `Numeris/app.manifest`; `Numeris/Properties/PublishProfiles/` |
| Local checks / intent | `tools/lint-check.ps1`, `tools/security-check.ps1`; `AGENTS.md`, `BING-WEBMASTER-SYNC.md`, `migration-plan.md` |

Follow each changed user action through page event, ViewModel guard/state, service scope, API request shape and repository keys/transaction. A metadata/target change can affect both domain discovery and report filtering; a schema change affects startup and existing installations; a token/style change affects every shared chart/control. Test patterns are supplemental evidence, not a replacement for these paths.

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
- Does any change reintroduce active YouTube or GA4 code/registry rows, or mistake dormant Play Store storage for an implemented integration?

## External Documentation Checked

The previous PROJECT.md recorded the following official references as checked on **2026-06-09**. This is retained historical attribution, not a new check or confirmation of current external API contracts. No external documentation/link validation was performed on 2026-10-07:

- WinUI 3: https://learn.microsoft.com/windows/apps/winui/
- Windows App SDK: https://learn.microsoft.com/windows/apps/windows-app-sdk/
- Unpackaged WinUI apps: https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app
- Windows App SDK runtime for unpackaged apps: https://learn.microsoft.com/windows/apps/windows-app-sdk/use-windows-app-sdk-run-time
- System backdrops: https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops
- Cloudflare GraphQL Analytics API: https://developers.cloudflare.com/analytics/graphql-api/
- Bing Webmaster API: https://learn.microsoft.com/bingwebmaster/
- Bing Webmaster supported API protocols: https://learn.microsoft.com/bingwebmaster/api-protocols


## Documentation Review Checks

The 2026-10-07 review covered every prior PROJECT.md section and the current production directory tree, project/manifests/profiles, schema/repositories, API/sync/auth/legacy paths, all seven page routes and source editors, shared controls/themes, test entry point/regression sources and local helper scripts. Planning/memory claims were compared against current implementation rather than copied as execution proof.

Only root PROJECT.md was authored in this task. Documentation validation included a full document read, source/symbol/command and internal-consistency review, complete diff review and `git diff --check -- PROJECT.md` without whitespace errors. Every previous top-level section remains covered; all 64 checked explicit repository paths exist and all eight direct package versions match the project file. Git state and hashes of existing non-ignored files were compared with the initial snapshot: only PROJECT.md changed, with no added or removed files. These are documentation checks, not application execution. Builds, tests, live integrations and user-run lint/security scripts remain **not run** for this review.
