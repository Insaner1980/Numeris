# Numeris Mattamusta UI- ja Palkkikaaviouudistus

**Yhteenveto**
Uudistetaan Numeris WinUI 3 / Windows App SDK -sovelluksen visuaalinen järjestelmä mattamustaksi ja ilmavammaksi. Toteutus tehdään nykyisen arkkitehtuurin mukaan: XAML-tokenit `Numeris/Themes/Tokens.xaml`, LiveCharts-teema `Numeris/Themes/ChartTheme.cs`, väripaletti `Numeris/Themes/ChartPalette.cs`, sekä olemassa olevat WinUI-kontrollit kuten `KpiCard` ja `HorizontalBars`.

Tämä ei ole Numeris/Tauri/Svelte-muutos. Ei kosketa Rustia, Tauri-projektia, vanhaa `C:\Dev\Numeris`-kansiota, tietokantaa, synkronointilogiikkaa tai API-rajapintoja.

## Tavoite

- Muuttaa Numeris mustaksi, mataksi, premium-henkiseksi analytiikkasovellukseksi.
- Soveltaa inspiraatiokuvan palkkityyliä LiveCharts-pylväisiin: tummat gradienttipalkit, hienovarainen ylävalo, yksi sininen korostuspalkki, kevyt ruudukko.
- Korjata nykyinen “kaikki on samanlaista korttia” -tunne hierarkialla: hero chart, pienemmät KPI:t, ilmavat listat ja vähemmän raskaita korttipintoja.
- Säilyttää nykyinen WinUI 3 -rakenne, Mica/custom titlebar, `NavigationView`, `SelectorBar`, `PeriodOptions`, `ChartTheme` ja testiharness.

## Toteutusmuutokset

### 1. Teematokenit

Päivitä `Numeris/Themes/Tokens.xaml` niin, että kaikki uusi ilme tulee keskitetysti resursseista.

Muuta värit mustaan mattasuuntaan:
- `AppBackgroundColor`: tumma fallback, esim. `#070808`.
- `AppBackdropScrimColor`: vahvempi musta scrim, jotta tausta ei kilpaile sisällön kanssa.
- `CardSurfaceColor`, `ControlSurfaceColor`, `ChartPanelColor`: mustapohjaisia läpikuultavia pintoja, mutta nykyistä vähemmän “lasisia”.
- `NumerisCardBorderColor`: ohut valkoinen hiusviiva, noin `#26FFFFFF` - `#3AFFFFFF`.
- `NumerisAccentColor`: vaihdetaan honey amberista siniseen korostukseen, esim. `#4F73FF`, koska palkkikuvassa valittu pylväs on sininen.
- Lisää erilliset chart-resurssit:
  - `ChartBarNeutralTopColor`
  - `ChartBarNeutralMidColor`
  - `ChartBarNeutralBottomColor`
  - `ChartBarHighlightTopColor`
  - `ChartBarHighlightMidColor`
  - `ChartBarHighlightBottomColor`
  - `ChartBarCapColor`
  - `ChartReferenceLineColor`
- Lisää delta-resurssit:
  - `PositiveDeltaColor`
  - `NegativeDeltaColor`
  - `NeutralDeltaColor`
  - vastaavat brushit.

Pidä nykyiset arkkitehtuurisäännöt:
- Header ei saa olla kortti.
- Navigation ja content käyttävät samaa shell-taustaa.
- Sivukohtaisia kovakoodattuja värejä ei lisätä.
- `ChartPalette` hakee värit XAML-resursseista.

### 2. Taustakuva

Korvaa tai uudelleenluo `Numeris/Assets/AppBackdrop.png`.

Tavoite:
- Tesla-kuvan kaltainen musta, pehmeä, mattainen satiini-/metallipinta.
- Ei selkeitä viivoja, ei orbeja, ei kirkasta hunaja-aksenttia.
- Keskisisältöalueen pitää pysyä matalakontrastisena.
- Taustakuva pysyy shell-tasolla `MainWindow`issa, ei sivukohtaisena hero-kuvana.

