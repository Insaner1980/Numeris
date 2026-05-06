# Numeris-muisti

## 2026-05-06

- `PulseDataMigrationService` laajennettiin Web Analytics -tuonnista kaynnistyksen yleiseksi Pulse-tuonniksi Cloudflarelle, Web Analyticsille ja Search Consolelle.
- Vanha Pulse SQLite -config luetaan polusta `%AppData%\com.finnvek.pulse\pulse.db`.
- Vanhat Pulse keyring -salaisuudet luetaan Windows Credential Managerista Rust keyringin `<username>.Pulse`-kohdemallilla ja kopioidaan Numeriksen `CredentialVault`iin.
- Tuotuihin yhteysconfigeihin lisataan `ImportSource = "pulse-tauri"`, eika uusiin configeihin kirjoiteta plain text -salaisuuksia.
- Sources-sivun integraatiotesteille lisattiin yhteinen `ConnectionTestResult`-malli. Cloudflare-, Web Analytics- ja Search Console -testit kayttavat samoja tallennettuja credentialeja kuin sync ja ajavat kevyen live-kyselyn ennen varsinaista synkronointia.
- `SettingsStore` lisattiin tallentamaan `ShellViewModel`in `SelectedPeriod`, `SelectedDomain` ja `LastPage` JSONiin `AppPaths.SettingsPath`-polkuun. `MainWindow` palauttaa viimeksi avatun NavigationView-sivun kaynnistyksessa.
