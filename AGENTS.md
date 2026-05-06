# Numeris-agenttimuisti

- Numeris on native WinUI 3 -sovellus, johon vanhan Pulse Tauri -sovelluksen data tuodaan legacy-importtina.
- Kaynnistys ajaa `LegacyDataMigrationService.ImportAllAsync()` SQLite-alustuksen ja mock-seedauksen jalkeen. Importteri keskittaa vanhan Pulse-lahteen `LegacyAppSource`-määrittelyyn, lukee `%AppData%\com.finnvek.pulse\pulse.db`-tietokannan, tuo Cloudflare-, Web Analytics- ja Search Console -yhteysasetukset ja kopioi vanhat salaisuudet Numeriksen credential vaultiin.
- Legacy-salaisuudet voivat olla Windows Credential Managerissa Rust keyringin kohteilla muodossa `<username>.Pulse`, esimerkiksi `cloudflare:example.com.Pulse`. Uudet ja migroidut salaisuudet kirjoitetaan Numeriksen `CredentialVault`-avaimiin; legacy-avaimia ei poisteta automaattisesti.
- Uusi Numeris-yhteysconfig ei saa serialisoida uusia plain text -salaisuuksia. Tokenit, client secretit ja refresh tokenit kuuluvat `CredentialVault`iin.
- CrUX-, PageSpeed- ja Bing Webmaster -integraatiot noudattavat samaa mallia: `connections`-tauluun tallennetaan vain konfiguraation metadata, API-avaimet tallennetaan `CredentialVault`iin. `performance_urls` on PageSpeedin ja URL-tason CrUX-haun lahdelista; oletuksena mukana ovat `https://finnvek.com/` ja `https://knittoolsapp.com/`, ja kayttaja voi lisata URL:eja manuaalisesti.
- `connections` toimii integraatioiden registry-tauluna. CrUX ja PageSpeed kirjataan erillisiksi riveiksi (`crux`, `pagespeed`) vanhan `perf`-koontitilan rinnalle, jotta Web Performance -integraatioiden statukset eivat lukitu yhteen monoliittiseen yhteyteen.
- Web Performance -data tallennetaan normalisoituna (`crux_metric_points`, `pagespeed_runs`, `pagespeed_audits`) ja rajattuna raw JSONina. `RawJsonStoragePolicy` maarittaa raw-payloadien maksimikoot, PageSpeed-runien retentionin ja Bing raw -rivien retentionin, ja redaktoi tokenit, API-avaimet, client secretit, salasanat ja bearer-arvot ennen SQLite-tallennusta.
- Bing tallentaa yleiset taulut (`bing_rank_traffic`, `bing_query_stats`, `bing_page_stats`) ja muun read-only API-datan `bing_raw_items`-tauluun. `bing_page_stats` ja query/page raw item -avaimet sailyttavat API:n Date-kentaan perustuvan historian, eivat vain viimeisinta sivu/query-rivia.
- SQLite-skeeman versiointi kulkee `Migrations.CurrentSchemaVersion`- ja `PRAGMA user_version` -polun kautta. Kun taulurakennetta muutetaan, lisaa versionumeroon sidottu migraatio `RunPendingMigrations`-polkuun ja paivita myos `meta.schema_version`.
- Moniriviset repository-kirjoitukset, joissa yhden loogisen tuloksen rivit kuuluvat yhteen, ajetaan `SqliteDatabase.WriteTransactionAsync`-helperin kautta. Sync-palvelut eivat avaa tietokantatransaktioita itse.
- Windowsissa `lc` ja `sc` ovat PowerShell-profiilin funktioita, jotka kutsuvat `tools/lint-check.ps1` ja `tools/security-check.ps1`. `sc` pitaa toteuttaa funktiona, koska Windowsin oma `sc.exe` loytyy PATHista muuten ensin. Profiilissa tulee olla muodot `function lc { lint-check @args }` ja `function sc { security-check @args }`.
- `lint-check` kirjoittaa raportit `reports/ktlint.txt`, `reports/detekt.txt` ja `reports/lint.txt`. `security-check` kirjoittaa raportit `reports/security-code.txt` ja `reports/security-deps.txt`. `reports/` pysyy gitignoressa.
- Bing Webmaster -syncin automaattinen ydin on rajattu metodeihin `GetUserSites`, `GetRankAndTrafficStats`, `GetQueryStats`, `GetPageStats`, `GetCrawlStats` ja `GetCrawlIssues`. Detail-metodeja ei lisata massana; tarkista `BING-WEBMASTER-SYNC.md` ennen uusien Bing-metodien toteutusta.
- API-clienttien virheviestit eivat saa sisaltaa raw response bodya, request-URLia tai avaimia. Kayta `ApiRequestException`- ja `ApiErrorMessage`-polkua ennen virheen nayttamista UI:ssa tai tallentamista SQLiteen.
- Site/domain/origin/URL-normalisoinnin yksi totuus on `SiteIdentity`. Paljaille domaineille, origin-URL:eille ja kotisivun URL:eille kaytetaan sita ennen tallennusta tai API-kohteen muodostusta. PageSpeedin URL-tason normalisointi kulkee edelleen `PerformanceUrl`-facaden kautta, mutta se delegoi `SiteIdentity`lle.
- Sources-sivun koonti-ViewModel on `SourcesViewModel`, mutta lahdekohtainen toiminta kuuluu `Numeris.ViewModels.Sources`-luokille: `CloudflareSourceViewModel`, `WebAnalyticsSourceViewModel`, `SearchConsoleSourceViewModel`, `PerformanceSourceViewModel` ja `BingSourceViewModel`.
- Sync-palvelut eivat kirjoita suoraan `SqliteDatabase`en. API-client hakee datan, SyncService orkestroi tokenit ja aikavalit, Repository tekee SQLite-luku- ja kirjoitusoperaatiot, ja ViewModel hoitaa UI-tilan.
- `SettingsStore` tallentaa shellin kevyen tilan `AppPaths.SettingsPath`-polkuun. Packaged-ajo kayttaa `ApplicationData.Current.LocalFolder`ia ja unpackaged-ajo `%LocalAppData%\Numeris`-hakemistoa `AppPaths`-logiikan kautta.
- Projekti kayttaa `<LangVersion>preview</LangVersion>`-asetusta, jotta CommunityToolkit.Mvvm partial property -malli toimii MVVMTK0045-korjauksissa.
- Unpackaged `Project`-kaynnistys tarvitsee `<WindowsPackageType>None</WindowsPackageType>`-asetuksen, jotta Windows App SDK kayttaa bootstrap-auto-initializeria eika MSIX DeploymentManageria.
- Pida `migration-plan.md` ajan tasalla, kun core-migraation jarjestys muuttuu.