Hyväksyntä:
- Tausta näkyy vain tunnelmana.
- Kortit ja chartit ovat luettavampia kuin tausta.
- NavigationView ei saa erillistä värirajaa content-alueeseen.

### 3. KPI- ja delta-kontrollit

Päivitä `Numeris/Controls/KpiCard.xaml` ja `.xaml.cs`.

Uusi KPI-rakenne:
- Yläreunassa pieni label.
- Keskellä iso arvo.
- Arvon vieressä tai heti alla tiivis delta-badge: `▲ +12.4%` tai `▼ -5.8%`.
- `Detail` jää alemman hierarkian tekstiksi, ei tärkeimmäksi riviksi.
- Delta ei enää näytä pitkää “vs previous period” -lausetta isona tekstinä; se voi olla tooltipissä tai lyhyenä muted-tekstinä.

Lisää tarvittaessa uusi `DeltaBadge`-UserControl:
- Propsit: `Value`, `Invert`, `Suffix`, `ShowSign`.
- Käyttää `PositiveDeltaBrush`, `NegativeDeltaBrush`.
- Ei kovakoodattuja värejä.
- `KpiCard` käyttää tätä kontrollia.

Jos erillinen UserControl tuntuu liian isolta, toteuta badge suoraan `KpiCard`iin, mutta jätä testillä varmistus että delta-värit tulevat resursseista.

### 4. Aikavalikko

Nykyinen `PeriodCombo` on toimiva mutta ei vastaa inspiraation segmenttivalikkoa. Toteuta yhteinen WinUI-kontrolli tai tyyli periodivalintaan.

Suositeltu ratkaisu:
- Luo `Numeris/Controls/PeriodSelector.xaml` ja `.xaml.cs`.
- Se näyttää `PeriodOptions.All` -arvot vaakasuuntaisina pill/segmented button -valintoina: `7d`, `30d`, `90d`, `All`.
- Valittu arvo on vaaleampi sisäänpainettu segmentti.
- Kontrolli sitoo valinnan `ShellViewModel.SelectedPeriod`iin code-behindin kautta samaan tapaan kuin nykyiset `ComboBox`it.
- Mobiili/narrow layoutissa kontrolli voi wrapata tai palata kompaktimpaan leveyteen, mutta ei näytä raakaa enum-arvoa.

Korvaa `PeriodCombo` ainakin näillä sivuilla:
- `DashboardPage`
- `CloudflarePage`
- `SearchConsolePage`
- `BingPage`
- `PerformancePage`
- `HealthPage`

Pidä `PeriodOptions.All` ja `Period.ShortLabel()` yhtenä totuutena.

### 5. LiveCharts-palkkityyli

Laajenna `Numeris/Themes/ChartTheme.cs`.

Lisää helperit:
- `CreateMatteColumnSeries<T>(string name, IReadOnlyList<T> values, int? highlightIndex = null)`
- `CreateMutedColumnSeries<T>(...)`
- `CreateHighlightColumnSeries<T>(...)` tarvittaessa monisarjaisiin kaavioihin.
- `CreateMatteBarFill()`: palauttaa `LinearGradientPaint`.
- `CreateHighlightBarFill()`: palauttaa sinisen `LinearGradientPaint`.
- `StyleColumnSeries(...)`: asettaa yhteiset arvot kuten `Stroke = null`, `MaxBarWidth`, `Padding`, `Rx/Ry` jos LiveCharts-versiossa tuettu.
- `StyleChartForBars(CartesianChart chart)`: säätää legendan, tooltipin, animaation, hoverin ja chartin taustan.

LiveCharts 2.0.2 tukee Skia-painteja; virallinen LiveCharts-dokumentaatio käyttää `LinearGradientPaint`-luokkaa ja `ColumnSeries`-sarjoja WinUI:ssa. Käytä sitä, älä lisää uutta kaaviokirjastoa.

