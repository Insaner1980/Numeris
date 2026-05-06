# Numeris Core Completion Plan

## Summary

Tavoite on saada Numeriksen tärkeimmät toiminnot luotettavasti toimimaan ennen ulkonäön hiomista: live-integraatiot, vanhan Pulsen tunnusten migraatio, synkronoinnit, asetusten pysyvyys ja packaged/unpackaged-ajon smoke testit.

Nykytila: vaiheet 0-5C ovat pääosin toteutettu. Suurin keskeneräinen osa on integraatioiden koventaminen: Numeris ei saa vaatia tunnusten manuaalista syöttöä, vaan sen pitää migroida Pulsen toimivat asetukset ja tokenit automaattisesti. UI-polish, brändiassetit ja hienommat layoutit jätetään viimeiseksi.

## Key Changes

### 1. Lukitse nykyinen Web Analytics -korjaus

- Viimeistele ja committoi nykyiset muutokset: Pulse Web Analytics -migraatio, manuaalinen site tag mapping, tarkempi GraphQL-virheviesti ja PasswordBox-tokenin eksplisiittinen lukeminen.
- Varmista, että Numeris käynnistyessään tuo Pulsen `pulse.db`:stä:
  - Web Analytics account id
  - Web Analytics API token
  - `web_analytics_sites` domain/site_tag mappingit
- Päivitä Sources-tekstit niin, ettei käyttäjää ohjata pakollisesti `Discover sites` -polkuun. `Sync` on ensisijainen, `Discover sites` on valinnainen debug/discovery-toiminto.

### 2. Laajenna Pulse -> Numeris credential-migraatio kaikkiin integraatioihin

- Lisää sama migraatiomalli Cloudflare-zone-yhteyksille:
  - domain
  - zone id
  - Cloudflare token
  - connection status/config
- Lisää sama migraatiomalli Search Consolelle:
  - client id
  - client secret
  - refresh token jos Pulse tallensi sen
  - tallennetut site/property-valinnat jos niitä löytyy Pulse-configista
- Tokenit tallennetaan aina Numeriksen `CredentialVault`iin, ei lähdekoodiin eikä SQLite-configiin uutena plain text -arvona.
- Migraatio saa lukea Pulsen vanhan plain text fallback-tokenin, koska Tauri käytti sitä jo. Numeris käyttää sitä vain ensimmäisenä siirtolähteenä ja siirtää arvon omaan vaultiin.
- Migraatio on idempotentti: käynnistyksen toistaminen ei duplikoi rivejä eikä tyhjennä toimivia mappingeja.

### 3. Lisää integraatioiden diagnostiset testipolut

- Lisää Sources-sivulle jokaiselle integraatiolle erillinen `Test`-polku ennen `Sync`-polkua:
  - Cloudflare zone analytics: validoi zone endpoint ja pieni GraphQL-kysely.
  - Web Analytics: validoi token pienellä GraphQL account queryllä ja käyttää tallennettua site_tagia, ei RUM discoveryä.
  - Search Console: validoi refresh tokenilla access token ja listaa ensimmäinen property.
- Virheviestit erotellaan:
  - token puuttuu
  - token tallennettu mutta Cloudflare/Google palauttaa 401
  - account/zone mismatch
  - site_tag puuttuu
  - API palauttaa tyhjän datasetin
- Virheviesteissä ei näytetä tokenia, mutta näytetään account id/domain/status code ja API:n virheteksti.

### 4. Tee synkronoinneista luotettavia ja yhdenmukaisia

- Web Analytics sync käyttää aina tallennettuja `web_analytics_sites`-rivejä. `Discover sites` ei saa olla pakollinen.
- Cloudflare, Web Analytics ja Search Console syncit päivittävät `connections.last_sync` ja `connections.status` vain onnistuneen synkin jälkeen.
- Päivitä Web Analyticsin nykyinen virhe `"No sites discovered yet — press Discover sites first"` muotoon, joka ohjaa migroituun/manuaaliseen site tagiin.
- Tarkista kaikki Dapper-parametrit, joissa SQL käyttää snake_case/lowercase-parametreja, ja varmista eksplisiittiset nimet.
- Tarkista päivämäärärajaukset Pulsea vasten: 7d/30d/90d/all eivät saa hakea eri rangea Numeriksessa kuin Taurissa.

### 5. Lisää asetusten pysyvyys ilman UI-polishia

- Toteuta kevyt `SettingsStore`:
  - packaged: `ApplicationData.Current.LocalFolder` tai sama `AppPaths.DataDir`
  - unpackaged: `%LocalAppData%\Numeris\settings.json`
- Tallenna vähintään:
  - selected period
  - selected domain
  - viimeksi avattu sivu
- Älä rakenna vielä hienoa asetussivua. Riittää, että ShellViewModel lataa ja tallentaa arvot.

### 6. Korjaa WinUI/MSIX-tekninen velka ennen ulkonäköä

