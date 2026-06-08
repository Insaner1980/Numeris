# Numeris Explainable Insights - toteutuksen seuranta

Luotu: 2026-06-08 10:17:43 +03:00

Lahdesuunnitelma: `Numeris Explainable Insights Implementation Plan.md`

## Tavoite

Toteuta deterministic, rule-based Insights-osio Numeriksen Overview-nakymaan. Ominaisuus kayttaa vain nykyisia SQLiteen tallennettuja analytiikkametriikoita. Se ei kayta AI-malleja, promptteja, uusia ulkoisia API-kutsuja, uutta mock-dataa tai SQLite-skeemamuutosta.

## Jatko-ohje kompaktion jalkeen

Jos keskustelun konteksti katoaa, seuraavan ajon tulee lukea:

1. `AGENTS.md`
2. `INSIGHTS-IMPLEMENTATION-PLAN.md`
3. `git status`
4. ensimmainen keskenerainen vaihe alta

## Virallisten dokumenttien tarkistus

Tarkistettu ennen toteutuskoodia 2026-06-08:

- Google Analytics Data API `properties.runReport`: virallinen dokumentaatio vahvistaa, etta `runReport` palauttaa GA-tapahtumadataa tauluna pyydetyille dimensioille/metriikoille, endpoint on `POST https://analyticsdata.googleapis.com/v1beta/{property=properties/*}:runReport`, ja `analytics.readonly` on tuettu OAuth-scope. Linkki: https://developers.google.com/analytics/devguides/reporting/data/v1/rest/v1beta/properties/runReport
- PageSpeed Insights API v5 `pagespeedapi.runpagespeed`: virallinen dokumentaatio vahvistaa, etta API analysoi annetun URLin `GET https://pagespeedonline.googleapis.com/pagespeedonline/v5/runPagespeed` -endpointilla, `strategy` voi olla `mobile` tai `desktop`, ja Lighthouse performance score on vastauksen `lighthouseResult.categories`-rakenteessa. Linkki: https://developers.google.com/speed/docs/insights/rest/v5/pagespeedapi/runpagespeed
- Bing Webmaster API: Microsoft Learn vahvistaa nykyiset metodit `GetRankAndTrafficStats`, `GetQueryStats`, `GetPageStats` ja `GetCrawlIssues`. Taman toteutuksen Insights-polku ei lisaa uusia API-kutsuja, vaan lukee aiemmin tallennettuja SQLite-riveja. Linkit: https://learn.microsoft.com/en-us/dotnet/api/microsoft.bing.webmaster.api.interfaces.iwebmasterapi ja https://learn.microsoft.com/en-us/dotnet/api/microsoft.bing.webmaster.api.interfaces.iwebmasterapi.getrankandtrafficstats?view=bing-webmaster-dotnet

Tulos: suunnitelman API-oletukset ovat edelleen yhteensopivia nykyisten virallisten dokumenttien kanssa. Insights-ominaisuuden ensimmainen toteutus lukee vain paikallisia repository-metriikoita.

## Phase Status

- [x] Phase 0: Handoff, branch, and progress document
- [x] Phase 1: Tests for trend math and rule output
- [x] Phase 2: Insight domain model and trend helpers
- [x] Phase 3: Repository aggregation for insight metrics
- [x] Phase 4: InsightEngine rules
- [x] Phase 5: Dashboard ViewModel integration
- [x] Phase 6: Dashboard UI
- [x] Phase 7: Documentation and project memory
- [ ] Phase 8: Verification and final cleanup

## Phase Log

### Phase 0: Handoff, branch, and progress document

Status: complete

Completed: 2026-06-08 10:17:43 +03:00

- Branch: `codex/numeris-core-continuation`
- Changed files:
  - `INSIGHTS-IMPLEMENTATION-PLAN.md`
- Verification command:
  - `git status --short -- INSIGHTS-IMPLEMENTATION-PLAN.md`
- Result:
  - `?? INSIGHTS-IMPLEMENTATION-PLAN.md` before commit; only the expected progress document was selected for the phase commit.
- Notes:
  - Nykyinen tyopuu sisaltaa paljon aiempia muokkauksia. Tahan vaiheeseen committoidaan vain tama progress-dokumentti.

### Phase 1: Tests for trend math and rule output

Status: complete

Completed: 2026-06-08 10:23:50 +03:00

- Changed files:
  - `Numeris.Tests/Program.cs`
  - `INSIGHTS-IMPLEMENTATION-PLAN.md`
- Verification command:
  - `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`
- Result:
  - Expected RED failure: `CS0234: The type or namespace name 'Insights' does not exist in the namespace 'Numeris.Services'`.
