# Numeris-muisti

## 2026-05-06

- `PulseDataMigrationService` laajennettiin Web Analytics -tuonnista kaynnistyksen yleiseksi Pulse-tuonniksi Cloudflarelle, Web Analyticsille ja Search Consolelle.
- Vanha Pulse SQLite -config luetaan polusta `%AppData%\com.finnvek.pulse\pulse.db`.
- Vanhat Pulse keyring -salaisuudet luetaan Windows Credential Managerista Rust keyringin `<username>.Pulse`-kohdemallilla ja kopioidaan Numeriksen `CredentialVault`iin.
- Tuotuihin yhteysconfigeihin lisataan `ImportSource = "pulse-tauri"`, eika uusiin configeihin kirjoiteta plain text -salaisuuksia.