- Korjaa tai aikatauluta MVVM Toolkit `MVVMTK0045` -varoitukset muuttamalla WinUI ViewModelien `[ObservableProperty] private field` -malli partial property -malliin.
- Tämä on tärkeää ennen packaged/MSIX-jakelua, koska Microsoftin nykyinen ohje sanoo kenttämuodon olevan WinRT/AOT-yhteensopimaton.
- Tee muutos vaiheittain ViewModel kerrallaan, ei samalla kertaa integraatiokorjausten kanssa.

### 7. Core smoke test ja commit-järjestys

- Commit 1: Web Analytics token/site migration + sync flow fix.
- Commit 2: Cloudflare ja Search Console Pulse-migraatiot.
- Commit 3: integraatioiden Test-polut ja tarkemmat virheviestit.
- Commit 4: SettingsStore ja shell-valintojen pysyvyys.
- Commit 5: MVVMTK0045-korjaukset tai erillinen teknisen velan commit.
- Commit 6: smoke test -korjaukset packaged/unpackaged-ajolle.

### 8. Lisää uudet live-lähteet Pulsen ulkopuolelta

- Lisää Web Performance -lähde, joka yhdistää CrUX Report API:n ja PageSpeed Insights API:n:
  - yksi CrUX API key `CredentialVault`iin
  - yksi PageSpeed API key `CredentialVault`iin; käyttäjä voi käyttää samaa Google Cloud -avainta, jos molemmat API:t on sallittu
  - `performance_urls`-lista, jossa oletuksena `https://finnvek.com/` ja `https://knittoolsapp.com/`
  - käyttäjä voi lisätä lisä-URL:eja manuaalisesti myöhemmin
  - CrUX haetaan `records:queryHistoryRecord`-metodilla origin- ja URL-tasolla
  - PageSpeed ajetaan MOBILE- ja DESKTOP-strategioilla
- Lisää Microsoft Bing Webmaster Tools -lähde:
  - API key `CredentialVault`iin, ei OAuthia
  - verified site URL:t muodossa `https://finnvek.com/` ja `https://knittoolsapp.com/`
  - synkki kutsuu read-only Webmaster API -metodeja; mutatoivia submit/block/remove/save-toimintoja ei tehdä automaattisesti
- Tallenna uudet API-vastaukset sekä normalisoituun muotoon että raw JSONina, jotta "kaikki mahdollinen" data säilyy vaikka dashboard-mallia tarkennetaan myöhemmin.

## Public Interfaces / Types

- `PulseDataMigrationService` laajennetaan yleiseksi Pulse-importtipalveluksi tai jaetaan pieniin importtereihin per integraatio.
- `WebAnalyticsConnectionConfig` saa pitää `ImportSource`-tyyppisen metatiedon, mutta token ei kuulu configiin uutena Numeris-datana.
- Lisää `SettingsStore` palveluna DI-containeriin.
- Lisää Test-tuloksille yhteinen kevyt result-malli, esimerkiksi `ConnectionTestResult { bool Ok; string Message; string? StatusCode; }`, jotta SourcesViewModel ei rakenna kaikkia statusviestejä ad hoc.
- Ei lisätä Play Store live -integraatiota tässä passissa. Play Store -taulut ja mock-data jäävät ennalleen.

## Test Plan

- Build: `dotnet build C:\Dev\Numeris\Numeris.slnx` palauttaa `0 Error(s)`.
- Migration:
  - käynnistä Numeris ilman manuaalista tokenin syöttöä
  - Web Analytics account id ja `knittoolsapp.com` site tag näkyvät Sourcesissa
  - Sync käyttää Pulsen migroitua tokenia
- Cloudflare:
  - Test Cloudflare onnistuu olemassa olevalla domain/zone/token-yhdistelmällä
  - Sync kirjoittaa `cloudflare_traffic` ja `cloudflare_status_codes` rivejä
- Web Analytics:
  - älä paina Discover sites
  - paina Sync
  - `web_analytics_daily`, `web_analytics_referrers`, `web_analytics_pages`, `web_analytics_countries` päivittyvät
- Search Console:
  - Test/Connect käyttää tallennettua refresh tokenia jos se löytyy
  - Sync kirjoittaa `search_console` ja related-taulut
- Persistence:
  - vaihda period/domain/sivu
  - sulje ja avaa appi
  - valinnat palautuvat
- Packaged/unpackaged:
  - testaa sekä `Numeris (Package)` että `Numeris (Unpackaged)` Visual Studiossa
  - varmista että molemmat käyttävät oikeaa datahakemistoa ja eivät hukkaa credentialeja

## Assumptions

- Ulkonäkö, brändiassetit, animaatiot, spacing ja viimeistelty layout tehdään vasta core-toiminnallisuuden jälkeen.
- Salaisuuksia ei kovakoodata lähdekoodiin eikä commitata. "Valmiiksi koodissa" toteutetaan automaattisena paikallisena migraationa Pulsen olemassa olevista tiedoista.
- Cloudflaren nykyisten virallisten docsien mukaan GraphQL Analytics API tarvitsee `Account -> Account Analytics -> Read`; RUM site discovery on eri asia ja voi vaatia eri oikeuden.
- Jos Pulsen vanhasta credentialista löytyy toimiva token, Numeris käyttää sitä ensisijaisena totuutena tällä koneella.
