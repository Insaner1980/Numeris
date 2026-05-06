# Numeris-agenttimuisti

- Numeris on vanhan Pulse Tauri -sovelluksen native WinUI 3 -migraatiokohde.
- Kaynnistys ajaa `PulseDataMigrationService.ImportAllAsync()` SQLite-alustuksen ja mock-seedauksen jalkeen. Importteri lukee Pulsen `%AppData%\com.finnvek.pulse\pulse.db`-tietokannan, tuo Cloudflare-, Web Analytics- ja Search Console -yhteysasetukset ja kopioi vanhat salaisuudet Numeriksen credential vaultiin.
- Pulse Taurin salaisuudet voivat olla Windows Credential Managerissa Rust keyringin kohteilla muodossa `<username>.Pulse`, esimerkiksi `cloudflare:example.com.Pulse`.
- Uusi Numeris-yhteysconfig ei saa serialisoida uusia plain text -salaisuuksia. Tokenit, client secretit ja refresh tokenit kuuluvat `CredentialVault`iin.
- CrUX-, PageSpeed- ja Bing Webmaster -integraatiot noudattavat samaa mallia: `connections`-tauluun tallennetaan vain konfiguraation metadata, API-avaimet tallennetaan `CredentialVault`iin. `performance_urls` on PageSpeedin ja URL-tason CrUX-haun lahdelista; oletuksena mukana ovat `https://finnvek.com/` ja `https://knittoolsapp.com/`, ja kayttaja voi lisata URL:eja manuaalisesti.
- Web Performance -data tallennetaan seka normalisoituna (`crux_metric_points`, `pagespeed_runs`, `pagespeed_audits`) etta raw JSONina, jotta API-vastauksen kaikki kentat sailyvat jatkojalostusta varten. Bing tallentaa yleiset taulut (`bing_rank_traffic`, `bing_query_stats`, `bing_page_stats`) ja kaiken muun read-only API-datan `bing_raw_items`-tauluun.
- `SettingsStore` tallentaa shellin kevyen tilan `AppPaths.SettingsPath`-polkuun. Packaged-ajo kayttaa `ApplicationData.Current.LocalFolder`ia ja unpackaged-ajo `%LocalAppData%\Numeris`-hakemistoa `AppPaths`-logiikan kautta.
- Projekti kayttaa `<LangVersion>preview</LangVersion>`-asetusta, jotta CommunityToolkit.Mvvm partial property -malli toimii MVVMTK0045-korjauksissa.
- Unpackaged `Project`-kaynnistys tarvitsee `<WindowsPackageType>None</WindowsPackageType>`-asetuksen, jotta Windows App SDK kayttaa bootstrap-auto-initializeria eika MSIX DeploymentManageria.
- Pida `migration-plan.md` ajan tasalla, kun core-migraation jarjestys muuttuu.
