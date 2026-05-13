# Bing + Performance UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Tee Bingistä oma analytiikkasivu ja lisää Bing + CrUX/PageSpeed selkeästi Overviewiin ilman, että Sources muuttuu raportointinäkymäksi.

**Architecture:** Sources jää vain konfigurointi-, testaus- ja sync-sivuksi. Uudet raportointipolut lukevat jo normalisoitua SQLite-dataa repositoryjen kautta: Bing saa oman sivun ja CrUX/PageSpeed oman `Performance`-sivun. Overview näyttää vain koko sovelluksen päätöksenteon kannalta tärkeimmät yhteenvedot.

**Tech Stack:** WinUI 3, CommunityToolkit.Mvvm partial properties, Dapper + SQLite, LiveCharts + `ChartTheme`, nykyiset design-tokenit.

---

## Summary

- Lisää vasempaan navigaatioon kaksi uutta pääsivua: `Bing` ja `Performance`.
- Pidä `Cloudflare`, `Google Search` ja `Bing` erillisinä kanavakohtaisina näkymissä.
- Pidä `Performance` erillisenä laatunäkymänä: CrUX = oikea käyttäjädata, PageSpeed = Lighthouse-diagnostiikka.
- Päivitä `Overview` näyttämään Google + Bing + Web Vitals -tilanne tiiviisti.
- Älä lisää uusia Bing detail API -metodeja tässä vaiheessa; käytä nykyistä ydinsynciä.

## Key Changes

- Navigaatio:
  - `Overview`
  - `Cloudflare`
  - `Google Search` nykyisen `Search`-sivun labeliksi
  - `Bing`
  - `Performance`
  - `Health`
  - footerissa `Sources`
- Lisää DI-rekisteröinnit ja routet uusille `BingPage`, `PerformancePage`, `BingViewModel`, `PerformanceViewModel`.
- Päivitä `ShellViewModel.IsKnownPage` hyväksymään `bing` ja `performance`.
- Sources-sivulle ei lisätä raporttikortteja; se säilyttää API keyt, site/URL-listat, testit ja sync-yhteenvedot.

## Repository + Type Additions

- Lisää `Numeris.Models.BingRows`-tiedostoon lukumallit:
  - `BingTrafficDay`: `Date`, `Clicks`, `Impressions`, `Ctr`
  - `BingQueryRow`: `Query`, `Clicks`, `Impressions`, `Ctr`, `AvgClickPosition`, `AvgImpressionPosition`
  - `BingPageRow`: `PageUrl`, `Clicks`, `Impressions`, `Ctr`
  - `BingRawMethodSummary`: `Method`, `ItemCount`, `LastFetchedAt`
- Lisää `BingRepository`-lukumetodit:
  - `GetTrafficDailyAsync(siteUrl, start, end)`
  - `GetQueriesAsync(siteUrl, start, end, sortBy, limit)`
  - `GetPagesAsync(siteUrl, start, end, limit)`
  - `GetRawMethodSummaryAsync(siteUrl)`
  - `GetCrawlIssueItemsAsync(siteUrl, limit)` lukee `bing_raw_items`-taulusta metodin `GetCrawlIssues`.
- Lisää `Numeris.Models.PerformanceRows`-tiedostoon lukumallit:
  - `CruxMetricSummary`: target, form factor, metric, latest collection end, p75, good/needs/poor densities, status
  - `CruxTrendPoint`: collection end, metric, form factor, p75
  - `PageSpeedLatestRun`: URL, strategy, scores, analysis UTC, runtime error
  - `PageSpeedAuditIssue`: URL, strategy, audit id, title, score, display value, numeric value/unit
- Lisää `PerformanceRepository`-lukumetodit:
  - `GetLatestCruxCoreVitalsAsync(domainOrAll)`
  - `GetCruxTrendAsync(domainOrAll, metric, formFactor, start, end)`
  - `GetLatestPageSpeedRunsAsync(domainOrAll)`
  - `GetPageSpeedScoreTrendAsync(domainOrAll, start, end)`
  - `GetPageSpeedAuditIssuesAsync(domainOrAll, limit)`
- Lisää `SummaryRepository`iin Overview-tason lukumetodit:
  - Bing clicks/impressions nykyiselle periodille ja edelliselle periodille.
  - Web Vitals summary: konservatiivinen status `Pass/Warn/Fail` uusimmista LCP/INP/CLS-riveistä.
  - PageSpeed mobile performance latest score.

## Bing Page

- Tee `BingPage` samalla rakenteella kuin `SearchConsolePage`: headerissä otsikko, domain, period, refresh ja top-tabit.
- Tabit:
  - `Overview`: clicks, impressions, CTR, Bing traffic trend.
  - `Queries`: top queryt, sorttaus `clicks/impressions/ctr/position`.
  - `Pages`: top pages.
  - `Crawl`: raw-pohjainen kevyt crawl health: viimeisin `GetCrawlStats`, crawl issue -lista, viimeinen fetch.
- Bingin domain-filtteri käyttää `bing_sites.site_url`-arvoja ja `SiteIdentity`/`PerformanceUrl`-normalisointia.
- `all`-domain näyttää summatut traffic-rivit ja yhdistetyt top query/page -listat.
- Käytä samoja `ContentCardBorderStyle`, `ChartCardBorderStyle`, `DataListViewStyle`, `TopTabNavigationViewStyle` ja `ChartTheme`-polkuja kuin Search/Cloudflare.

