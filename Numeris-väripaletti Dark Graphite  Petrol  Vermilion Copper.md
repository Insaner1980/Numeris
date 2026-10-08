# Numeris-väripaletti: Dark Graphite / Petrol / Vermilion Copper

## Yhteenveto
Toteuta Numerikseen rohkea mutta analytiikkasovellukseen sopiva tumma paletti: **obsidian/graphite pohja**, **deep petrol valinta- ja syvyyssävy**, **vermilion copper pääaksentti** ja **warm white/ash tekstit**. Muutos tehdään keskitetysti teematokeneissa, ei sivukohtaisina värihackeina.

Lähteet suunnitteluperiaatteille: Microsoftin [XAML theme resources](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources) ja [NavigationView customization](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview).

## Tärkeimmät muutokset
- Päivitä [Tokens.xaml](C:/Dev/Numeris/Numeris/Themes/Tokens.xaml) niin, että vanha sininen aksentti poistuu:
  - `NumerisAccentColor #F5662F`, hover `#FF7A42`, pressed `#D94E1F`.
  - Lisää `NumerisAccentForegroundColor #070808` ja brush, koska tumma teksti oranssilla on kontrastiltaan parempi kuin valkoinen.
  - Tekstit: primary `#F4F0EA`, secondary `#D6D0C8`, tertiary `#AFA9A2`, disabled `#79736C`.
  - Pinnat: `AppBackgroundColor #070808`, `AppBackdropScrimColor #59000608`, `CardSurfaceColor #A00D1013`, `ControlSurfaceColor #9913181B`, `ChartPanelColor #A0080C0F`, `NumerisCardBorderColor #32F4F0EA`.
- Pidä `NavigationLayerColor` ja `ContentLayerColor` läpinäkyvinä. Älä palauta nav/content-alueille erillisiä taustavärikerroksia.
- Älä muuta `AppBackdrop.webp`-assetia tässä vaiheessa. Ilme muutetaan tokeneilla, jotta nykyisiä tausta- ja asset-muutoksia ei sotketa.
- Lisää NavigationViewin valintaresurssit `Tokens.xaml`iin:
  - selected/hover background deep petrol -sävyllä (`#33143C42`, hover `#22143C42`).
  - `NavigationViewSelectionIndicatorForeground` käyttää `NumerisAccentBrush`.
- Pidä rutiinipainikkeet graphite/matte-tyylisinä. Lisää erillinen `AccentActionButtonStyle`, jonka tausta on vermilion copper ja foreground `NumerisAccentForegroundBrush`.
  - Käytä accent-tyyliä vain Sources-sivun varsinaisissa sitouttavissa toimissa: `Connect...`, `Save`, `Add`.
  - Jätä `Test`, `Delete`, report-refresh ja Health-sivun rutiinitoiminnot nykyisiin secondary/danger/neutral-tyyleihin.
- Päivitä chart-tokenit:
  - neutral bar: top `#FF6E7C80`, mid `#FF2A3336`, bottom `#FF0B1012`.
  - highlight bar: top `#FFFF8A54`, mid `#FFF5662F`, bottom `#FF9E3518`.
  - `ChartSecondaryColor #D89145`, `ChartMutedColor #B0B8B0`, grid/reference warm white -läpinäkyvyyksillä.
  - `SuccessColor #67D391`, `WarningColor #D89145`, `DangerColor #FF6B76`, `InfoColor #7FB6C0`.

## Julkiset rajapinnat ja vastuut
- Ei tietokanta-, API-, sync- tai ViewModel-dataflow-muutoksia.
- `ChartPalette` ja `ChartTheme` jäävät chart-värien lähteeksi; ViewModelit eivät saa alkaa luoda omia hex-värejä tai gradientteja.
- Uusi XAML-rajapinta on vain tyylitasolla: `AccentActionButtonStyle` ja sitä tukevat color/brush-resurssit.
- Päivitä [AGENTS.md](C:/Dev/Numeris/AGENTS.md) nykyisen visual-system-kohdan väribaseline vastaamaan uutta palettia.

## Testisuunnitelma
- Päivitä [Program.cs](C:/Dev/Numeris/Numeris.Tests/Program.cs) invariantit:
  - vanha `#4F73FF` ei enää ole sallittu aksenttina.
  - uudet surface-, text-, accent-, nav- ja chart-tokenit löytyvät.
  - `AccentActionButtonStyle` löytyy ja käyttää tummaa accent-foregroundia.
  - ViewModelien `SKColor.Parse` ja sivukohtaiset chart-värit pysyvät kiellettyinä.
- Aja `dotnet run --project C:\Dev\Numeris\Numeris.Tests\Numeris.Tests.csproj`; odotus: exit code 0.
- Aja `dotnet build C:\Dev\Numeris\Numeris.slnx`; odotus: build onnistuu.
- Visuaalinen tarkistus sovelluksessa: Overview, Sources, Cloudflare ja Performance.
  - Ei nav/content-värirajaa.
  - Oranssi näkyy valittuna aksenttina, ei täytä koko sovellusta.
  - Kortit pysyvät luettavina taustaa vasten.
  - Chart-korostukset erottuvat, mutta statusvärit eivät sekoitu brändiaksenttiin.

## Oletukset
- Tämä on ensimmäinen väripassi, ei uusi taustakuva- tai layout-uudistus.
- Nykyinen likainen worktree säilytetään: toteutus muuttaa vain väriteemaan, nappityyleihin, niihin liittyviin XAML-käyttöihin, testiodotuksiin ja AGENTS-dokumenttiin liittyvät kohdat.
- `lc` ja `sc` jätetään ajamatta, koska käyttäjän ohjeen mukaan käyttäjä ajaa ne itse.
