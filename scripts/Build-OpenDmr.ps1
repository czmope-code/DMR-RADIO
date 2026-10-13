param(
    [string]$Msys2Root = "C:\msys64"
)

$ErrorActionPreference = "Stop"
$commit = "d28164b39ba4d91ad5948ff22707937f8944f70f"
$bash = Join-Path $Msys2Root "usr\bin\bash.exe"
$objdump = Join-Path $Msys2Root "mingw64\bin\objdump.exe"

if (-not (Test-Path $bash)) {
    throw "MSYS2 nebylo nalezeno v '$Msys2Root'. Nainstaluj MSYS2 a balíčky GCC/Make pro MINGW64."
}

if (-not (Test-Path $objdump)) {
    throw "Chybí MinGW objdump. V MSYS2 MINGW64 spusť: pacman -S --needed mingw-w64-x86_64-gcc make"
}

$workDirectory = Join-Path $env:TEMP "DmrRadioOpenDmr-$PID"
$archivePath = Join-Path $workDirectory "opendmr.zip"
$destinationDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "DMR-RADIO\Native"

try {
    New-Item -ItemType Directory -Path $workDirectory | Out-Null
    $archiveUrl = "https://github.com/MW0MWZ/OpenDMR/archive/$commit.zip"
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath
    Expand-Archive -Path $archivePath -DestinationPath $workDirectory

    $sourceDirectory = Join-Path $workDirectory "OpenDMR-$commit"
    if (-not (Test-Path (Join-Path $sourceDirectory "Makefile"))) {
        throw "Ve stažené revizi OpenDMR nebyl nalezen Makefile."
    }

    $env:MSYSTEM = "MINGW64"
    $env:CHERE_INVOKING = "1"
    $env:OPENDMR_SOURCE = $sourceDirectory
    & $bash --login -c 'cd "$(cygpath -u "$OPENDMR_SOURCE")" && make clean && make SHARED_EXT=dll SHARED_FLAGS="-shared -Wl,--export-all-symbols -static-libgcc -static-libstdc++"'
    if ($LASTEXITCODE -ne 0) {
        throw "Sestavení OpenDMR selhalo (kód $LASTEXITCODE). Ověř instalaci MINGW64 GCC/G++ a GNU Make."
    }

    $builtDll = Join-Path $sourceDirectory "libopendmr.dll"
    if (-not (Test-Path $builtDll)) {
        throw "Sestavení nedokončilo libopendmr.dll."
    }

    $exports = (& $objdump -p $builtDll | Out-String)
    foreach ($symbol in @("opendmr_decoder_create", "opendmr_decode", "opendmr_decoder_destroy", "opendmr_encoder_create", "opendmr_encode", "opendmr_encoder_destroy")) {
        if ($exports -notmatch [regex]::Escape($symbol)) {
            throw "libopendmr.dll neexportuje požadovanou funkci '$symbol'."
        }
    }

    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -Path $builtDll -Destination (Join-Path $destinationDirectory "libopendmr.dll") -Force
    Write-Host "OpenDMR DLL je připravená: $(Join-Path $destinationDirectory 'libopendmr.dll')"
    Write-Host "Nyní spusť dotnet build DMR-RADIO.slnx; MSBuild DLL zkopíruje vedle aplikace."
}
finally {
    Remove-Item -Path $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
