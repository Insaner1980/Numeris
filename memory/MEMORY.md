# Numeris-muisti

## 2026-05-06

- `PulseDataMigrationService` laajennettiin Web Analytics -tuonnista kaynnistyksen yleiseksi Pulse-tuonniksi Cloudflarelle, Web Analyticsille ja Search Consolelle.
- Vanha Pulse SQLite -config luetaan polusta `%AppData%\com.finnvek.pulse\pulse.db`.
- Vanhat Pulse keyring -salaisuudet luetaan Windows Credential Managerista Rust keyringin `<username>.Pulse`-kohdemallilla ja kopioidaan Numeriksen `CredentialVault`iin.
- Tuotuihin yhteysconfigeihin lisataan `ImportSource = "pulse-tauri"`, eika uusiin configeihin kirjoiteta plain text -salaisuuksia.
- Sources-sivun integraatiotesteille lisattiin yhteinen `ConnectionTestResult`-malli. Cloudflare-, Web Analytics- ja Search Console -testit kayttavat samoja tallennettuja credentialeja kuin sync ja ajavat kevyen live-kyselyn ennen varsinaista synkronointia.
- `SettingsStore` lisattiin tallentamaan `ShellViewModel`in `SelectedPeriod`, `SelectedDomain` ja `LastPage` JSONiin `AppPaths.SettingsPath`-polkuun. `MainWindow` palauttaa viimeksi avatun NavigationView-sivun kaynnistyksessa.
- `ShellViewModel` muutettiin CommunityToolkit.Mvvm partial property -malliin. `Numeris.csproj` sai `<LangVersion>preview</LangVersion>`-asetuksen, koska partial properties vaatii C# 13/preview -kieliversion nykyisella toolchainilla.
- Unpackaged-kaynnistys kaatui ennen `App.OnLaunched`-metodia Windows App SDK:n DeploymentManager-auto-initializerissa virheeseen `REGDB_E_CLASSNOTREG`. Korjaus oli lisata `Numeris.csproj`-tiedostoon `<WindowsPackageType>None</WindowsPackageType>`, jolloin unpackaged build kayttaa bootstrap-auto-initializeria.
- Search Console -syncin HTTP 401 `CREDENTIALS_MISSING` johtui token-vastauksen snake_case-kentista. `SearchConsoleClient.TokenResponse` mapattiin eksplisiittisesti `access_token` ja `refresh_token` -kenttiin, ja tyhjalle access tokenille lisattiin guard ennen API-kutsuja.
- CrUX-, PageSpeed- ja Bing Webmaster Tools -integraatioiden ensimmainen core-toteutus lisattiin Sources-sivulle. CrUX ja PageSpeed kayttavat Google API key -avaimia `CredentialVault`ista; Bing kayttaa Bing Webmaster API keyta ilman OAuthia. `performance_urls` sisaltaa oletuksena `https://finnvek.com/` ja `https://knittoolsapp.com/`, ja URL:eja voi lisata manuaalisesti myohemmin.
- Performance-sync hakee CrUX `records:queryHistoryRecord` -dataa origin- ja URL-tasolla form factoreille ALL/PHONE/DESKTOP/TABLET, ajaa PageSpeedin MOBILE/DESKTOP-strategioilla ja tallentaa seka normalisoidut rivit etta raw JSONin. Bing-sync ajaa read-only Webmaster API -metodeja, normalisoi rank/query/page-rivit ja tallentaa muun datan `bing_raw_items`-tauluun.
