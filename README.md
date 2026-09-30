# Milo 🐕

Een pixel-art Milo die rondloopt op je scherm. Hij wandelt over je taakbalk en springt op de bovenkant van je vensters. Hij reageert op je muis, op welke app je gebruikt, op muziek en (met Ollama) ook op wat er op je scherm staat.

## Starten

- **Snel testen:** dubbelklik `start.bat`
- **Echte exe maken:** dubbelklik `build.bat`, daarna staat hij in `dist\Milo.exe`

Je hebt de .NET 10 SDK nodig, en die heb je al.

Milo zit ook in je systeemvak (tray). Rechtsklik op hem of op het icoontje voor het menu.

## Wat je met hem kan doen

| Actie | Wat Milo doet |
|---|---|
| Muis heen en weer over hem bewegen | aaien → hartjes, en als hij ligt rolt hij op zijn rug |
| Klikken | boop, hij kijkt naar je (slaapt hij, dan is hij even chagrijnig 💨) |
| 4x snel klikken | zoomies (nu echt snel, met slippen, sprongetjes en daarna hijgen) |
| Slepen en loslaten | oppakken en gooien |
| Snel met de muis bewegen als hij zin heeft | hij jaagt op je cursor |
| Menu → Spelen → Balletje | hij haalt het en brengt het terug. Je kan de bal zelf ook slepen en gooien |
| Menu → Spelen → Laserpuntje | een rood puntje volgt je muis en hij wordt gek (gaat na 2 min vanzelf uit) |
| Menu → Spelen → Snoepje | hij rent erheen en eet het op |
| Klikken op de voerbak / Menu → Voerbak vullen | hij gaat eten als hij honger heeft |
| Menu → Spelen → Vlinder loslaten | hij kijkt ernaar, soms landt ze op zijn neus en hij niest |
| Klikken op een vogel of vlinder | ze vliegt weg |
| Klikken en vasthouden aan zijn mond als hij iets vast heeft | touwtrekken! Laat je los, dan wint hij. Hou je lang genoeg vast, dan win jij en kan je het gooien |
| Menu → Trucjes | zit, af, spreek, pootje, opzitten, draai, buiging, rol om, pang! |
| Menu → 📸 Foto | hij poseert, de foto komt in `Afbeeldingen\Milo` |
| Menu → Milo roepen | hij komt naar je muis |
| Muziek aanzetten | hij danst mee op de beat (en huilt soms mee 🎵) |
| Een tijdje niks doen | hij gaat in zijn mandje slapen en droomt, en als je terugkomt begroet hij je |

### Trucjes leren

Elk trucje heeft sterretjes (★☆☆☆☆). Nieuwe trucjes lukken bijna nooit: dan doet hij een verkeerd trucje, kantelt hij zijn kop of is hij afgeleid. **Klik op hem, aai hem of geef een snoepje binnen 6 seconden na een gelukt trucje** en hij wordt er beter in. Oefenen helpt ook een beetje.

### Wat hij zelf doet

Rondsnuffelen, op vensters springen, gapen, krabben, liggen, slapen, achter zijn staart aan draaien, huilen, krabben aan de rand van het scherm. Als jullie goeie vrienden zijn brengt hij soms een cadeautje (sok, stokje, botje) en hij ruimt zijn speelgoed zelf naar zijn mandje. Honger krijgt hij vanzelf, dan gaat hij naar zijn voerbak (en bedelt als die leeg is). Vogels jaagt hij weg met blaffen.

Zit je lang achter elkaar aan je pc (standaard 75 min), dan komt hij met zijn **leiband** aanzetten. Klik op hem = wandeling gaan (pauze nemen 😉).

In december draagt hij een kerstmuts en op nieuwjaar en jullie "verjaardag" een feesthoedje. Andere hoedjes (ook een zonnebril) kies je in het menu.

### Interactie met je scherm

- **Venster slepen waar hij op staat** → hij surft mee 🏄. De eerste keer is hij bang, daarna vindt hij het leuk. **Schud** je het venster heen en weer, dan valt hij eraf en is hij even duizelig 💫
- **Venster sluiten/minimaliseren onder zijn pootjes** → hij valt, kijkt verbaasd naar waar het was en snuffelt. Na een paar keer is hij gewoon gepikeerd 😤
- **Muis net onder hem bewegen** als hij op een venster staat → hij gaat plat liggen, loert over de rand en mept met zijn pootje naar je cursor
- Bij een hoge rand kijkt hij eerst even naar beneden voor hij springt
- **Meldingen** (die popups rechtsonder) → hij rent erheen en kijkt ernaar. Knippert een app in je taakbalk (nieuw bericht) en zit je niet achter je pc, dan komt hij het zeggen ✉️
- **Bureaublad leeg** (Win+D) → vrij veld, zoomies!
- **Lang typen** → hij komt bij je venster zitten om je gezelschap te houden
- **Heel veel backspace** → hij kijkt je vragend aan, en als het blijft duren komt hij je troosten
- **Wild klikken** → hij schrikt
- **Screenshot** (PrtScn of Win+Shift+S) → hij poseert 📸

