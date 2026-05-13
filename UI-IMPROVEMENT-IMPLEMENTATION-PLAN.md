# Numeris UI Improvement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Toteuttaa koko UI-auditin parannuspaketti: responsiivisemmat raporttisivut, saavutettavammat kontrollit, kevyempi sivunsisainen navigaatio, selkeammat Sources-toiminnot ja keskitetymmat UI-sopimukset.

**Architecture:** Muutos pidetaan WinUI 3 -natiivina ja nykyisen token-/teemajarjestelman sisalla. Raporttisivujen yhteiset kayttaytymissaannot lukitaan arkkitehtuuritesteilla, XAML muuttuu `SelectorBar`-pohjaiseksi sivunsisaisissa valinnoissa, ja nykyiset viewmodelit jatkavat datan omistajina ilman uutta data-flow-kerrosta.

**Tech Stack:** WinUI 3, Windows App SDK, XAML, CommunityToolkit.Mvvm, LiveChartsCore, nykyinen `Numeris.Tests`-harness.

**Status:** Implemented 2026-05-12. dotnet run --project Numeris.Tests and dotnet build Numeris.slnx were run after implementation.

---

### Task 1: Lukitse UI-parannusten sopimus testeilla

**Files:**
- Modify: `Numeris.Tests/Program.cs`

- [x] Lisaa testi, joka varmistaa taman suunnitelmatiedoston olemassaolon projektin juuressa ja tarkistaa, etta se kattaa responsiivisuuden, saavutettavuuden, SelectorBarin, Sources-toimintohierarkian ja data-listat.
- [x] Lisaa testi, joka vaatii sivunsisaisilta tabeilta `SelectorBar`-kayttoa ja estaa `TopTabNavigationViewStyle`-riippuvuuden raporttisivuilla.
- [x] Lisaa testi, joka vaatii refresh-painikkeille `AutomationProperties.Name`, busy-sidonnat ja raporttikomboille accessible name -arvot.
- [x] Lisaa testi, joka vaatii responsiiviset `VisualStateManager`-breakpointit KPI-/taulukkopainotteisille sivuille.
- [x] Lisaa testi, joka vaatii Sources-sivulle provider-kohtaiset `InfoBar`-statuspinnat, busy-disabloinnit ja destruktiivisten `Delete`-toimintojen erillisen tyylin.
- [x] Aja `dotnet run --project Numeris.Tests` ja varmista, etta uudet testit failaavat odotetusti.

### Task 2: Paivita jaettu teema- ja kontrollisopimus

**Files:**
- Modify: `Numeris/Themes/Tokens.xaml`
- Modify: `Numeris/Controls/KpiCard.xaml`
- Modify: `Numeris/Controls/HorizontalBars.xaml`
- Modify: `Numeris/Controls/HorizontalBars.xaml.cs`

- [x] Korvaa sivunsisaisen tabin tyyli `TopTabSelectorBarStyle`-tyylilla.
- [x] Lisaa `ResponsiveKpiGridStyle`, `ResponsiveTwoColumnGridStyle`, `ResponsiveThreeColumnGridStyle`, `CompactToolbarPanelStyle`, `DangerActionButtonStyle`, `SecondaryActionButtonStyle`, `DataTableHeaderBorderStyle`, `DataTableRowBorderStyle` ja yhteiset accessibility/busy-tokenit.
- [x] Lisaa `KpiCard`iin `TextTrimming`/`MaxLines` myos arvolle ja labelille, jotta pitkien arvojen layout ei riko kortteja.
- [x] Muuta `HorizontalBars` lukemaan label/value-sarakkeiden mitat resursseista ja nayttamaan tyhja tila, kun riveja ei ole.
- [x] Aja testit ja varmista, etta teemasopimusta koskevat testit etenevat.

### Task 3: Muuta raporttisivujen headerit ja sivunsisaiset valitsimet

**Files:**
- Modify: `Numeris/Views/DashboardPage.xaml`
- Modify: `Numeris/Views/CloudflarePage.xaml`
- Modify: `Numeris/Views/CloudflarePage.xaml.cs`
- Modify: `Numeris/Views/SearchConsolePage.xaml`
- Modify: `Numeris/Views/SearchConsolePage.xaml.cs`
- Modify: `Numeris/Views/BingPage.xaml`
- Modify: `Numeris/Views/BingPage.xaml.cs`
- Modify: `Numeris/Views/PerformancePage.xaml`
- Modify: `Numeris/Views/PerformancePage.xaml.cs`
- Modify: `Numeris/Views/HealthPage.xaml`
- Modify: `Numeris/Views/HealthPage.xaml.cs`
- Modify: `Numeris/Views/YouTubePage.xaml`
- Modify: `Numeris/Views/YouTubePage.xaml.cs`

