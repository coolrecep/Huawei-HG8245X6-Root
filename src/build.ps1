# build.ps1 — HekzOntRace v2.0.1 Windows + Linux (Standart + Lite) Tek-Tik Derleme
# Kullanim: Projenin kok klasorunde sag tik -> "PowerShell ile calistir"
#          veya terminal:  pwsh -File .\build.ps1
#
# Gereksinimler:
#   - .NET 9 SDK (yoksa winget ile otomatik kurulur)

param(
    [string]$Run = ''
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot

function Write-Step($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }
function Write-Ok($m)   { Write-Host "   [OK] $m" -ForegroundColor Green }
function Write-Warn($m) { Write-Host "   [!] $m" -ForegroundColor Yellow }

# 1) .NET SDK kontrol
Write-Step ".NET SDK araniyor..."
$dotnetOk = $false
try {
    $v = (dotnet --list-sdks 2>$null) | Select-String '^\s*9\.' | Select-Object -First 1
    if ($v) { Write-Ok ".NET 9 SDK bulundu: $v"; $dotnetOk = $true }
    else    { Write-Warn ".NET 9 SDK bulunamadi" }
} catch { Write-Warn "dotnet komutu bulunamadi" }

if (-not $dotnetOk) {
    Write-Step "winget ile .NET 9 SDK kuruluyor..."
    try {
        winget install Microsoft.DotNet.SDK.9 --silent --accept-package-agreements --accept-source-agreements
        Write-Ok ".NET 9 SDK kuruldu - PATH icin yeni terminal gerekebilir"
    } catch {
        Write-Warn "winget basarisiz - https://dotnet.microsoft.com/download adresinden elle kurun"
        exit 1
    }
}

# 2) Cikti klasorlerini temizle
Write-Step "Onceki derleme ciktilari temizleniyor..."
Remove-Item -Recurse -Force "$ProjectRoot\bin","$ProjectRoot\obj","$ProjectRoot\publish_final" -ErrorAction SilentlyContinue
Write-Ok "Temizlik tamamlandi."

# 3) Restore (Windows + Linux)
Write-Step "NuGet paketleri geri yukleniyor (Windows & Linux)..."
dotnet restore "$ProjectRoot\HekzOntRace.csproj"
if ($LASTEXITCODE -ne 0) { Write-Warn "Windows restore hatasi"; exit $LASTEXITCODE }
dotnet restore "$ProjectRoot\HekzOntRace.Linux.csproj"
if ($LASTEXITCODE -ne 0) { Write-Warn "Linux restore hatasi"; exit $LASTEXITCODE }
Write-Ok "Bagimliliklar guncel."

# 4) [1/4] Windows Standart Surum Yayinlama (HekzOntRace.exe)
Write-Step "[1/4] Windows Standart Surum derleniyor (HekzOntRace.exe)..."
dotnet publish "$ProjectRoot\HekzOntRace.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$ProjectRoot\publish_final"
if ($LASTEXITCODE -ne 0) { Write-Warn "Windows Standart surum publish hatasi!"; exit $LASTEXITCODE }
Write-Ok "Windows Standart Surum hazirlandi."

# 5) [2/4] Windows Lite Surum Yayinlama (HekzOntRace-Lite.exe)
Write-Step "[2/4] Windows Lite Surum derleniyor (HekzOntRace-Lite.exe)..."
Remove-Item -Recurse -Force "$ProjectRoot\obj" -ErrorAction SilentlyContinue
dotnet publish "$ProjectRoot\HekzOntRace.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:LiteMode=true `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$ProjectRoot\publish_final"
if ($LASTEXITCODE -ne 0) { Write-Warn "Windows Lite surum publish hatasi!"; exit $LASTEXITCODE }
Write-Ok "Windows Lite Surum hazirlandi."

# 6) [3/4] Linux (x64) Standart Surum Yayinlama (HekzOntRace-linux-x64)
Write-Step "[3/4] Linux (x64) Standart Surum derleniyor (HekzOntRace-linux-x64)..."
Remove-Item -Recurse -Force "$ProjectRoot\obj" -ErrorAction SilentlyContinue
dotnet publish "$ProjectRoot\HekzOntRace.Linux.csproj" `
    -c Release -r linux-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$ProjectRoot\publish_final"
if ($LASTEXITCODE -ne 0) { Write-Warn "Linux Standart surum publish hatasi!"; exit $LASTEXITCODE }
Write-Ok "Linux (x64) Standart Surum hazirlandi."

# 7) [4/4] Linux (x64) Lite Surum Yayinlama (HekzOntRace-Lite-linux-x64)
Write-Step "[4/4] Linux (x64) Lite Surum derleniyor (HekzOntRace-Lite-linux-x64)..."
Remove-Item -Recurse -Force "$ProjectRoot\obj" -ErrorAction SilentlyContinue
dotnet publish "$ProjectRoot\HekzOntRace.Linux.csproj" `
    -c Release -r linux-x64 --self-contained true `
    -p:LiteMode=true `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$ProjectRoot\publish_final"
if ($LASTEXITCODE -ne 0) { Write-Warn "Linux Lite surum publish hatasi!"; exit $LASTEXITCODE }
Write-Ok "Linux (x64) Lite Surum hazirlandi."

# 8) Dogrulama ve Raporlama
Write-Step "Derleme Sonuclari Dogrulaniyor..."
$exeWinStd   = Join-Path $ProjectRoot "publish_final\HekzOntRace.exe"
$exeWinLite  = Join-Path $ProjectRoot "publish_final\HekzOntRace-Lite.exe"
$binLinStd   = Join-Path $ProjectRoot "publish_final\HekzOntRace-linux-x64"
$binLinLite  = Join-Path $ProjectRoot "publish_final\HekzOntRace-Lite-linux-x64"

$winStdOk  = Test-Path $exeWinStd
$winLiteOk = Test-Path $exeWinLite
$linStdOk  = Test-Path $binLinStd
$linLiteOk = Test-Path $binLinLite

if ($winStdOk -and $winLiteOk -and $linStdOk -and $linLiteOk) {
    $szWinStd  = [math]::Round((Get-Item $exeWinStd).Length / 1MB, 1)
    $szWinLite = [math]::Round((Get-Item $exeWinLite).Length / 1MB, 1)
    $szLinStd  = [math]::Round((Get-Item $binLinStd).Length / 1MB, 1)
    $szLinLite = [math]::Round((Get-Item $binLinLite).Length / 1MB, 1)

    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host " TEK-TIK DERLEME BASARIYLA TAMAMLANDI (WINDOWS + LINUX):" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " 1) Windows Standart : $exeWinStd ($szWinStd MB)" -ForegroundColor White
    Write-Host " 2) Windows Lite     : $exeWinLite ($szWinLite MB)" -ForegroundColor Yellow
    Write-Host " 3) Linux Standart   : $binLinStd ($szLinStd MB)" -ForegroundColor Cyan
    Write-Host " 4) Linux Lite       : $binLinLite ($szLinLite MB)" -ForegroundColor Magenta
    Write-Host "============================================================`n" -ForegroundColor Green

    $ans = $Run
    if ([string]::IsNullOrWhiteSpace($ans) -and [Environment]::UserInteractive) {
        $ans = Read-Host "Hangi surumu calistirmak istersiniz? (1: Windows Standart, 2: Windows Lite, H: Cikis)"
    }
    if ($ans -eq '1') { Start-Process $exeWinStd }
    elseif ($ans -eq '2') { Start-Process $exeWinLite }
} else {
    if (-not $winStdOk)  { Write-Warn "Windows Standart ciktisi bulunamadi: $exeWinStd" }
    if (-not $winLiteOk) { Write-Warn "Windows Lite ciktisi bulunamadi: $exeWinLite" }
    if (-not $linStdOk)  { Write-Warn "Linux Standart ciktisi bulunamadi: $binLinStd" }
    if (-not $linLiteOk) { Write-Warn "Linux Lite ciktisi bulunamadi: $binLinLite" }
    exit 1
}
