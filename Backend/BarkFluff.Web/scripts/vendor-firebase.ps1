$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
$projectRoot = Resolve-Path (Join-Path $scriptRoot '..')
Push-Location $scriptRoot
try {
    & npm install --silent
    if ($LASTEXITCODE -ne 0) { throw "npm install failed ($LASTEXITCODE)" }

    $outDir = Join-Path $projectRoot 'wwwroot\js\vendor'
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    & (Join-Path $scriptRoot 'node_modules\.bin\esbuild.cmd') (Join-Path $scriptRoot 'firebase-compat-entry.js') `
        --bundle `
        --format=iife `
        --outfile=(Join-Path $outDir 'firebase-messaging-compat.bundle.js') `
        --target=es2020 `
        --log-level=warning
    if ($LASTEXITCODE -ne 0) { throw "esbuild failed ($LASTEXITCODE)" }
} finally {
    Pop-Location
}
