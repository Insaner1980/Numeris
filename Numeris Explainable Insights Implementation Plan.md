# Numeris Explainable Insights Implementation Plan

## Summary

Build a deterministic, rule-based `Insights` feature for Numeris Overview. It must use only Numeris' existing SQLite-backed analytics data, not AI models, not raw prompts, and not new external API calls.

The feature adds a small `Insights` card to `DashboardPage` that shows 0-4 plain-English observations. Each observation explains what it means, why it appeared, and what the user should check next. Internal trend math uses ratios such as `0.34`; UI copy formats them as percentages such as `34%`.

All implementation must be done in small phases. At implementation start, create a repo-tracked progress document named `INSIGHTS-IMPLEMENTATION-PLAN.md`. After every phase, mark that phase complete in the document with date/time, changed files, verification command, result, and remaining notes. If the context window fills and auto compaction fails, the next chat must read `AGENTS.md`, `INSIGHTS-IMPLEMENTATION-PLAN.md`, `git status`, and continue from the first incomplete phase.

## Key Interfaces And Defaults

Create focused insight models and services:

```csharp
public enum InsightSeverity
{
    Info,
    Warning,
    Critical,
}

public enum TrendState
{
    NoData,
    Flat,
    Up,
    Down,
    NewActivity,
}

public sealed record MetricWindow(double Current, double Previous);

public sealed record InsightCard(
    string Title,
    string Message,
    string WhyShown,
    string NextStep,
    InsightSeverity Severity,
    int Priority);

public sealed record InsightMetrics(
    MetricWindow CloudflareVisitors,
    MetricWindow Ga4Users,
    MetricWindow GoogleImpressions,
    MetricWindow GoogleClicks,
    MetricWindow GoogleMobileClicks,
    MetricWindow GoogleMobileImpressions,
    MetricWindow PageSpeedMobileScore,
    MetricWindow BingImpressions,
    MetricWindow BingClicks,
    MetricWindow Ga4EngagementRate,
    MetricWindow Ga4KeyEvents,
    MetricWindow CloudflareCacheHitRatio,
    MetricWindow CloudflareThreats,
    StatusCodeSummary HttpStatus,
    IndexingSummary Indexing,
    SourceFreshnessSummary Freshness);
```

Concrete defaults:

- `Trend.IsUp`: current/previous ratio `>= 0.25`.
- `Trend.IsFlat`: absolute ratio `<= 0.05`.
- `Trend.IsDown`: current/previous ratio `<= -0.10`.
- `previous == 0 && current > 0`: `NewActivity`, not `+100%`.
- Existing KPI properties such as `VisitorsChangePct` can remain as current percentage-number values for now; new insight calculations use `MetricWindow` ratios only.
- Show at most 4 insight rows, sorted by severity then priority.
- Empty state when enough data exists: `All connected sources look consistent for this period.`
- Empty state when sources/data are missing: `Connect or refresh sources to generate insights.`
- No SQLite schema migration is required.

## Insight Rules And Copy

Implement these rules in `InsightEngine`. Every rule must be skipped when the needed source data is missing, except explicit freshness/data-missing rules.

