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
- [ ] Phase 3: Repository aggregation for insight metrics
- [ ] Phase 4: InsightEngine rules
- [ ] Phase 5: Dashboard ViewModel integration
- [ ] Phase 6: Dashboard UI
- [ ] Phase 7: Documentation and project memory
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