### Spelen met wat er ín je apps staat

Milo kijkt (zonder AI, gewoon naar de pixels, ongeveer 1x per seconde) wat er in het venster staat waar je mee bezig bent: **plaatjes**, **vakjes/knoppen** en **blokken tekst**. De bovenkant daarvan wordt een richeltje waar hij op kan springen:

- Op een **plaatje** gaat hij liggen, loert hij over de rand en mept hij ernaar, of hij gaat er gewoon op zitten
- Over een **blok tekst** loopt hij langzaam met zijn neus omlaag, alsof hij het leest 💬
- Op een **knop of vakje** tikt hij een paar keer met zijn pootje (hij klikt nooit echt iets aan)
- Laat je je muis een tijdje op een plaatje rusten, dan komt hij kijken waar jij naar kijkt
- **Scroll** je terwijl hij ergens op staat, dan valt hij eraf 😅

Uitzetten kan in menu → Opties → Speelt met wat er in je apps staat.

### Kruipen over de rand van je scherm

Af en toe (of via menu → Spelen → Tegen het plafond kruipen) doet Milo een Terraria-crawler na: hij loopt naar de rand van je scherm, klimt de muur op, kruipt ondersteboven langs de bovenkant en laat zich dan vallen, soms precies op je cursor. Soms klimt hij ook gewoon weer naar beneden. Klik je op hem terwijl hij hangt, dan laat hij los.

### Een beetje ondeugend

Een lichte versie van Desktop Goose:

- Ligt je muis een tijdje stil (je leest bv. iets), dan besluipt hij hem en **mept hij hem een stukje opzij**
- Heel af en toe **trekt hij een klein venster een stukje opzij** met zijn tanden

Hij doet dat nooit met gemaximaliseerde of fullscreen vensters, nooit van je scherm af, niet als je aan het typen bent en niet als je een muisknop ingedrukt houdt. Pak je je muis zelf, dan stopt hij meteen. Uitzetten kan in menu → Opties → Ondeugend.

### Bovenaanzicht

Menu → Opties → **Bovenaanzicht**: dan kijk je van bovenaf op Milo en is je hele scherm zijn vloer. Hij loopt overal rond (over je vensters heen), snuffelt, gaat zitten of liggen, rolt zich op om te slapen, komt naar je muis en jaagt erachteraan als je snel beweegt. Waar hij loopt zie je even **pootafdrukjes** die na een seconde weer verdwijnen. Aaien, klikken, oppakken en gooien (dan glijdt hij over je scherm) werken ook, net als het laserpuntje. Balletje, snoepje, trucjes, vlinders en zijn mandje horen bij de zijaanzicht-modus en staan dan even uit.

### Mandje en voerbak verplaatsen

Sleep het mandje of de voerbak naar een andere plek: ze klikken vast op je taakbalk, ook op je andere scherm, en Milo onthoudt de plek. Gewoon klikken op de voerbak vult hem nog altijd.

### Gamen op één scherm

Heb je twee schermen en zit je fullscreen te gamen, dan blijft Milo van dat scherm af en houdt hij je gezelschap op het andere scherm. Heb je maar één scherm, dan verstopt hij zich zoals vroeger.

Zolang een game open staat (ook als je even alt-tabt naar iets anders) staat de scherm-visie op pauze en is het model uit je grafische kaart gehaald. Pas als je de game afsluit gaat hij weer kijken.

### Hij hoort wat je typt

Net als een echte hond reageert hij op bepaalde woorden, in elke app:

| Typ | Milo |
|---|---|
| `milo` | komt naar je toe of kijkt je vragend aan |
| `braaf`, `good boy` | trots + hartjes (telt ook als beloning na een trucje) |
| `wandelen`, `wandeling`, `walkies` | gaat helemaal los en haalt zijn leiband |
| `snoepje`, `koekje`, `treat` | komt bedelen |
| `balletje` | wil spelen |
| `dierenarts`, `badje` | 😰 verstopt zich |
| `eekhoorn`, `squirrel` | EEKHOORN! rent naar de rand en blaft |
| `milo zit`, `milo pootje`, `milo draai`, ... | doet het trucje |

