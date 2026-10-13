# DMR Radio

WPF klient pro Homebrew/MMDVM přes UDP. Přihlášení a uložení nastavení včetně DPAPI zůstávají beze změny.

## RX

Aplikace přijímá pakety DMRD, vybírá pouze skupinový hovor pro zvolenou TG a timeslot a z 33bajtového burstu skládá tři 72bitové AMBE+2 kanálově kódované rámce. Rámce nejsou v burstu uloženy jako tři sousední devítibajtové bloky: jejich části A/B/C jsou prokládané a druhý rámec je rozdělen kolem 48bitového sync/embedded pole. Dekódování musí zpracovat i kanálové kódování, které z těchto 72 bitů obnovuje vlastní 49bitová hlasová data.

`IVocoder` je rozhraní pro dekodér a kodér. Výchozí `NullVocoder` vrací ticho pro RX a nulové rámce pro TX; **není to skutečný AMBE+2 vokodér**. Proto aplikace v současné podobě rozpozná a zpracuje příchozí pakety, ale z reproduktoru neuslyšíš hlas ani při správně zvoleném výstupu. Po připojení reálného dekodéru se zvuk přehrává přes NAudio v 8 kHz mono s jitter bufferem přibližně 60 ms. Posuvník hlasitosti ovládá výstup a VU metr počítá RMS z dekódovaného PCM. Hovor končí terminátorem nebo po 800 ms bez paketů; při změně TG/timeslotu či odpojení se přehrávání a buffer uvolní.

Výstupní reproduktor/sluchátka lze vybrat v seznamu **VÝSTUP ZVUKU** pod posuvníkem hlasitosti. Volba „Výchozí zařízení Windows“ použije aktuální výchozí zařízení. Výběr se uloží do nastavení; změna během hovoru restartuje lokální přehrávač.

### Skutečný AMBE+2 vokodér

Vlastní implementace AMBE+2 není součástí aplikace. Vhodným bodem pro napojení je `IVocoder` (`DMR-RADIO/Audio/Audio_IVocoder_Version2.cs`); adaptér musí přijímat/vracet 72bitové kanálově kódované DMR rámce. Možnosti k prověření:

- `mbelib` jako externí knihovna/wrapper; před distribucí je nutné ověřit licenci konkrétní verze a případné patentové/licenční povinnosti.
- DVSI AMBEserver nebo kompatibilní licencovaný vokodér dostupný přes UDP.
- VoxDMR uvádí emulaci AMBE+2 z firmwaru MD-380. Jeho veřejná dokumentace říká, že firmware se stahuje až za běhu a není součástí aplikace z právních důvodů. Toto repo firmware neobsahuje ani nestahuje; VoxDMR navíc nedokumentuje rozhraní, které by šlo přímo použít jako `IVocoder`. Integrace by vyžadovala samostatně ověřený a licenčně přípustný adaptér. Viz [VoxDMR](https://www.voxdmr.com/) a jeho [instalační dokumentace](https://www.voxdmr.com/docs/installation/).

### TX

PTT zatím **nepřenáší zvuk z mikrofonu**. Při podržení odešle group voice LC header, nulové AMBE bursty v intervalu 60 ms a po uvolnění terminátor s LC. Hlavička/terminátor používají RS(12,9), BPTC(196,96), Golay slot type a data sync. Přenos lze držet maximálně 180 sekund a je dostupný pouze ve stavu Connected. Skutečné hlasové TX vyžaduje doplnění zdroje PCM a licenčně vhodného kodéru.

## Build a testy

Na Windows:

```sh
dotnet build DMR-RADIO.slnx
dotnet test DMR-RADIO.Tests/DMR-RADIO.Tests.csproj
```

Pro cross-build WPF projektu z Linuxu přidejte `-p:EnableWindowsTargeting=true` k `dotnet build`. Testovací projekt je platformně nezávislý a ověřuje DMRD, AMBE bursty a LC header/terminátor.