| Rule | Condition | User-facing English copy |
|---|---|---|
| Data stale | Any connected source has no `last_sync` or last sync older than 48 hours | **Some data may be out of date**. One or more connected sources have not synced recently, so the insights may not reflect the latest traffic. |
| Traffic mismatch | Cloudflare visitors up `>= 25%`, GA4 users flat `<= 5%`, current Cloudflare visitors `>= 50` | **Traffic looks inconsistent**. Cloudflare recorded more visitors, but GA4 users stayed almost the same. This can happen with bots, cached/static requests, or tracking gaps. |
| Google visibility without clicks | Google impressions up `>= 25%`, clicks flat `<= 5%`, current impressions `>= 100` | **People see your pages, but do not click**. Google impressions are rising, but clicks are not. Your result may need a better title, snippet, or ranking position. |
| Declining page | Worst declining page has previous clicks `>= 5` and current clicks down at least `25%` or `5` clicks | **A previously useful page is losing search traffic**. One page that used to bring Google clicks is now bringing fewer clicks. |
| New search queries | At least one new query has current clicks `> 0` for a single selected domain | **New search queries are bringing traffic**. Google is sending clicks from queries that were not visible in the previous period. |
| Mobile demand risk | Google mobile clicks or impressions up `>= 20%` and PageSpeed mobile score down `>= 10%` or latest score `< 0.70` | **Mobile traffic may need attention**. Mobile search demand is growing while mobile performance looks weak or is getting worse. |
| Indexing issue | Active sitemap URLs exist, inspected URLs `> 0`, and not-indexed count `>= 3` or not-indexed ratio `>= 20%` | **Some sitemap pages may not be indexed**. Numeris found sitemap URLs that do not currently have a passing Google URL Inspection result. |
| Indexing data missing | Active sitemap URLs `>= 5`, inspected URLs `== 0` | **Indexing data is missing**. Numeris knows about sitemap URLs, but they have not been inspected through Google URL Inspection yet. |
| HTTP errors | Cloudflare requests `>= 100` and 5xx share `>= 1%` or 5xx count `>= 10`; 4xx share `>= 5%` can produce warning | **Some visitors may be hitting errors**. Cloudflare recorded HTTP error responses during this period. |
| Bing visibility without clicks | Bing impressions `>= 50`, Bing clicks `== 0` | **Bing sees the site, but brings no visitors**. Bing has impressions for the site, but no clicks in the selected period. |
| Engagement gap | GA4 users or sessions up `>= 25%`, engagement rate down `>= 10%` or key events flat/down | **More visitors are not becoming more engaged**. GA4 traffic is growing, but engagement or key events are not improving with it. |
| Cache efficiency | Cloudflare requests `>= 100`, cache hit ratio down `>= 15%`, current hit ratio `< 50%` | **Caching may be less effective**. Cloudflare is serving fewer requests from cache than before. |
| Threat spike | Cloudflare threats `NewActivity` or up `>= 50%`, current threats `>= 10` | **Cloudflare is blocking more suspicious traffic**. Threat events increased during this period. |

`WhyShown` examples must use measured numbers:

- `Cloudflare visitors increased by 34%, while GA4 users changed by 2%.`
- `Google impressions increased by 41%, while clicks changed by 3%.`
- `18 of 42 inspected sitemap URLs are indexed.`
- `5xx responses were 1.4% of Cloudflare requests.`
- `Bing recorded 320 impressions and 0 clicks.`

`NextStep` must point to an existing page, not invent a workflow:

- Cloudflare traffic/status/cache issues: `Open Cloudflare and review Traffic, Cache, or Status Codes.`
- Google search issues: `Open Google Search and review Queries, Pages, and Indexing.`
- GA4 issues: `Open Analytics and review Pages, Acquisition, Events, and Devices.`
- Performance issues: `Open Performance and review PageSpeed and Core Web Vitals.`
- Bing issues: `Open Bing and review Queries, Pages, and Crawl.`

## Implementation Phases

### Phase 0: Handoff, branch, and progress document

- Create or switch to a `codex/...` feature branch.
- Create `INSIGHTS-IMPLEMENTATION-PLAN.md` at repo root from this plan.
- Add a `Phase Status` section with checkboxes for every phase.
- Before coding, verify current official docs and log links/results in the progress doc because `AGENTS.md` requires latest-doc checks before implementation. No new API syntax is expected, but verify Google Analytics Data API, PageSpeed API, and Bing Webmaster API assumptions.
- After this phase, update `INSIGHTS-IMPLEMENTATION-PLAN.md`, commit with a Finnish commit message, and continue only after `git status` confirms the expected changes.

### Phase 1: Tests for trend math and rule output

