# Numeris-agenttimuisti

- Numeris on native WinUI 3 -sovellus, johon vanhan Pulse Tauri -sovelluksen data tuodaan legacy-importtina.
- Kaynnistys alustaa SQLite-skeeman, ajaa `LegacyDataMigrationService.ImportAllAsync()`-tuonnin ja avaa sovelluksen ilman mock-siementa. Importteri keskittaa vanhan Pulse-lahteen `LegacyAppSource`-määrittelyyn, lukee `%AppData%\com.finnvek.pulse\pulse.db`-tietokannan, tuo Cloudflare-, Web Analytics- ja Search Console -yhteysasetukset ja kopioi vanhat salaisuudet Numeriksen credential vaultiin.
- Sovellus ei saa generoida demo-/mock-analytiikkadataa kaynnistyksessa. Tyhjan tai poistetun integraation yhteystila on `disconnected`; `mock` saa esiintya vain legacy-tuonnin vanhan statuksen normalisoinnissa tai migraatiossa, joka siivoaa aiemman mock-tilan pois.
- Legacy-salaisuudet voivat olla Windows Credential Managerissa Rust keyringin kohteilla muodossa `<username>.Pulse`, esimerkiksi `cloudflare:example.com.Pulse`. Uudet ja migroidut salaisuudet kirjoitetaan Numeriksen `CredentialVault`-avaimiin; legacy-avaimia ei poisteta automaattisesti.
- Uusi Numeris-yhteysconfig ei saa serialisoida uusia plain text -salaisuuksia. Tokenit, client secretit ja refresh tokenit kuuluvat `CredentialVault`iin.
- CrUX-, PageSpeed-, Bing Webmaster- ja YouTube-integraatiot noudattavat samaa salaisuusmallia: `connections`-tauluun tallennetaan vain konfiguraation metadata, API-avaimet, OAuth client secretit ja refresh tokenit tallennetaan `CredentialVault`iin. `performance_urls` on PageSpeedin ja URL-tason CrUX-haun lahdelista; oletuksena mukana ovat `https://finnvek.com/` ja `https://knittoolsapp.com/`, ja kayttaja voi lisata URL:eja manuaalisesti.
- `connections` toimii integraatioiden registry-tauluna. CrUX ja PageSpeed kirjataan erillisiksi riveiksi (`crux`, `pagespeed`) vanhan `perf`-koontitilan rinnalle, ja YouTube kirjataan omaksi `youtube`-riviksi omalla OAuth-yhteydellaan, jotta integraatioiden statukset eivat lukitu yhteen monoliittiseen yhteyteen.
- Web Performance -data tallennetaan normalisoituna (`crux_metric_points`, `pagespeed_runs`, `pagespeed_audits`) ja rajattuna raw JSONina. `RawJsonStoragePolicy` maarittaa raw-payloadien maksimikoot, PageSpeed-runien retentionin ja Bing raw -rivien retentionin, ja redaktoi tokenit, API-avaimet, client secretit, salasanat ja bearer-arvot ennen SQLite-tallennusta.
- Bing tallentaa yleiset taulut (`bing_rank_traffic`, `bing_query_stats`, `bing_page_stats`) ja muun read-only API-datan `bing_raw_items`-tauluun. `bing_page_stats` ja query/page raw item -avaimet sailyttavat API:n Date-kentaan perustuvan historian, eivat vain viimeisinta sivu/query-rivia.
- YouTube tallentaa yhden autentikoidun oman kanavan datan tauluihin `youtube_channels`, `youtube_videos`, `youtube_daily`, `youtube_video_stats`, `youtube_countries`, `youtube_traffic_sources`, `youtube_devices` ja `youtube_retention_points`. YouTube v1 ei kayta appi-/domain-jakoa; data suodatetaan YouTube-sivulla vain periodin mukaan.
- SQLite-skeeman versiointi kulkee `Migrations.CurrentSchemaVersion`- ja `PRAGMA user_version` -polun kautta. Kun taulurakennetta muutetaan, lisaa versionumeroon sidottu migraatio `RunPendingMigrations`-polkuun ja paivita myos `meta.schema_version`.
- Moniriviset repository-kirjoitukset, joissa yhden loogisen tuloksen rivit kuuluvat yhteen, ajetaan `SqliteDatabase.WriteTransactionAsync`-helperin kautta. Sync-palvelut eivat avaa tietokantatransaktioita itse.
- Windowsissa `lc` ja `sc` ovat PowerShell-profiilin funktioita, jotka kutsuvat `tools/lint-check.ps1` ja `tools/security-check.ps1`. `sc` pitaa toteuttaa funktiona, koska Windowsin oma `sc.exe` loytyy PATHista muuten ensin. Profiilissa tulee olla muodot `function lc { lint-check @args }` ja `function sc { security-check @args }`.
- `lint-check` kirjoittaa raportit `reports/ktlint.txt`, `reports/detekt.txt` ja `reports/lint.txt`. `security-check` kirjoittaa raportit `reports/security-code.txt` ja `reports/security-deps.txt`. `reports/` pysyy gitignoressa.
- Bing Webmaster -syncin automaattinen ydin on rajattu metodeihin `GetUserSites`, `GetRankAndTrafficStats`, `GetQueryStats`, `GetPageStats`, `GetCrawlStats` ja `GetCrawlIssues`. Detail-metodeja ei lisata massana; tarkista `BING-WEBMASTER-SYNC.md` ennen uusien Bing-metodien toteutusta.
- API-clienttien virheviestit eivat saa sisaltaa raw response bodya, request-URLia tai avaimia. Kayta `ApiRequestException`- ja `ApiErrorMessage`-polkua ennen virheen nayttamista UI:ssa tai tallentamista SQLiteen.
- Site/domain/origin/URL-normalisoinnin yksi totuus on `SiteIdentity`. Paljaille domaineille, origin-URL:eille ja kotisivun URL:eille kaytetaan sita ennen tallennusta tai API-kohteen muodostusta. PageSpeedin URL-tason normalisointi kulkee edelleen `PerformanceUrl`-facaden kautta, mutta se delegoi `SiteIdentity`lle.
- Sources-sivun koonti-ViewModel on `SourcesViewModel`, mutta lahdekohtainen toiminta kuuluu `Numeris.ViewModels.Sources`-luokille: `CloudflareSourceViewModel`, `WebAnalyticsSourceViewModel`, `SearchConsoleSourceViewModel`, `PerformanceSourceViewModel`, `YouTubeSourceViewModel` ja `BingSourceViewModel`.
- Sync-palvelut eivat kirjoita suoraan `SqliteDatabase`en. API-client hakee datan, SyncService orkestroi tokenit ja aikavalit, Repository tekee SQLite-luku- ja kirjoitusoperaatiot, ja ViewModel hoitaa UI-tilan.
- Raportointinakyminen vastuut ovat erilliset: `CloudflarePage` nayttaa Cloudflare/Web Analytics -liikennedatan, `SearchConsolePage` nayttaa Google Search Console -datan, `BingPage` nayttaa Microsoft Bing Webmaster Tools -datan, `PerformancePage` nayttaa CrUX- ja PageSpeed-datan ja `YouTubePage` nayttaa YouTube-kanavan trendit, videot, maat, traffic sourcet, laitteet ja retentionin. `SourcesPage` on vain konfigurointi-, testaus- ja sync-nakyma, ei raportointisivu.
- Search Console Indexing -tabin indeksointidata tulee URL Inspection APIsta, ei Search Analytics -riveista eika pelkasta sitemap-listasta. Sivun ylaoikean refresh-napin tulee Indexing-tabilla ajaa `SearchConsoleSyncService.InspectSitemapUrlsAsync()`; erillista indexing-refresh-nappia ei kayteta. Sync-palvelu kayttaa olemassa olevaa OAuth-yhteytta, hakee propertyn `SearchConsoleClient.PropertyForDomain`-logiikalla, tarkistaa aktiiviset sitemap-URLit `urlInspection/index:inspect`-endpointilla ja tallentaa tulokset `sitemap_urls`-taulun inspection-kenttiin.
- `DashboardPage`/Overview on kevyt koonti, ei detail-analyysi. Overview nostaa Cloudflare visitors, Google clicks, Bing clicks, CrUX Web Vitals -statuksen, PageSpeed mobile -scoretason ja viimeisimman syncin; tarkemmat query/page/crawl/performance-listat kuuluvat lahdetyyppien omille sivuille.
- WinUI-visuaalinen jarjestelma keskitetaan `Numeris/Themes/Tokens.xaml`-resursseihin. `MainWindow` kayttaa `MicaBackdrop`ia rauhallisena shell-materiaalina ja fallbackina, mutta nakyva sovellustausta on shell-tason oma bitmap-asset `Numeris/Assets/AppBackdrop.png`, jonka paalla on `AppBackdropScrimBrush`-himmennyskerros. `#444444` on app-taustan fallback, leveassa ikkunassa shell kayttaa expanded left `NavigationView` -paneelia eika ikonipelkkaa railia, NavigationView kayttaa lapinakyvaa shell-taustaa ja `NavigationLayerBrush`-resurssia, kortti-, chart-, status- ja tekstipinnat kayttavat yhteisia tokeneita, custom-titlebar on `MainWindow`in lapinakyva `AppTitleBar`-rivi ja LiveCharts-varit kulkevat `Numeris.Themes.ChartPalette`-luokan kautta eivatka ViewModelien kovakoodattuina hexeina.
- Sovelluksen taidekerros ei ole sivukohtainen Dashboard-hero vaan `MainWindow`in globaali `AppBackdropImage`. Taustan tulee olla tumma, viivaton, pehmea lasi/satiininauha-tyylinen bitmap, jossa honey amber -korostus on hienovarainen ja paasisaltoalue pysyy matalakontrastisena luettavuuden vuoksi. Tausta alistetaan UI:lle vahvalla `AppBackdropScrimBrush`-himmennyksella, NavigationViewin content-grid border pidetaan pois paalta, korttipinnat ovat taustaa selvemmin lukupintoja ja titlebar sulautetaan shelliin `ExtendsContentIntoTitleBar` + `SetTitleBar(AppTitleBar)` -polulla.
- Kortti-, chart- ja kontrollipinnat kayttavat tummaa black-glass-tokenisopimusta eivatka vaaleaa harmaata white-overlayta: `CardSurfaceColor`, `ControlSurfaceColor`, `ChartPanelColor` ja `ContentLayerColor` ovat mustapohjaisia lapikuultavia pintoja, jotta ne sopivat `AppBackdrop.png`-taustaan. Sivujen yla-header ei ole kortti: `PageHeaderBorderStyle` on lapinakyva ja reunaton layout-pinta.
- NavigationView-paneeli ei saa omaa erivarista vignette-/taustakerrosta. `NavigationLayerColor` on lapinakyva (`#00000000`), jotta vasen nav-alue nayttaa samaa `AppBackdrop.png`-taustaa kuin muu sovellus; vain valittu/hoverattu nav-item saa oman overlaynsa.
- Myos content-alueen yleista taustakerrosta ei saa tummentaa erikseen navista: `ContentLayerColor` on lapinakyva (`#00000000`), jotta shellin vasen ja oikea puoli jakavat saman taustakuvan ilman varirajaa. Luettavuus hoidetaan korteilla/header-pinnoilla, ei koko content-alueen scrimilla.
- Overview-, Cloudflare-, Google Search-, Bing-, Performance-, YouTube- ja Health-sivujen ylaosa kayttaa yhteista `PageHeaderBorderStyle`-layoutia: otsikko, domain-valinta kun soveltuu, period-valinta, refresh ja sivukohtaiset valitsimet kuuluvat samaan rauhalliseen header-alueeseen, mutta header ei piirra taustaa, reunaa tai korttimaista kehysta. Period-valintojen yksi totuus on `PeriodOptions.All` / `Period.DisplayLabel()`, jotta UI:ssa ei nay raakaa enum-arvoa kuten `Last7Days`.
- Raporttisivujen sisainen nakymavalinta kayttaa `SelectorBar`-kontrollia ja `TopTabSelectorBarStyle`-tyylia. Sisaisia raporttitabeja ei toteuteta nested `NavigationView` -kontrolleina; `NavigationView` kuuluu vain paashalliin.
- Raporttisivujen refresh-painikkeet sidotaan ViewModelien `CanRefresh`-propertyyn ja niilla seka domain/period/filter/sort-kontrolleilla on `AutomationProperties.Name`. Search Console Indexing -tabilla sama refresh kaynnistaa edelleen URL Inspection -polun, mutta busy-tila estaa rinnakkaisajon.
- Sources-sivun lahdekohtaiset ViewModelit exposeeraavat `CanRun`- ja `HasStatusMessage`-propertyt UI:lle. `SourcesPage` nayttaa integraatioiden tilaviestit `InfoBar`-pinnoilla, estaa saman lahteen rinnakkaiset Test/Sync/Save/Add/Delete-ajot busy-tilassa ja kayttaa `DangerActionButtonStyle`-tyylia poistotoiminnoissa.
- Taulukkomaisten listojen header-pinnat kayttavat `DataTableHeaderBorderStyle`-tyylia ja palkkilistojen tyhja tila kuuluu `HorizontalBars`-kontrollille. Uusia listapintoja ei rakenneta sivukohtaisilla kovakoodatuilla header-backgroundeilla, jos yhteinen data table -tyyli riittaa.
- Cloudflare Traffic -nakyman Top countries -kortti kayttaa vain `HorizontalBars`-kontrollia ja samaa `BarRow`-dataa kuin muut listavisualisoinnit. `CountryTrafficMap`-, WebView2-, MapLibre-, topojson- ja world-atlas -vastuut on poistettu; `Numeris/Assets/Maps`-assetteja ei pideta mukana. Jos maantieteellinen kartta palautetaan joskus, se suunnitellaan uutena erillisena toteutuksena, ei osaksi nykyista palkkilistaa.
- LiveChartsin WinUI-rakenteen yksi totuus on `Numeris.Themes.ChartTheme`: chart-kontrollien luonti, `CreateChartSurface`-pinnan hienovarainen 2.5D mesh-tausta, draw margin -tausta, legend/tooltip-paintit seka akselien label/grid-paintit keskitetaan sinne. ViewModelit voivat muodostaa sarjat, mutta niiden akselit tulee viimeistella `ChartTheme.StyleXAxis`/`StyleYAxis`-helperien kautta.
- `SettingsStore` tallentaa shellin kevyen tilan `AppPaths.SettingsPath`-polkuun. Packaged-ajo kayttaa `ApplicationData.Current.LocalFolder`ia ja unpackaged-ajo `%LocalAppData%\Numeris`-hakemistoa `AppPaths`-logiikan kautta.
- Projekti kayttaa `<LangVersion>preview</LangVersion>`-asetusta, jotta CommunityToolkit.Mvvm partial property -malli toimii MVVMTK0045-korjauksissa.
- Unpackaged `Project`-kaynnistys tarvitsee `<WindowsPackageType>None</WindowsPackageType>`-asetuksen, jotta Windows App SDK kayttaa bootstrap-auto-initializeria eika MSIX DeploymentManageria.
- Pida `migration-plan.md` ajan tasalla, kun core-migraation jarjestys muuttuu.