Palkkityyli:
- Neutraali palkki: tumma harmaa gradientti ylhäältä alas.
- Highlight-palkki: sininen gradientti.
- Palkkien yläreuna: jos LiveCharts ei tue erillistä cap-strokea luotettavasti, simuloidaan se hillityllä gradientin kirkkaalla ylävärillä.
- Ruudukko: vain y-akselin kevyt katkoviiva tai himmeä separator.
- X-akselin labelit: lyhyet päivämäärät/kuukaudet, harmaa, ei ylikorostusta.
- Y-akselin arvot: muted, mutta luettavat.
- Chart-mesh-tausta: nykyinen 2.5D mesh joko himmennetään selvästi tai poistetaan bar chart -pinnoilta, koska inspiraatiokuvan palkkityyli on rauhallisempi ja mattamaisempi.

### 6. ChartPalette

Päivitä `Numeris/Themes/ChartPalette.cs`.

Lisää värit:
- `BarNeutralTop`
- `BarNeutralMid`
- `BarNeutralBottom`
- `BarHighlightTop`
- `BarHighlightMid`
- `BarHighlightBottom`
- `BarCap`
- `ReferenceLine`
- `PositiveDelta`
- `NegativeDelta`

Säilytä nykyiset `Accent`, `Secondary`, `Success`, `Danger`, `Muted`, `GridLine`, `AxisText`, koska niitä käytetään jo useissa ViewModelissä.

Tavoite:
- ViewModelit eivät käytä `SKColor.Parse(...)`.
- ViewModelit eivät sisällä design-hexeja.
- Uudet chart-värit kulkevat aina `ChartPalette`n kautta.

### 7. Dashboard / Overview

Päivitä `DashboardPage.xaml` ja `DashboardViewModel.cs`.

Rakenne:
- Ylhäällä header kuten nykyään, mutta `PeriodSelector` korvaa `ComboBox`-periodin.
- Ensimmäinen sisältöalue:
  - vasemmalle/ylös iso hero chart: “Traffic” tai “Visitors”.
  - chartin otsikon alla iso arvo `VisitorsTotal` ja delta `VisitorsChangePct`.
  - chart on uusi mattapalkkikaavio, jossa viimeisin tai suurin päivä korostuu sinisellä.
- KPI-rivi:
  - Google Clicks
  - Bing Clicks
  - Web Vitals
  - PageSpeed mobile
- `StatusStrip` kevennetään ilmavaksi viivapaneeliksi, ei raskaaksi kortiksi.

ViewModel-muutos:
- `BuildTrafficChart` vaihtaa `LineSeries<long>` -> `ColumnSeries<long>` käyttäen `ChartTheme.CreateMatteColumnSeries`.
- `BuildSearchChart` voi jäädä line chartiksi, jos siinä verrataan Google vs Bing; vaihtoehtoisesti toteuta grouped column vain jos LiveCharts-konfiguraatio pysyy selkeänä. Suositus: pidä Search Trend line chartina v1:ssä, koska palkkityylin pääfokus on Traffic.
- Lisää helper `HighlightIndex(rows)`:
  - jos rivejä on, korosta viimeinen rivi
  - jos viimeinen arvo on nolla mutta aiemmissa on dataa, korosta suurin arvo
  - jos kaikki nollaa, ei highlightia.

### 8. Cloudflare

Päivitä `CloudflareViewModel.cs`, `CloudflarePage.xaml`.

Sovella palkkityyli:
- Traffic tab:
  - Visitors/Pageviews voi pysyä line chartina tai vaihtua stacked/grouped bariksi. Suositus: Visitors mattapalkiksi, Pageviews muted overlay/secondary-sarjaksi vain jos se ei sotke luettavuutta.
- Security tab:
  - `SecuritySeries` vaihtuu mattapalkiksi.
  - Korostus suurimpaan uhkapäivään.
