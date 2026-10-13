# DMR Radio

WPF klient pro Homebrew/MMDVM přes UDP, kompatibilní se servery FreeDMR. Přihlášení a uložení nastavení včetně DPAPI zůstávají beze změny.

Po standardním Homebrew přihlášení (`RPTL`, výzva `RPTACK`, ověření `RPTK`, konfigurace `RPTC`) klient odešle seznam požadované TG příkazem `RPTO`, například `TS2=230`. Změna TG nebo timeslotu odešle aktualizovaný `RPTO`. Heslo je sdílené heslo Homebrew serveru a používá se při `RPTK`; není součástí `DMRD`.

## RX

Aplikace přijímá standardní 55bajtové pakety DMRD s 33bajtovým burstem, vybírá pouze skupinový hovor pro zvolenou TG a timeslot a z burstu skládá tři 72bitové AMBE+2 kanálově kódované rámce po 9 bajtech. Hlasová data proto nejsou prostých 27 souvislých bajtů za 12bajtovou hlavičkou: části A/B/C jsou v burstu prokládané a druhý rámec je rozdělen kolem 48bitového sync/embedded pole. `DmrVoiceBurstCodec` provádí jejich extrakci; jitter buffer předává dekodéru jeden rámec každých 20 ms s cílovým předstihem 60 ms.

`IVocoder` je rozhraní pro dekodér a kodér. Aplikace potřebuje OpenDMR (`libopendmr.dll`) přes C API; bez něj použije `NullVocoder`, který vrací ticho pro RX a nulové rámce pro TX. Při hovoru se do logu zapíše, zda je OpenDMR aktivní, a případná chyba dekódování. Návod níže sestaví DLL pro Windows a projekt ji při dalším buildu automaticky zkopíruje vedle EXE. Zvuk se přehrává přes NAudio `WaveOut` jako 8 kHz mono PCM, což odpovídá výstupu dekodéru; `MediaFoundationResampler` se nepoužívá, protože tato pipeline nepoužívá `WasapiOut` ani nevyžaduje 48 kHz PCM vstup. Posuvník hlasitosti ovládá výstup a VU metr počítá RMS z dekódovaného PCM. Hovor končí terminátorem nebo po 800 ms bez paketů; při změně TG/timeslotu či odpojení se přehrávání a buffer uvolní.

Výstupní reproduktor/sluchátka lze vybrat v seznamu **VÝSTUP ZVUKU** pod posuvníkem hlasitosti. Volba „Výchozí zařízení Windows“ použije aktuální výchozí zařízení. Výběr se uloží do nastavení; změna během hovoru restartuje lokální přehrávač.

### OpenDMR a licenční podmínky

Adaptér `OpenDmrVocoder` volá `opendmr_decoder_create/opendmr_decode` a `opendmr_encoder_create/opendmr_encode`. OpenDMR přebírá dekódování 72bitového rámce v kanonickém pořadí A+B+C, které vytváří `DmrVoiceBurstCodec`; vrací 160 PCM vzorků a používá stejný formát při kódování pro TX. Zdrojový kód OpenDMR ani jeho binární knihovna nejsou v tomto repozitáři.

1. Nainstaluj [MSYS2](https://www.msys2.org/) a v terminálu **MSYS2 MINGW64** nainstaluj nástroje:

   ```sh
   pacman -S --needed mingw-w64-x86_64-gcc make
   ```

2. V PowerShellu z kořene repozitáře spusť:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\Build-OpenDmr.ps1
   dotnet build DMR-RADIO.slnx
   dotnet run --project .\DMR-RADIO\DMR-RADIO.csproj
   ```

   Skript stáhne neměnnou revizi OpenDMR `d28164b39ba4d91ad5948ff22707937f8944f70f`, sestaví DLL s MinGW a umístí ji do ignorované složky `DMR-RADIO\Native`. MSBuild ji automaticky přidá do výstupní i publish složky aplikace. Při hovoru musí log zobrazit **OpenDMR AMBE+2 dekodér je aktivní.** Pokud místo toho hlásí chybějící dekodér nebo chybu AMBE, audio nepůjde; při úspěšném dekódování zkontroluj vybraný výstup zvuku a hlasitost.

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