- Notes:
  - Testit kattavat trendiluokittelun, prosenttiformatoinnin, liikenne-/Google-/indexing-/HTTP-/Bing-saannot, plain-English title -vaatimuksen ja puhtaan empty staten.
  - `InsightEngine`-saannot kutsutaan reflektiolla, jotta Phase 2 voi viela kaantya mallien ja trend-helperien jalkeen ilman engine-toteutusta.

### Phase 2: Insight domain model and trend helpers

Status: complete

Completed: 2026-06-08 10:26:43 +03:00

- Changed files:
  - `Numeris/Models/InsightRows.cs`
  - `Numeris/Services/Insights/Trend.cs`
  - `Numeris.Tests/Program.cs`
  - `INSIGHTS-IMPLEMENTATION-PLAN.md`
- Verification command:
  - `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`
- Result:
  - Expected partial RED: trend math test passed; remaining six insight rule tests failed with `Numeris.Services.Insights.InsightEngine is missing`.
- Notes:
  - `InsightMetrics` includes small optional fields for worst declining search page and new query count because those rules cannot be derived from aggregate windows alone.

### Phase 3: Repository aggregation for insight metrics

Status: complete

Completed: 2026-06-08 10:33:53 +03:00

- Changed files:
  - `Numeris/Services/Database/Repositories/InsightMetricsRepository.cs`
  - `Numeris.Tests/Program.cs`
  - `INSIGHTS-IMPLEMENTATION-PLAN.md`
- Verification command:
  - `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`
- Result:
  - Expected partial RED: insight metrics aggregation test passed; remaining six insight rule tests failed with `Numeris.Services.Insights.InsightEngine is missing`.
- Notes:
  - Aggregointi lukee vain nykyisia SQLite-tauluja ja kayttaa lahdekohtaisia domain/site/url-filttereita.
  - `Migrations.CurrentSchemaVersion` pysyi arvossa `7`; uusia insight-tauluja ei lisatty.

### Phase 4: InsightEngine rules

Status: complete

Completed: 2026-06-08 10:39:54 +03:00

- Changed files:
  - `Numeris/Services/Insights/InsightEngine.cs`
  - `Numeris.Tests/Program.cs`
  - `INSIGHTS-IMPLEMENTATION-PLAN.md`
- Verification command:
  - `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`
- Result:
  - Pass. Trend, rule output, max-4/sort, false-positive, repository and existing architecture tests passed.
- Notes:
  - Saannot ovat deterministic ja thresholdit on keskitetty `InsightEngine`in private vakioihin.
  - `Generate` palauttaa enintaan nelja rivia severity-desc/priority-asc jarjestyksessa.

### Phase 5: Dashboard ViewModel integration

Status: complete

Completed: 2026-06-08 10:44:08 +03:00

- Changed files:
  - `Numeris/App.xaml.cs`
  - `Numeris/ViewModels/DashboardViewModel.cs`
  - `Numeris.Tests/Program.cs`
  - `INSIGHTS-IMPLEMENTATION-PLAN.md`
- Verification command:
  - `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`
- Result:
  - Pass. Dashboard ViewModel DI/static architecture test passed with existing harness.
- Notes:
  - `DashboardViewModel.LoadAsync` hakee insight-metriikat muiden Overview-tehtavien rinnalla ja paivittaa `Insights`, `InsightsSummaryText` ja `HasInsightRows`.
  - Insights-polku ei kaynnista synceja eika lue `SqliteDatabase`a ViewModelista.

### Phase 6: Dashboard UI

Status: complete

Completed: 2026-06-08 10:48:49 +03:00

- Changed files:
  - `Numeris/Views/DashboardPage.xaml`
  - `Numeris/Models/InsightRows.cs`
  - `Numeris.Tests/Program.cs`
  - `INSIGHTS-IMPLEMENTATION-PLAN.md`
- Verification command:
  - `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`
- Result:
  - Pass. Dashboard Insights UI static test passed and XAML compilation succeeded through the test project build.
- Notes:
  - Insights-osio on yksi `ChartCardBorderStyle`-kortti KPI-gridin ja status stripin valissa.
  - Rivirakenne kayttaa simple separator -riveja ja yhteisia tekstin/rajojen tokeneita.

### Phase 7: Documentation and project memory

Status: complete

Completed: 2026-06-08 10:53:01 +03:00

- Changed files:
  - `AGENTS.md`
  - `memory/MEMORY.md`
  - `PROJECT.md`
  - `INSIGHTS-IMPLEMENTATION-PLAN.md`
- Verification command:
  - `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`
- Result:
  - Pass. Documentation-sensitive harness checks still passed.
- Notes:
  - `migration-plan.md` was intentionally not updated because no schema or core migration order changed.
  - Docs now record that Insights is deterministic, local SQLite/repository based, ratio-driven, and has no AI/prompt/new API path.