- Status codes tab:
  - säilytä `StackedColumnSeries<long>`, mutta muuta värit mattamaisiksi.
  - 2xx = neutraali/green-muted, 3xx = muted, 4xx = warning, 5xx = danger.
  - Ei kirkasta sateenkaarilegendaa.
- Web Analytics:
  - Visits mattapalkiksi, page views muted-sarjaksi tai lineksi tarpeen mukaan.
- Top countries/pages/referrers pysyvät `HorizontalBars`-kontrollina, mutta sen tyyli päivitetään.

### 9. HorizontalBars

Päivitä `Numeris/Controls/HorizontalBars.xaml.cs`.

Nykyinen kontrolli luo palkit code-behindissa. Pidä rakenne, mutta paranna ilme:
- Track tummaksi ja ohuemmaksi.
- Fill gradienttimaiseksi mahdollisuuksien mukaan XAML/LinearGradientBrushilla.
- Suurin rivi saa highlight-värin.
- Muut rivit ovat harmaa/neutral.
- Label ja value käyttävät `NumerisTextSecondaryBrush` / `NumerisTextTertiaryBrush`.
- Lisää optionaaliset dependency propertyt:
  - `HighlightTopValue: bool = true`
  - `Compact: bool = false`
  - `ValueFormat: string?` vain jos tarpeen, muuten ei lisätä.

Hyväksyntä:
- Cloudflare Top countries näyttää samalta design-perheeltä kuin iso palkkikaavio.
- Tyhjä tila jää “No data” -tekstiksi, mutta sekin käyttää muted-tokenia.

### 10. Muut raporttisivut

Sovella yhteisiä muutoksia ilman isoa toiminnallista refaktoria:
- `SearchConsolePage`: Devices-kaavio voidaan muuttaa grouped mattapalkiksi; overview jää line/mixed chartiksi jos se on selkeämpi.
- `BingPage`: traffic trend voi käyttää mattapalkkia.
- `PerformancePage`: PageSpeed score trend voi olla palkki; CrUX vitals voi pysyä omassa visualisoinnissaan jos palkki ei sovi mittariin.
- `HealthPage`: incidents/status countit voivat käyttää mattapalkkia; response time voi pysyä line chartina.

Sääntö:
- Älä pakota jokaista kaaviota palkiksi. Palkkia käytetään volyymeihin, määrien jakautumiin ja kuukausi-/päivätrendeihin. Line chart säilyy jatkuville arvoille kuten response time, cache ratio tai engagement rate.

### 11. XAML-layout ja ilmavuus

Päivitä sivujen spacing maltillisesti:
- Lisää hero-paneeleille enemmän sisäistä hengitystä.
- Vähennä pienten korttien määrää siellä, missä data voidaan esittää listana tai status stripissä.
- Kortit eivät saa olla kaikki saman painoisia:
  - hero chart = suurin pinta
  - KPI = pienempi reunallinen pinta
  - listat = ilmavat rivit / kevyet paneelit
  - status strip = jakoviiva- ja tekstirakenne

Narrow layout:
- Varmista `VisualStateManager`-polku jokaisella raporttisivulla.
- KPI:t pinoutuvat 1-2 sarakkeeseen.
- PeriodSelector ei leikkaa tekstiä.

### 12. Testit

Päivitä `Numeris.Tests/Program.cs` arkkitehtuuritesteillä:

Lisää testit:
- `Tokens.xaml` sisältää uudet mattamustat chart bar -resurssit.
- `ChartPalette.cs` lukee uudet bar/delta-värit resursseista.
- `ChartTheme.cs` sisältää keskitetyt `CreateMatteColumnSeries`-helperit.
- ViewModelit eivät luo omia gradientteja tai kovakoodaa chart-hexeja.
- `DashboardViewModel` käyttää `ColumnSeries`/`ChartTheme` traffic-palkkeihin.
- `KpiCard` käyttää `SuccessBrush` / `DangerBrush` tai uusia delta-brusheja, ei WinUI default `TextFillColorSecondaryBrush` fallbackia.
- Raporttisivut käyttävät `PeriodSelector`ia eivätkä vanhaa period `ComboBox`ia, jos toteutus korvaa sen kaikilla sivuilla.
- `HorizontalBars` käyttää token-resursseja eikä kovakoodattuja värejä.