- Add focused tests in `Numeris.Tests/Program.cs` before implementation.
- Cover `TrendState` behavior: flat, up, down, no data, new activity, ratio formatting.
- Cover at least these rule outputs: traffic mismatch, Google visibility without clicks, indexing issue, HTTP error, Bing visibility without clicks, and clean empty state.
- Include tests that verify plain-English strings exist, technical names like `CTR`, `CrUX`, `5xx`, and `GA4` do not appear in titles, and `WhyShown` contains formatted percentages or counts.
- Run `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`; expected result is failure because the new types do not exist yet.
- Mark Phase 1 complete in the progress document with the failing test evidence.

### Phase 2: Insight domain model and trend helpers

- Create `Numeris/Models/InsightRows.cs` for `InsightCard`, `InsightSeverity`, `MetricWindow`, `TrendState`, `StatusCodeSummary`, `IndexingSummary`, and `SourceFreshnessSummary`.
- Create `Numeris/Services/Insights/Trend.cs` with ratio-based helpers:
  - `ChangeRatio(MetricWindow metric)`.
  - `Classify(MetricWindow metric, double flatTolerance = 0.05, double upThreshold = 0.25, double downThreshold = 0.10)`.
  - `FormatRatio(double ratio)` returning `+34%`, `-12%`, or `0%`.
- Do not reuse existing `SummaryRepository.PctChange` for new rules.
- Run the test harness; trend tests should pass and rule tests can still fail.
- Mark Phase 2 complete in the progress document and commit.

### Phase 3: Repository aggregation for insight metrics

- Add `GetInsightMetricsAsync(string domainOrAll, int days)` to `SummaryRepository` or a new `InsightMetricsRepository`.
- Prefer a new repository if `SummaryRepository` becomes too large; register it in `App.xaml.cs` if new.
- Use the same current/previous window semantics as Overview: selected period and the previous same-length period.
- Pull metrics from existing tables only:
  - Cloudflare traffic/cache/threat/status code data.
  - Search Console clicks, impressions, device rows, sitemap inspection state.
  - GA4 active users, sessions, engagement rate, key events.
  - Bing rank traffic clicks/impressions and crawl issue count.
  - PageSpeed mobile score trend.
  - `connections.status` and `connections.last_sync`.
- For `domain == "all"`, aggregate source-level metrics, but skip single-page/query insights that currently also skip `all`.
- Preserve `SiteIdentity` and existing domain/homepage normalization patterns.
- Add tests that inspect the repository code for no direct API clients and no new schema/migration.
- Run the test harness and mark Phase 3 complete with evidence.

### Phase 4: InsightEngine rules

- Create `Numeris/Services/Insights/InsightEngine.cs`.
- `Generate(InsightMetrics metrics)` returns sorted, max-4 `InsightCard` rows.
- Implement rules in the exact matrix above.
- Never show a diagnostic certainty that the data cannot prove; use `may`, `looks`, or `can happen` wording.
- Keep rule thresholds as private named constants in `InsightEngine`; do not scatter magic numbers.
- Add tests for rule priority and max-4 behavior.
- Add tests for no false positives when current and previous values are tiny or missing.
- Run the test harness and mark Phase 4 complete.

### Phase 5: Dashboard ViewModel integration

- Inject `InsightEngine` and the metrics repository into `DashboardViewModel`.
- Add:
  - `ObservableCollection<InsightCard> Insights`.
  - `string InsightsSummaryText`.
  - `bool HasInsightRows`.
- In `LoadAsync`, fetch insight metrics alongside existing Overview data and generate insights after all data tasks complete.
- Keep existing KPI/chart/status behavior unchanged.
- When no insight rows exist, set `InsightsSummaryText` to the correct empty state.
- Do not trigger sync from Insights; Overview refresh still uses existing page refresh behavior.
- Add tests that verify `DashboardViewModel` uses the engine/repository and does not read `SqliteDatabase` directly.
- Run tests and mark Phase 5 complete.

### Phase 6: Dashboard UI