- [x] Korvaa kaikki raporttisivujen sisaiset `NavigationView` tabit `SelectorBar`-kontrollilla.
- [x] Paivita code-behindit kayttamaan `SelectorBarSelectionChangedEventArgs` ja `SelectorBar.SelectedItem`.
- [x] Lisaa domain-, periodi-, filter-, sort- ja refresh-kontrolleille `AutomationProperties.Name`.
- [x] Lisaa refresh-painikkeille `IsEnabled`-sidonta `ViewModel.IsLoading`-tilan vastakohtaan code-behindin helperilla tai ViewModel-propertylla.
- [x] Lisaa responsiiviset VisualStateManager-tilat: laaja 4/3/2 saraketta, keskikokoinen 2/1 saraketta, kapea pinottu sisalto ja pienempi sivupadding.
- [x] Poista sivukohtaisia tarpeettomia kovakoodattuja leveyksia silloin kun sama arvo voidaan ottaa tokenista.
- [x] Aja testit ja korjaa XAML- tai code-behind-virheet.

### Task 4: Paranna raporttien data- ja tyhjatiloja

**Files:**
- Modify: `Numeris/Views/DashboardPage.xaml`
- Modify: `Numeris/Views/CloudflarePage.xaml`
- Modify: `Numeris/Views/SearchConsolePage.xaml`
- Modify: `Numeris/Views/BingPage.xaml`
- Modify: `Numeris/Views/PerformancePage.xaml`
- Modify: `Numeris/Views/HealthPage.xaml`
- Modify: `Numeris/Views/YouTubePage.xaml`

- [x] Lisaa datataulujen headereihin yhteinen header-border tyyli.
- [x] Lisaa rivien ylarajaan yhteinen row-border tyyli niissa listoissa, joissa lukutiheys hyotyy erotuksesta.
- [x] Lisaa raporttikortteihin pienet status-chipit semanttisia brusheja kayttaen: Health uptime, Performance Web Vitals/PageSpeed, Cloudflare status codes.
- [x] Siirra raakaa JSONia nayttavat Bing crawl -rivit tiiviimpaan, ellipsilla rajattuun muotoon ja tee raw-kentasta sekundaarinen.
- [x] Pida kaikki tekstit englanninkielisina UI:ssa nykyisen sovelluskielen mukaisesti.

### Task 5: Selkeyta Sources-sivun toimintohierarkia

**Files:**
- Modify: `Numeris/Views/SourcesPage.xaml`
- Modify: `Numeris/Views/SourcesPage.xaml.cs`

- [x] Lisaa jokaiselle provider-ryhmalle `InfoBar`, joka nayttaa kyseisen integraation status-/virheviestin.
- [x] Ryhmittele ensisijaiset toiminnot vasemmalle: `Connect`/`Sync`; pidä testit ja poistot sekundaarisina tai destruktiivisena tyylina.
- [x] Lisaa kaikkiin busy-painikkeisiin `IsEnabled`-sidonta niin, ettei samaa integraatiota voi ajaa rinnakkain.
- [x] Lisaa `AutomationProperties.Name` provider-headerien ikoneille, painikkeille ja credential-editoreille.
- [x] Kayta `DangerActionButtonStyle` kaikissa `Delete`-painikkeissa.
- [x] Jata credential-formit edelleen collapsed expandereihin ja sailyta CredentialVault-ohjetekstit.

### Task 6: Paivita dokumentaatio ja varmista build

**Files:**
- Modify: `AGENTS.md`
- Modify: `memory/MEMORY.md`
- Modify: `UI-IMPROVEMENT-IMPLEMENTATION-PLAN.md`

- [x] Paivita `AGENTS.md` ja `memory/MEMORY.md`, koska UI-vastuita ja shared-sopimuksia muutetaan.
- [x] Merkitse suunnitelman tehtavat tehdyiksi.
- [x] Aja `dotnet run --project Numeris.Tests`.
- [x] Aja `dotnet build Numeris.slnx`.
- [x] Raportoi mahdolliset build-/testiblokit ilman `lint-check`- tai `security-check`-skriptien ajamista.