Älä tee pikselitason snapshot-testejä testiharnessiin, koska nykyinen testiharness on staattinen konsolitarkistin. Visuaalinen tarkistus tehdään ajamalla sovellus.

### 13. Dokumentointi

Koska tämä on arkkitehtuurinen UI-vastuun muutos, päivitä:
- `AGENTS.md`
- `memory/MEMORY.md`

Kirjaa:
- Numeris on WinUI 3 / Windows App SDK -sovellus.
- UI-tokenien yksi totuus on `Numeris/Themes/Tokens.xaml`.
- LiveChartsin yksi totuus on `ChartTheme` + `ChartPalette`.
- Mattapalkkikaaviot tehdään `ChartTheme.CreateMatteColumnSeries`-helperillä.
- Period-valinta tehdään `PeriodSelector`/`PeriodOptions`-polulla.
- Sivut eivät saa kovakoodata chart-värejä tai luoda sivukohtaisia chart-pintoja.
- Vanhaa `C:\Dev\Numeris` Svelte/Tauri UI:ta ei käytetä Numeriksen nykyisen UI:n lähteenä.

### 14. Verifikaatio

Aja:
- `dotnet run --project C:\Dev\Numeris\Numeris.Tests\Numeris.Tests.csproj`
- `dotnet build C:\Dev\Numeris\Numeris.slnx -p:Platform=x64`

Älä aja:
- `lc`
- `sc`

Visuaalinen tarkistus:
- Käynnistä Numeris unpackaged/projektiajona.
- Tarkista vähintään:
  - Overview
  - Cloudflare Traffic
  - Cloudflare Security
  - Cloudflare Status codes
  - Search Console Devices
  - Bing
  - Performance
  - Health
  - Sources
- Tarkista leveä ikkuna ja kapeampi ikkuna.
- Varmista, että:
  - sovellus on musta matta, ei honey amber -painotteinen
  - palkit vastaavat inspiraatiokuvan tyyliä
  - valittu/suurin/viimeisin palkki korostuu sinisellä
  - delta-arvot ovat vihreitä/punaisia
  - tekstit eivät leikkaudu
  - NavigationView ja content jakavat saman taustan
  - kortit eivät näytä tasapaksulta massalta

## Hyväksyntäkriteerit

- Numeris näyttää selvästi uudelta mattamustalta sovellukselta.
- Palkkikaavioiden tyyli on yhtenäinen kaikilla sivuilla.
- Design-tokenit ovat yksi totuus väreille, pinnoille ja chart-väreille.
- LiveCharts-tyyli on keskitetty `ChartTheme`/`ChartPalette`-polkuun.
- Tauri/Svelte/Chart.js-rakennetta ei mainita toteutuksessa.
- Build ja testiharness menevät läpi.
- `AGENTS.md` ja `memory/MEMORY.md` kuvaavat uuden UI-arkkitehtuurin.

## Oletukset

- Uudistus tehdään `C:\Dev\Numeris`-repoon, ei `C:\Dev\Numeris`-repoon.
- Nykyinen dirty worktree on käyttäjän/aiemman työn baseline; toteutuksessa ei saa palauttaa tai poistaa siihen liittyviä muutoksia ilman erillistä pyyntöä.
- Pakettilisäyksiä ei tehdä. Käytetään nykyisiä riippuvuuksia: Windows App SDK, WinUI 3, LiveChartsCore.SkiaSharpView.WinUI ja SkiaSharp.
- Viralliset lähteet suunnittelun tueksi: Microsoft Learn custom title bar, Microsoft Learn SelectorBar, LiveCharts WinUI paints/gradients ja LiveCharts WinUI bar samples.