Gewone woorden zoals `eten`, `kat`, `bal` of `slapen` hoort hij ook, maar dan spitst hij enkel zijn oren, tenzij je `milo` erbij typt. Hij onthoudt niks van wat je typt en er wordt niks opgeslagen, hij checkt alleen of er een van zijn woordjes tussen zit. Uitzetten kan in menu → Opties → Luistert naar wat je typt.

### Met scherm-visie erbij

Bovenop de reacties van vroeger kan hij nu ook **springen** naar dingen die hij ziet (ballen, katten, dieren), of op het venster erboven gaan liggen en er **over de rand naar loeren en naar meppen**. Als je iets wint zet hij zijn **zonnebril** op 😎. Kijkt hij met je naar een video, dan volgt hij wat beweegt en springt hij er soms naar.

### Dagboek en vriendschap

Menu → Milo's dagboek toont hoe lang jullie samen zijn, jullie vriendschapslevel, wat hij vandaag allemaal gedaan en gezien heeft en zijn beste trucje. Alles wordt bewaard in `%AppData%\MiloBot\save.json`, dus hij vergeet niks als je hem afsluit.

## Scherm-visie (optioneel, gratis, lokaal)

Draait op je eigen GPU via Ollama, dus niks gaat het internet op.

```
ollama pull qwen3-vl:8b-instruct
```

Milo vindt het model vanzelf (ongeveer 6 GB). Is dat te traag of zit je te gamen, dan kan `qwen3-vl:4b-instruct` ook: kies het in het menu (Scherm-visie → Licht).

Hij kijkt als je naar een andere app gaat, als er veel verandert op je scherm, en af en toe tussendoor (menu → Scherm-visie → Kijkt: Vaak/Normaal/Zelden). Nooit als hij slaapt of als je fullscreen zit. Hij beschrijft niks, hij reageert gewoon op honden, katten, andere dieren, eten, ballen, gezichten, errors, winnen, droevige of enge dingen en schattige dingen.

**Hij raakt gewend, maar wordt niet saai:** de eerste keer dat hij een kat ziet blaft hij, daarna gromt hij, dan staart hij, en uiteindelijk geeft hij je een zijblik of zucht hij. Iets wat hij een week niet gezien heeft wordt weer spannend, en soms "herontdekt" hij iets gewoon opnieuw.

In het menu kan je het model kiezen (8B slim, 4B licht) en zie je wat hij het laatst gezien heeft. Alles staat ook in de log (menu → Log openen).

## Eigen geluiden

Zet je eigen `.wav` bestanden in `assets\sounds\` met dezelfde naam, bv. `bark.wav`, `yip.wav`, `whine.wav`, `sniff.wav`, `yawn.wav`, `sneeze.wav`. Wil je meerdere versies van hetzelfde geluid, noem ze dan `bark_1.wav`, `bark_2.wav`, ... en Milo kiest er willekeurig één.

## Uiterlijk aanpassen

De sprites worden gemaakt met `tools\make_sprites.py` (Python + Pillow + numpy):

```
cd tools
python make_sprites.py
```

Je kan `assets\milo.png` ook gewoon zelf bijwerken in Aseprite. Elk frame is 48x48 en de pootjes staan op lijn 45. De kleuren zijn gebaseerd op de echte Milo: roodbruine vacht, oranje pootjes met lichte voetjes en een donkere "zadel" over zijn rug en staart (zie `PAL` en `sable()` in het script).

## Testen (voor ontwikkelaars)

Zet `"Debug": true` in `settings.json` en start Milo opnieuw. Schrijf daarna een commando in `%AppData%\MiloBot\command.txt` en Milo voert het binnen een seconde uit: `crawl`, `nudge`, `bat`, `content`, `top` (bovenaanzicht aan/uit), `come` of `state` (schrijft zijn toestand in de log).

## Hoe zwaar is het?

Milo zelf gebruikt heel weinig CPU en RAM (kijk in de log naar de `perf` regels, die komen elke minuut). Scherm-visie gebruikt je GPU een paar seconden telkens als hij kijkt; het 8B model neemt ~6 GB VRAM in zolang het geladen is (Ollama laat het na 30 min niks doen weer los). Ga je gamen, kies dan 4B of zet visie uit.

## Settings

Die staan in `%AppData%\MiloBot\settings.json`. Daar kan je o.a. het visie-model, de Ollama-url en hoe vaak hij kijkt (`VisionMinSeconds`) aanpassen.
