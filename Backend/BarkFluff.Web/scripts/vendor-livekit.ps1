<#
.SYNOPSIS
    Бандлит livekit-client (ESM) в один self-contained browser-глобал window.LivekitClient.
    Результат: wwwroot/js/vendor/livekit-client.bundle.js — подключается обычным <script>.

    LiveKit JS SDK меняется редко, поэтому это отдельный скрипт (не часть generate-proto).
    Кроссплатформенно: см. vendor-livekit.sh для Linux/macOS.

.PREREQUISITES
    - Node.js + npm (esbuild и livekit-client ставятся локально в scripts/node_modules)

.EXAMPLE
    cd Backend/BarkFluff.Web
    pwsh scripts/vendor-livekit.ps1
#>

$ErrorActionPreference = 'Stop'

$scriptRoot  = Split-Path -Parent $PSCommandPath
$projectRoot = Resolve-Path (Join-Path $scriptRoot '..')
$outDir      = Join-Path $projectRoot 'wwwroot\js\vendor'
$entry       = Join-Path $scriptRoot '.livekit-entry.js'

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js не найден. Установите с https://nodejs.org"
}

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# 1. Зависимости (esbuild + livekit-client) в scripts/node_modules
$haveLivekit = Test-Path (Join-Path $scriptRoot 'node_modules\livekit-client')
$haveEsbuild = Test-Path (Join-Path $scriptRoot 'node_modules\esbuild')
if (-not $haveLivekit -or -not $haveEsbuild) {
    Write-Host "Устанавливаю зависимости (esbuild, livekit-client)..." -ForegroundColor Cyan
    Push-Location $scriptRoot
    try {
        & npm install --silent
        if ($LASTEXITCODE -ne 0) { throw "npm install failed ($LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
}

# 2. Точка входа: ре-экспорт всего публичного API livekit-client в глобал
Set-Content -Path $entry -Value "export * from 'livekit-client';" -Encoding UTF8

# 3. esbuild → IIFE-глобал window.LivekitClient
Write-Host "esbuild → $outDir\livekit-client.bundle.js" -ForegroundColor Cyan
$esbuild = Join-Path $scriptRoot 'node_modules\.bin\esbuild.cmd'
if (-not (Test-Path $esbuild)) { $esbuild = Join-Path $scriptRoot 'node_modules\.bin\esbuild' }

& $esbuild $entry `
    --bundle `
    --format=iife `
    --global-name=LivekitClient `
    --outfile="$outDir\livekit-client.bundle.js" `
    --target=es2020 `
    --minify `
    --log-level=warning

$exit = $LASTEXITCODE
Remove-Item -Force $entry -ErrorAction SilentlyContinue
if ($exit -ne 0) { throw "esbuild failed ($exit)" }

Write-Host "`nГотово. Подключите в HTML:" -ForegroundColor Green
Write-Host "  <script src=`"/js/vendor/livekit-client.bundle.js`"></script>" -ForegroundColor Gray
Write-Host "  // доступно: window.LivekitClient.{Room, RoomEvent, Track, ConnectionState, ...}" -ForegroundColor Gray
