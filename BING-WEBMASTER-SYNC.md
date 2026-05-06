# Bing Webmaster sync - muisti

Numeriksen Bing Webmaster -sync pidetaan tarkoituksella kapeana, kunnes UI tarvitsee tarkempia detail-nakyvia.

Nykyinen luotettava read-only-ydin:

- `GetUserSites`: yhteyden testaus ja kayttajan vahvistettujen sivustojen yleislista.
- `GetRankAndTrafficStats`: paivittainen sivustotason clicks/impressions-aikasarja.
- `GetQueryStats`: viikoittain paivittyvat top-queryt.
- `GetPageStats`: viikoittain paivittyvat top-sivut.
- `GetCrawlStats` ja `GetCrawlIssues`: kevyt teknisen SEO-terveyden raw-data.

Detail-metodeja ei ajeta automaattisessa syncissa ennen kuin niille on selkea kayttokohde ja request-sopimus on toteutettu erikseen. Erityisesti `GetChildrenUrlInfo` vaatii POST-bodyn ja `FilterProperties`-objektin, ja `GetUrlLinks` kayttaa `link` + `page` -parametreja eika vanhaa `url` + `count` -muotoa.

Jos detail-dataa tarvitaan myohemmin, lisaa se yksi metodi kerrallaan:

1. Tarkista Microsoft Learn -sopimus metodille.
2. Toteuta oikea GET/POST-polku ja parametrinimet.
3. Tallenna normalisoidut rivit, jos UI kayttaa dataa.
4. Pida raw JSON vain rajattuna debug-/auditointitukena.
5. Sanitioi kaikki virheet ennen UI:ta tai SQLitea.