<claude-mem-context>
# Memory Context

# [Numeris] recent context, 2026-05-06 7:37pm GMT+3

Legend: 🎯session 🔴bugfix 🟣feature 🔄refactor ✅change 🔵discovery ⚖️decision 🚨security_alert 🔐security_note
Format: ID TIME TYPE TITLE
Fetch details: get_observations([IDs]) | Search: mem-search skill

Stats: 14 obs (5,469t read) | 1,408,461t work | 100% savings

### May 6, 2026
5184 6:43p 🔵 CredentialVault handles secrets via Windows PasswordVault with replacement support
5185 " 🔵 Legacy Pulse migration is safe, idempotent, and cautious with plain text fallbacks
5186 " 🚨 Raw JSON storage does not redact secrets from API error responses
5192 " 🔴 Legacy migration now preserves existing Numeris secrets
5193 " 🔴 Credential vault delete methods now remove secrets instead of storing empty strings
5194 " 🚨 SearchConsoleClient sanitizes API error bodies before showing in UI
**5195** 7:03p 🔵 **Numeris.Tests Project Has Self-Contained Configuration Mismatch**
The Numeris.Tests project is a console executable test harness that references the main Numeris WinUI3 project. The test project explicitly sets SelfContained=true, while the main project relies on default framework-dependent behavior. This creates a reference conflict where dotnet build rejects the ProjectReference due to incompatible deployment models. The test suite contains 23+ architectural and integration tests written as direct assertions in Program.cs, checking code structure patterns like MVVM property declarations, service layering, and security practices. No traditional test framework (MSTest/xUnit/NUnit) is present. To run tests, the SelfContained mismatch must be resolved by either making both projects self-contained or both framework-dependent, accounting for WinUI3 packaging requirements.
~380t 🔍 87,534

