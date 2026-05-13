# Suomenkielisen sisällön pariteettisuunnitelma

## Yhteenveto
Tavoite on tehdä julkaisemattomista suomenkielisistä työkalusivuista ja 12 suomenkielisestä artikkelista sisällöllisesti samat kuin englanninkieliset vastineet, mutta luonnollisella suomella. Tämä ei sisällä julkaisua, deployta, URL-rakenteen muutosta tai uusien `/en/`-reittien lisäämistä.

## Toteutusmuutokset
- Määritä pariteettisääntö: suomenkielisen sivun pitää kattaa samat aiheet, laskurit, taulukot, FAQ-kysymykset, listat, sisäiset linkit ja olennaiset SEO-metatiedot kuin englanninkielinen vastine.
- Pidä rakenne käyttäjälle samana: sama pääsisältöjärjestys ja samat aiheblokit, mutta otsikoita saa muotoilla suomeksi luontevammin, jos merkitys säilyy.
- Työkalusivuilla täydennä puuttuvat FAQ:t ja selittävät SEO-osiot englanninkielisten mukaan:
  - `silmukkalaskuri`: lisää puuttuva stitch multiple -FAQ.
  - `lankamuunnin`: sovita englanninkielinen sivu ja suomenkielinen sivu nykyiseen komponenttiin, joka arvioi työn tyypin, koon ja lankavahvuuden mukaan. Älä käännä vanhentunutta skein-input-selitystä sellaisenaan.
  - `puikkokoot`: lisää puuttuvat aiheet conversion pitfalls, yarn weight pairing, unmarked needles ja “do I need every size” -FAQ.
  - `lankavahvuudet`: lisää englanninkielisen sivun WPI-tunnistin myös suomeksi ja säilytä kaikki category/gauge/needle/WPI/use/regional name -sisältö.
  - `neulelyhenteet`: lisää pattern-specific abbreviations, early abbreviations ja asterisk / ssk vs skp -FAQ:t suomeksi.
  - `neulekokotaulukot`: lisää puuttuva FAQ mittaamisesta vaatteen päältä / paljaalta iholta.
- Vältä duplikaatiota: jos WPI-tunnistin tai muu sama interaktiivinen logiikka tarvitaan molemmille kielille, nosta data ja tekstit jaettuun komponenttiin tai pieneen helperiin sen sijaan, että sama scripti kopioidaan kahteen sivuun.
- Jos jaettua komponenttia/dataa lisätään tai vastuita siirretään, päivitä `AGENTS.md` ja `memory/MEMORY.md`, koska kyse on arkkitehtuurisesta muutoksesta.

## Artikkelien yhtenäistäminen
- Käy kaikki 12 käännösparia yksitellen läpi `src/i18n/articles.ts`-järjestyksessä.
- Korvaa nykyiset tiivistetyt suomenkieliset artikkelit täysillä suomennoksilla, jotka säilyttävät englanninkielisen artikkelin:
  - kaikki `##`-aiheet
  - olennaiset kappaleväitteet ja esimerkit
  - listat ja checklistit
  - FAQ-osion kysymykset ja vastaukset
  - sisäiset linkit
- Linkkipolitiikka:
  - Jos linkitetylle englanninkieliselle artikkelille on suomenkielinen vastine, käytä `/fi/artikkelit/.../`.
  - Jos linkki osoittaa suomennettuun työkaluun, käytä `/fi/tyokalut/.../`.
  - Jos linkitettyä artikkelia ei ole vielä suomennettu, jätä linkki englanninkieliseen URLiin vain silloin, kun se on sisällöllisesti hyödyllinen, ja kirjoita ankkuriteksti suomeksi.
- Pidä frontmatter-pariteetti hallittuna:
  - `lang: fi` ja `translationKey` säilyvät.
  - `category` säilyy samana teknisenä kategoriana kuin englannissa.
  - `categoryOrder` asetetaan vastaamaan englanninkielistä vastinetta, ellei suomenkieliselle artikkelilistalle ole tietoisesti eri järjestystä.
  - `publishDate` päätetään yhtenäisesti: joko alkuperäisen englanninkielisen päivämäärä pariteetin vuoksi tai nykyinen suomenkielinen julkaisupäivä lokalisaation vuoksi. Oletus: käytä englanninkielistä päivämäärää, koska käyttäjä pyysi samaa sisältöä.
  - `tags` käännetään suomeksi, mutta niiden määrä ja hakutarkoitus pidetään englanninkielistä vastaavana.

## Laadunvarmistus
- Tee automaattinen tarkistus ennen viimeistelyä:
  - 6/6 suomenkielistä työkalusivua olemassa.
  - 12/12 suomenkielistä artikkelia olemassa.
  - Jokaisessa suomiartikkelissa `lang: fi` ja oikea `translationKey`.
  - H2-määrä, FAQ-määrä, listamäärä ja linkkimäärä vastaavat englanninkielistä vastinetta tai poikkeama on tietoinen ja kirjattu.
  - Ei `TODO`, `FIXME`, `href="#"`, `/articles/fi` tai julkisia `/en/`-reittejä.
- Aja `npm run build` ja varmista, että Astro build onnistuu eikä generoi vahingossa `/articles/fi/...`-reittejä.
- Älä aja `lint-check` tai `security-check`, koska projektiohjeen mukaan käyttäjä ajaa ne itse.
- Älä deployaa suomenkielistä sisältöä tuotantoon ilman käyttäjän erillistä hyväksyntää.

## Hyväksymiskriteerit
- Suomenkielinen lukija saa samat tiedot, samat varoitukset, samat laskentaperiaatteet ja samat jatkolinkit kuin englanninkielinen lukija.
- Teksti ei ole sanasanainen konekäännös: termit ovat projektin termipolitiikan mukaisia, esimerkiksi `gauge` → `neuletiheys`, `gauge swatch` → `mallitilkku`, `cast on` → `luoda silmukat`.
- Suomenkieliset sivut ovat edelleen julkaisemattomia tuotantoon asti.