- Add one `Insights` section to `DashboardPage.xaml` below KPI cards and above the status strip.
- Use one outer `Border` with an existing shared style such as `ChartCardBorderStyle` or `ContentCardBorderStyle`.
- Do not nest cards inside the Insights card. Render insight rows as simple unframed rows with separators.
- Each row shows:
  - severity text: `Info`, `Warning`, or `Critical`;
  - title;
  - message;
  - `Why shown`;
  - `Next step`.
- Add `AutomationProperties.Name` for the section and rows.
- Keep text English, plain, and non-technical in titles/messages.
- Use shared tokens from `Tokens.xaml`; do not hardcode colors, spacing, fonts, or chart-style values.
- Update responsive behavior so the Insights section stays full-width on narrow and wide layouts and does not push text outside containers.
- Add static tests that check the section exists, uses x:Bind to `ViewModel.Insights`, has `AutomationProperties.Name`, and does not contain forbidden technical title strings.
- Run tests and mark Phase 6 complete.

### Phase 7: Documentation and project memory

- Update `AGENTS.md` to document:
  - Overview now includes deterministic `InsightEngine` rules.
  - Insights use local repository metrics only.
  - Rule math uses ratios internally and UI copy formats percentages.
  - No AI model or external prompt path is part of this feature.
- Update `memory/MEMORY.md` with the same architecture summary.
- Update `PROJECT.md` only if it currently describes Overview content and would become stale.
- Do not update `migration-plan.md` because no schema or core migration order changes.
- Mark Phase 7 complete and commit with a Finnish commit message.

### Phase 8: Verification and final cleanup

- Run `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`.
- Run `dotnet build Numeris.slnx -p:Platform=x64`.
- Do not run `lc` or `sc`; the user runs those scripts when requested.
- If build/test fails with `MSB3027` or `MSB3021` against `Numeris.exe`, check whether Numeris is running and ask the user to close it before rerunning.
- Inspect `git diff` for accidental design-token, unrelated UI, or schema drift.
- Update `INSIGHTS-IMPLEMENTATION-PLAN.md` with final evidence.
- Commit final docs/test/UI/service changes if not already committed.
- Final response must summarize completed phases, verification commands, and any remaining user-run checks.

## Test Cases And Acceptance Criteria

Acceptance criteria:

- Overview shows an `Insights` card with 0-4 rows.
- Insight titles and messages are understandable without knowing GA4, CTR, CrUX, PageSpeed internals, or HTTP status terminology.
- Each insight has a `Why shown` line with concrete numbers.
- Each insight has a `Next step` that points to an existing Numeris page.
- No insight appears when required data is missing, except freshness/data-missing insights.
- New activity from zero is not displayed as `+100%`.
- Existing Overview KPIs, charts, period selector, domain selector, refresh button, and status strip still work.
- No new mock data, no new API calls, no new database tables, and no raw secret exposure.
- `AGENTS.md`, `memory/MEMORY.md`, and `INSIGHTS-IMPLEMENTATION-PLAN.md` reflect the final architecture and phase status.

Primary automated checks:

- `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`
- `dotnet build Numeris.slnx -p:Platform=x64`

Manual smoke checks:

- Start Numeris.
- Open Overview with `all`, `finnvek.com`, and `knittoolsapp.com`.
- Change period and domain; insights update with the rest of Overview.
- Confirm long English text wraps cleanly.
- Confirm empty state appears when no rules match.
- Confirm the app does not show AI branding, AI wording, or speculative diagnosis as certainty.

## Assumptions

- The first implementation keeps existing KPI percent fields unchanged and uses ratio math only for the new insights.
- The insights feature is Overview-only. Detail pages keep their current role.
- The feature uses existing persisted analytics data. It does not fetch extra data during insight generation.
- The first release includes all rules listed above, but implementation still proceeds phase-by-phase so any context loss can resume safely.
- Commit messages are Finnish, even though this handoff plan and UI copy are English.