5196 " 🔵 MVVM Toolkit 8.4 Partial Property Migration Complete Across All ViewModels
5197 " 🔵 Custom Test Harness Validates Architecture Without Live API Keys
5198 " 🔵 WinUI3 MSIX Packaging Configured with Manual Chart Construction
**5202** 7:16p 🔵 **Dashboard Integration Design Question for CrUX, PageSpeed, and Bing Data**
The Numeris WinUI3 analytics dashboard recently added CrUX (Chrome User Experience Report), PageSpeed Insights, and Bing Webmaster Tools integrations at the data layer. The backend sync services, API clients, database repositories, and SQLite schema are complete for all three sources. However, the DashboardPage currently only visualizes Cloudflare RUM traffic (visitors, pageviews) and Google Search Console search performance (clicks, impressions, average position). The project now faces design decisions about how to surface the new data: whether to keep the dashboard lightweight by showing only key metrics or add dedicated sections for Core Web Vitals and PageSpeed scores; whether to unify Bing and Google search data into a single "search visibility" view with a provider column or maintain separate views; and which metrics deserve first-screen prominence versus navigation to dedicated pages like the existing CloudflarePage, SearchConsolePage, and HealthPage.
~486t 🔍 43,239

**5207** 7:27p 🔵 **Numeris core migration implementation status verified**
The primary session performed a comprehensive status check on the Numeris WinUI3 migration implementation. Key verification points: LegacyDataMigrationService.ImportAllAsync reads Pulse's legacy SQLite database and Windows keyring credentials, migrates them to Numeris CredentialVault, and marks imported configs with ImportSource tag. Sources page UI delegates to five separate source-specific ViewModels instead of a monolithic SourcesViewModel. SettingsStore saves shell state (period/domain/page) to JSON in AppPaths.SettingsPath. All integrations use ConnectionTestResult for lightweight validation before sync. CrUX/PageSpeed/Bing store raw API JSON with RawJsonStoragePolicy limits and secret redaction. SQLite Migrations.CurrentSchemaVersion = 2 with RunV2Migration handling bing_page_stats schema change. The solution builds successfully with zero errors, confirming core architecture is complete.
~510t 🔍 126,397

**5208** " 🔵 **Numeris.Tests project has self-contained reference mismatch**
The test project configuration prevents running tests via dotnet run. Numeris.Tests.csproj declares SelfContained=true with a single RuntimeIdentifier=win-x64, while the main Numeris.csproj uses RuntimeIdentifiers (plural) for cross-platform WinUI builds without SelfContained. The .NET SDK prevents a self-contained executable from referencing a non-self-contained one. The build succeeds because the test project compiles, but runtime execution fails. The tests contain architecture/pattern checks (SourcesViewModel delegation, RawJsonStoragePolicy usage, partial properties, transaction batching) that cannot run until the reference model is aligned. Either Numeris.Tests must drop SelfContained, or Numeris must add it (which breaks WinUI multi-platform packaging).
~349t 🔍 126,397

**5209** " 🔵 **migration-plan.md references obsolete PulseDataMigrationService class name**
The migration plan documentation contained a stale reference to the old service class name. The implementation evolved from Pulse-specific naming (PulseDataMigrationService, PulseCredentialReader) to generic legacy import naming (LegacyDataMigrationService, LegacyCredentialReader) with a centralized LegacyAppSource definition. The git diff confirms migration-plan.md was updated to reflect the current LegacyDataMigrationService design. This is a documentation drift issue where the plan file wasn't updated when the class was renamed. The change aligns the plan with the actual code structure.
~309t 🔍 126,397


Access 1408k tokens of past work via get_observations([IDs]) or mem-search skill.
</claude-mem-context>