<claude-mem-context>
# Memory Context

# [Numeris] recent context, 2026-05-12 3:47pm GMT+3

Legend: 🎯session 🔴bugfix 🟣feature 🔄refactor ✅change 🔵discovery ⚖️decision 🚨security_alert 🔐security_note
Format: ID TIME TYPE TITLE
Fetch details: get_observations([IDs]) | Search: mem-search skill

Stats: 47 obs (21,977t read) | 2,895,052t work | 99% savings

### May 6, 2026
5184 6:43p 🔵 CredentialVault handles secrets via Windows PasswordVault with replacement support
5185 " 🔵 Legacy Pulse migration is safe, idempotent, and cautious with plain text fallbacks
5186 " 🚨 Raw JSON storage does not redact secrets from API error responses
5192 " 🔴 Legacy migration now preserves existing Numeris secrets
5193 " 🔴 Credential vault delete methods now remove secrets instead of storing empty strings
5194 " 🚨 SearchConsoleClient sanitizes API error bodies before showing in UI
5195 7:03p 🔵 Numeris.Tests Project Has Self-Contained Configuration Mismatch
5196 " 🔵 MVVM Toolkit 8.4 Partial Property Migration Complete Across All ViewModels
5197 " 🔵 Custom Test Harness Validates Architecture Without Live API Keys
5198 " 🔵 WinUI3 MSIX Packaging Configured with Manual Chart Construction
5202 7:16p 🔵 Dashboard Integration Design Question for CrUX, PageSpeed, and Bing Data
5224 " 🔵 Visual Design Document Defines Windows 11 Native Aesthetic for Numeris
5229 " 🟣 Centralized WinUI Visual Design System with Mica Backdrop
5230 " 🔄 LiveCharts Color Palette Centralized from ViewModels
5231 " ✅ Dashboard Page Redesigned with Centralized Design Tokens
5232 " ✅ KpiCard Control Converted to Design Token System
5233 " 🟣 Architectural Tests Added for WinUI Visual System
5234 " ✅ Documentation Updated with WinUI Visual System Architecture
5207 7:27p 🔵 Numeris core migration implementation status verified
5208 " 🔵 Numeris.Tests project has self-contained reference mismatch
5209 " 🔵 migration-plan.md references obsolete PulseDataMigrationService class name
5213 " 🔴 Fixed NETSDK1151 build error by making both projects framework-dependent
5214 " 🟣 Added Bearer token redaction to API error message sanitization
5215 " ✅ Created CORE-COMPLETION-PLAN.md with verification and commit strategy
5216 " 🔄 Removed unused StatusCode property from ConnectionTestResult
5222 7:31p 🔴 CrUX test now treats HTTP 404 as missing field data instead of API key failure
5223 " ✅ Numeris Core Completion plan fully implemented with four commits
5236 8:21p 🟣 WinUI Visual Design System Centralized in Tokens.xaml
5237 " 🟣 LiveCharts Color Palette Centralized in ChartPalette Class
5238 " ✅ Dashboard Page Updated as WinUI Visual System Reference Implementation
5239 " ✅ Cloudflare Page Visual System Applied with Shared Component Styles
5240 " ✅ Architectural Tests Enforce WinUI Visual System Usage
5241 " ✅ WinUI Visual System Documentation Added to AGENTS.md and MEMORY.md
5244 8:48p 🔄 Sources page migrated to shared design system
### May 7, 2026
5254 12:07p 🟣 WinUI visual design system centralized with hero backdrop asset
5255 12:20p 🔴 Reverted window backdrop from Desktop Acrylic to Mica
S763 UI surface transparency adjustments to balance navigation and content layers with backdrop (May 7, 2:54 PM)
S764 Implement 2.5D mesh backdrop for LiveCharts visualizations in Numeris WinUI application (May 7, 4:54 PM)
5270 5:02p 🟣 3D mesh backdrop added to charts for depth visualization
5271 6:56p 🔴 Sources page Expander controls now maintain full width when collapsed
5272 " 🟣 Country traffic map implemented with WebView2 and MapLibre GL JS
5273 9:54p 🔴 Fixed MapLibre country map clipping and horizontal line artifacts
5274 11:01p ✅ Reverted country map visualization to previous shadowed gradient version
### May 8, 2026
**5275** 4:40p 🔵 **Navigation Icons Have Inconsistent Visual Sizes**
Pixel-level analysis revealed why navigation brand icons appear inconsistent: while all three PNG files (bing.png, cloudflare.png, google-search.png) use 64×64px canvases, their actual opaque content occupies dramatically different regions. Bing's logo occupies only a 28×43px centered box, Cloudflare spans the full 64px width but only 31px height, and Google Search Console fills nearly the entire 61×61px area. This explains the visual size mismatch reported by the user where Cloudflare and Google icons appear much larger than Bing, and the filled vs outlined style differences.
~278t 🔍 2,975

