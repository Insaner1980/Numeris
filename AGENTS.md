# Numeris-agenttimuisti

- Numeris on vanhan Pulse Tauri -sovelluksen native WinUI 3 -migraatiokohde.
- Kaynnistys ajaa `PulseDataMigrationService.ImportAllAsync()` SQLite-alustuksen ja mock-seedauksen jalkeen. Importteri lukee Pulsen `%AppData%\com.finnvek.pulse\pulse.db`-tietokannan, tuo Cloudflare-, Web Analytics- ja Search Console -yhteysasetukset ja kopioi vanhat salaisuudet Numeriksen credential vaultiin.
- Pulse Taurin salaisuudet voivat olla Windows Credential Managerissa Rust keyringin kohteilla muodossa `<username>.Pulse`, esimerkiksi `cloudflare:example.com.Pulse`.
- Uusi Numeris-yhteysconfig ei saa serialisoida uusia plain text -salaisuuksia. Tokenit, client secretit ja refresh tokenit kuuluvat `CredentialVault`iin.
- `SettingsStore` tallentaa shellin kevyen tilan `AppPaths.SettingsPath`-polkuun. Packaged-ajo kayttaa `ApplicationData.Current.LocalFolder`ia ja unpackaged-ajo `%LocalAppData%\Numeris`-hakemistoa `AppPaths`-logiikan kautta.
- Pida `migration-plan.md` ajan tasalla, kun core-migraation jarjestys muuttuu.
