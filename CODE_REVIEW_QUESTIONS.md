# Koodikatselmuskysymykset seuraavaan chattiin

Kysy nämä seuraavassa chatissä laajaa koodikatselmusta varten. Tarkoitus on löytää arkkitehtuuri-, data-, tietoturva- ja luotettavuusriskit ennen UI-viimeistelyä.

## Kokonaisarkkitehtuuri

1. Tarkista Numeriksen nykyinen integraatioarkkitehtuuri end-to-end. Onko `ApiClient -> SyncService -> Repository -> ViewModel` -vastuunjako johdonmukainen kaikissa lähteissä?
2. Onko `SourcesViewModel` kasvanut liian suureksi, ja pitäisikö se pilkkoa lähdekohtaisiin ViewModeleihin tai palveluorkestraattoreihin?
3. Onko `connections`-taulun käyttö skaalautuva, kun integraatioita on Cloudflare, Web Analytics, Search Console, CrUX, PageSpeed ja Bing?
4. Onko domain/site/origin/URL-käsitteille yksi riittävän selkeä totuus, vai onko riski että `finnvek.com`, `https://finnvek.com/` ja `https://finnvek.com` sekoittuvat?

## API-integraatiot

5. Tarkista CrUX-, PageSpeed- ja Bing-clientit virallisia API-sopimuksia vasten. Onko endpointit, parametrit, response-parsinta ja virheenkäsittely oikein?
6. Onko Bing Webmaster API:n read-only-metodien valikoima järkevä, vai kutsutaanko turhaan endpointteja jotka ovat hitaita, vanhentuneita tai väärillä parametreilla?
7. Pitäisikö PageSpeed-synciin lisätä throttling, retry/backoff ja rinnakkaisuuden rajoitus ennen kuin URL-lista kasvaa?
8. Onko CrUX URL-tason 404/puuttuva data käsitelty oikein ilman että koko sync epäonnistuu?
9. Vuotaako API-avaimia tai tokeneita lokiin, virheviesteihin, SQLiteen tai raw JSON -tallennukseen?

## Data ja SQLite

10. Tarkista uudet taulut `crux_metric_points`, `pagespeed_runs`, `pagespeed_audits`, `bing_*` ja `performance_urls`. Ovatko primary keyt, indeksit ja konfliktipäivitykset oikein?
11. Onko raw JSONin tallennus riittävästi rajattu, vai voiko SQLite kasvaa liian suureksi PageSpeed/Bing-synkeissä?
12. Pitäisikö syncien käyttää transaktioita laajemmin, jotta puolittain epäonnistunut synkki ei jätä dataa epäjohdonmukaiseksi?
13. Onko olemassa migraatiostrategia schema-version nostolle, jos tauluja pitää muuttaa myöhemmin?

## Luotettavuus ja UX

14. Ovatko Sources-sivun Test/Save/Sync-tilat käyttäjälle selkeät, kun jokin integraatio toimii osittain mutta toinen epäonnistuu?
15. Näyttääkö appi riittävästi `last_sync`, rivimäärät ja viimeisimmän virheen, jotta käyttäjä tietää mitä tapahtui?
16. Voiko pitkä sync jäädyttää UI:n tai aiheuttaa päällekkäisiä synkkejä, jos käyttäjä painaa painikkeita monta kertaa?
17. Pitäisikö syncille lisätä per-integraatio cancellation tai timeout?

## Turvallisuus

18. Tarkista `CredentialVault`-käyttö. Poistetaanko, korvataanko ja luetaanko salaisuudet oikein packaged- ja unpackaged-ajossa?
19. Onko vanhan Pulse-datan migraatio turvallinen, idempotentti ja riittävän varovainen plain text -fallbackien kanssa?
20. Onko Bing/Google/Cloudflare-virheiden raw-vastauksissa salaisuuksia, joita ei pitäisi tallentaa?

## Testit ja ylläpidettävyys

21. Mitkä integraatioiden osat kannattaa nostaa yksikkötestattaviksi ilman live API -avaimia?
22. Riittääkö nykyinen `Numeris.Tests`-konsolitestiharness, vai pitäisikö lisätä varsinainen test framework?
23. Onko MVVM Toolkit partial property -muutos tehty oikein kaikissa ViewModeleissa, ja onko sidonta/XAML edelleen ehjä?
24. Mitä smoke-testejä pitäisi lisätä ennen packaged/MSIX-jakelua?

## Dashboardien seuraava vaihe

25. Mikä olisi paras tapa tuoda CrUX/PageSpeed/Bing-data näkyviin ilman että dashboardista tulee liian raskas?
26. Pitäisikö Bing ja Google Search Console yhdistää samaan hakunäkyvyysnäkymään provider-kentällä, vai pitää ne erillään?
27. Mitkä metriikat kannattaa näyttää ensimmäisellä ruudulla: Core Web Vitals, PageSpeed scoret, Bing clicks/impressions, crawl issues vai top query/page -listat?