**5278** " 🔴 **Navigation icon sizing inconsistency discovered**
Pixel-level analysis revealed that navigation brand icons have inconsistent visual sizes - Bing content occupies 28x43px, Cloudflare 64x31px, and Google Search 61x61px within their 64x64 canvases. Additionally, the Cloudflare and Google Search Console icons use a filled style while other navigation icons are outlines, creating visual inconsistency. The fix approach involves generating uniform 20x20 outline versions with a morphological stroke operation to ensure all brand icons match the navigation's visual style.
~261t 🛠️ 8,900

**5279** " 🔵 **PowerShell icon generation script fails due to incorrect source paths**
The morphological outline generation script attempts to load source icons from incorrect paths. It uses $srcPath = Join-Path $srcDir $item.Source where $srcDir is the project root (C:\Dev\Numeris) and $item.Source is just the filename (e.g., 'bing.png'). This results in looking for C:\Dev\Numeris\bing.png when the actual file is at C:\Dev\Numeris\Numeris\Assets\Icons\bing.png. The script completes with exit code 0 but produces no output or generated files, causing the test to continue failing on missing Width="20" attributes and -outline.png filenames.
~282t 🔍 8,276

5280 " 🟣 Generated uniform outline navigation icons using morphological operations
**5283** 4:45p 🔴 **Fixed navigation icon sizing and style inconsistency**
Completed the navigation icon sizing fix by updating MainWindow.xaml to add explicit 20x20 dimension constraints on all brand ImageIcon elements and switching references to the generated outline versions. The Numeris.csproj file was updated to include the new -outline.png assets. The Cloudflare icon was regenerated with aspect-ratio-aware scaling (52x38 target instead of uniform 52x52) to better match its wide logo shape, resulting in 56x42px bounds. All three navigation icons now display with consistent visual sizing and outline style, replacing the previous inconsistent setup where logos ranged from 28-64px and mixed filled/outline styles.
~358t 🛠️ 42,534

**5286** 4:49p 🔴 **Navigation icon consistency fix completed and verified**
Successfully completed the navigation icon sizing and style consistency bugfix. After multiple failed attempts with high-resolution morphology and C# compilation, the direct 64px PowerShell approach generated all three outline icons with uniform dimensions. The Cloudflare icon was regenerated with non-uniform scaling to preserve its wide aspect ratio. All unit tests now pass, including the updated test that verifies Width="20" Height="20" attributes and -outline.png filenames. The solution builds cleanly, and visual inspection confirms the icons display with consistent outline styling and proportional sizing, replacing the original mismatched filled logos that ranged from 28-64px in their dominant dimensions.
~379t 🛠️ 37,893


Access 2895k tokens of past work via get_observations([IDs]) or mem-search skill.
</claude-mem-context>