## Performance Page

- Tee `PerformancePage` omaksi pääsivuksi otsikolla `Performance`.
- Tabit:
  - `Overview`: Core Web Vitals status, latest PageSpeed mobile/desktop category scores.
  - `CrUX`: LCP, INP, CLS trendit ja p75-arvot form factoreille `PHONE`, `DESKTOP`, `TABLET`, `ALL`.
  - `PageSpeed`: latest runs, category scores, failing audits.
  - `URLs`: read-only lista `performance_urls`-kohteista ja ohjaus Sourcesiin asetusten muuttamista varten.
- CrUX-status:
  - LCP hyvä jos p75 <= 2500 ms.
  - INP hyvä jos p75 <= 200 ms.
  - CLS hyvä jos p75 <= 0.10.
  - Jos mikään core metric puuttuu, status on `No data`, ei `Fail`.
- `all`-domain:
  - Overview näyttää kaikki targetit listana.
  - Yhteinen status on konservatiivinen: huonoin uusin core metric kaikista domaineista.
  - Trendeissä käytetään huonointa p75-arvoa per collection_end, ei keskiarvoa.
- PageSpeed audit issue -lista näyttää vain auditit, joilla `score IS NOT NULL AND score < 0.9`, järjestettynä pienin score ensin.

## Overview Updates

- Vaihda KPI-rivi neljään selkeään korttiin:
  - `Visitors` Cloudflaresta.
  - `Google Clicks` Search Consolesta.
  - `Bing Clicks` Bing Webmasterista.
  - `Web Vitals` CrUX-statuksesta.
- Päivitä Search Trend -chart näyttämään Google clicks ja Bing clicks samassa chartissa.
- Lisää status stripiin:
  - `Cache hit ratio`
  - `Indexed`
  - `PageSpeed mobile`
  - `Last sync`
- `Last sync` näyttää uusimman `connections.last_sync`-ajan Cloudflare/Search/Bing/Performance-lähteistä.
- Älä tee Overviewista detail-sivua; detailit avataan omilta sivuilta.

## Tests

- Lisää `Numeris.Tests/Program.cs` contract-testit:
  - navigaatiossa on `Bing`, `Performance`, `Google Search` ja route-map sisältää `bing`/`performance`.
  - `ShellViewModel.IsKnownPage` hyväksyy uudet sivut.
  - `App.xaml.cs` rekisteröi uudet ViewModelit ja sivut.
  - Bing-sivu käyttää shared header/tab/chart/list tyylejä ja `ChartTheme`-polkua.
  - Performance-sivu käyttää shared header/tab/chart/list tyylejä ja `ChartTheme`-polkua.
  - Overview sisältää Bing KPI:n ja Web Vitals KPI:n.
  - `BingRepository` sisältää lukumetodit eikä ViewModel lue SQLitea suoraan.
  - `PerformanceRepository` sisältää lukumetodit eikä ViewModel lue SQLitea suoraan.
  - uusia Bing detail API -metodeja ei lisätä automaattisynciin.
- Aja verifikaatio:
  - `dotnet run --project Numeris.Tests/Numeris.Tests.csproj`
  - `dotnet build Numeris.slnx -p:Platform=x64`
- Älä aja `lc` tai `sc`; projektiohjeen mukaan käyttäjä ajaa ne itse.

## Documentation

- Luo projektin juureen `ANALYTICS-UI-IMPLEMENTATION-PLAN.md` tällä suunnitelmalla.
- Päivitä `AGENTS.md`:
  - Bingillä on oma raportointisivu.
  - CrUX/PageSpeed näkyvät `Performance`-sivulla.
  - Sources ei ole raportointisivu.
  - Overviewin rooli on koonti, ei detail-analyysi.
- Päivitä `memory/MEMORY.md` samalla arkkitehtuuripäätöksellä.
- Päivitä tarvittaessa `numeris_winui3_visual_design_instructions.md`, jos uudet sivut lisäävät toistettavan layout-sopimuksen.
- Commit-viestit suomeksi, esimerkiksi:
  - `test: lukitse Bing- ja Performance-navigaatiosopimus`
  - `feat: lisää Bing-raportointinäkymä`
  - `feat: lisää Performance-näkymä CrUX- ja PageSpeed-datalle`
  - `feat: nosta Bing ja Web Vitals Overview-koontiin`
  - `docs: dokumentoi analytiikkanäkymien vastuut`

## Assumptions

- Uuden Performance-sivun nimi on `Performance`, ei `Web Vitals`, koska sivu sisältää myös PageSpeedin accessibility/best-practices/SEO-auditit.
- Nykyistä SQLite-skeemaa ei muuteta v1:ssä; kaikki uudet näkymät lukevat olemassa olevia tauluja.
- Bing crawl stats/issues näytetään v1:ssä nykyisestä `bing_raw_items`-datasta kevyenä health-listana.
- Uusia Bing Webmaster detail API -metodeja ei lisätä ennen erillistä käyttötapausta.
- Viralliset dokumentit tarkistettu suunnittelua varten 2026-05-08: Microsoft Bing Webmaster API, CrUX API/History API ja PageSpeed Insights API.
