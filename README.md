# DMR Radio

WPF klient pro Homebrew/MMDVM přes UDP. Přihlášení a uložení nastavení včetně DPAPI zůstávají beze změny.

## RX

Aplikace přijímá pakety DMRD, vybírá pouze skupinový hovor pro zvolenou TG a timeslot a z 33bajtového burstu skládá tři 72bitové AMBE+2 kanálově kódované rámce. Rámce nejsou v burstu uloženy jako tři sousední devítibajtové bloky: jejich části A/B/C jsou prokládané a druhý rámec je rozdělen kolem 48bitového sync/embedded pole. Dekódování musí zpracovat i kanálové kódování, které z těchto 72 bitů obnovuje vlastní 49bitová hlasová data.

`IVocoder` je rozhraní pro dekodér a kodér. Aplikace se pokusí načíst OpenDMR (`libopendmr.dll`) přes C API. Pokud knihovna chybí nebo je nekompatibilní, použije `NullVocoder`, který vrací ticho pro RX a nulové rámce pro TX; aplikace tuto skutečnost zapíše do logu. Po připojení OpenDMR se zvuk přehrává přes NAudio v 8 kHz mono s jitter bufferem přibližně 60 ms. Posuvník hlasitosti ovládá výstup a VU metr počítá RMS z dekódovaného PCM. Hovor končí terminátorem nebo po 800 ms bez paketů; při změně TG/timeslotu či odpojení se přehrávání a buffer uvolní.

Výstupní reproduktor/sluchátka lze vybrat v seznamu **VÝSTUP ZVUKU** pod posuvníkem hlasitosti. Volba „Výchozí zařízení Windows“ použije aktuální výchozí zařízení. Výběr se uloží do nastavení; změna během hovoru restartuje lokální přehrávač.

### OpenDMR a licenční podmínky

Adaptér `OpenDmrVocoder` volá `opendmr_decoder_create/opendmr_decode` a `opendmr_encoder_create/opendmr_encode`. OpenDMR přebírá dekódování 72bitového rámce v kanonickém pořadí A+B+C, které vytváří `DmrVoiceBurstCodec`; vrací 160 PCM vzorků a používá stejný formát při kódování pro TX. Zdrojový kód OpenDMR ani jeho binární knihovna nejsou v tomto repozitáři.

1. Získej zdrojový kód z [MW0MWZ/OpenDMR](https://github.com/MW0MWZ/OpenDMR) a zkontroluj jeho README, licenci a `decoder/CREDITS`/`encoder/CREDITS`.
2. V prostředí MSYS2 MINGW64 s GCC/G++ a GNU Make sestav DLL:

   ```sh
   make SHARED_EXT=dll SHARED_FLAGS="-shared -Wl,--export-all-symbols"
   ```

   Výsledkem podle Makefile upstreamu má být `libopendmr.dll`. Zkopíruj DLL a případné MinGW runtime DLL závislosti vedle `DMR-RADIO.exe`. Build upstream Makefile na Windows zde nebyl ověřen.
3. Spusť aplikaci a ověř log: bez nativní knihovny je použit `NullVocoder`; při jejím úspěšném načtení log tuto fallback zprávu nevypíše.

OpenDMR je pod **GNU GPL v2** a obsahuje GPL části mbelib-neo a OP25. Dynamické propojení z této aplikace může mít dopad na licenční podmínky celé distribuované aplikace; před distribucí si ověř povinnosti GPL a právo distribuovat výslednou aplikaci. GPL samo o sobě není patentová licence AMBE. Toto není právní rada.

`md380_vocoder` používá firmware z rádia MD-380 a jeho README cílí ARM, nikoli Windows x64. VoxDMR také uvádí MD-380 firmware emulaci, ale jeho instalační dokumentace říká, že firmware se stahuje mimo aplikaci a není součástí distribuce; jeho interní rozhraní není kompatibilní C API OpenDMR. Firmware nepřebíráme ani nestahujeme.

### TX

PTT zatím **nepřenáší zvuk z mikrofonu**. Při podržení odešle group voice LC header, nulové AMBE bursty v intervalu 60 ms a po uvolnění terminátor s LC. Hlavička/terminátor používají RS(12,9), BPTC(196,96), Golay slot type a data sync. Přenos lze držet maximálně 180 sekund a je dostupný pouze ve stavu Connected. Skutečné hlasové TX vyžaduje doplnění zdroje PCM a licenčně vhodného kodéru.

## Build a testy

Na Windows:

```sh
dotnet build DMR-RADIO.slnx
dotnet test DMR-RADIO.Tests/DMR-RADIO.Tests.csproj
```

Pro cross-build WPF projektu z Linuxu přidejte `-p:EnableWindowsTargeting=true` k `dotnet build`. Testovací projekt je platformně nezávislý a ověřuje DMRD, AMBE bursty a LC header/terminátor.
