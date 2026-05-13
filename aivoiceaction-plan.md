# AiVoiceAction-siirron suunnitelma

## Yhteenveto

Siirretään `AiVoiceAction` pois UI-paketista AI-paketin omaksi sopimustyypiksi. Tavoite on katkaista vääränsuuntainen riippuvuus `ai -> ui` ilman käyttäytymismuutoksia.

Uusi sijainti: `app/src/main/java/com/finnvek/knittools/ai/AiVoiceAction.kt`.

## Keskeiset muutokset

- Muuta `AiVoiceAction.kt` paketti:
  `com.finnvek.knittools.ui.screens.counter` -> `com.finnvek.knittools.ai`.
- Päivitä importit ainakin näissä:
  `VoiceCommandInterpreter.kt`, `CounterViewModel.kt`, `VoiceCommandInterpreterTest.kt`.
- Tarkista haulla kaikki jäljelle jäävät viittaukset:
  `rg "ui\\.screens\\.counter\\.AiVoiceAction|AiVoiceAction" app/src/main app/src/test`.
- Älä muuta sealed classin sisältöä, nimiä, objekteja, dataluokkia tai komentologiikkaa.

## Testisuunnitelma

- Aja kohdennettu testi:
  `./gradlew test --tests "*VoiceCommandInterpreterTest"`.
- Jos importtimuutos koskee CounterViewModelin testejä, aja lisäksi:
  `./gradlew test --tests "*CounterViewModel*"`.
- Lopuksi aja nopea grep-varmistus:
  `rg "com\\.finnvek\\.knittools\\.ui\\.screens\\.counter\\.AiVoiceAction" app/src/main app/src/test`
  Odotettu tulos: ei osumia.

## Dokumentointi

- Koska tämä muuttaa vastuun sijaintia, päivitä `AGENTS.md` ja `memory/MEMORY.md` lyhyesti:
  `AiVoiceAction` on AI-komentotulkinnan sopimus `ai`-paketissa, ei counter-UI:n omistama tyyppi.

## Oletukset

- `AiVoiceAction` kuuluu `ai`-pakettiin, ei `domain/model`-pakettiin, koska se kuvaa AI-äänikomentojen tulkinnan tulosta.
- Käyttäytymistä ei muuteta.
- Ei commitointia.
