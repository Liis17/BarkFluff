$ErrorActionPreference = "Stop"

$Url = "https://github.com/Liis17/BarkFluff.Releases/releases/download/Installer/Barkfluff.Updater.CLI.exe"
$TempPath = Join-Path $env:TEMP "Barkfluff.Updater.CLI.exe"

Write-Host "Downloading BarkFluff installer..."
Write-Host "Source: $Url"
Write-Host "Target: $TempPath"

if (Test-Path $TempPath) {
    Remove-Item $TempPath -Force
}

$Downloaded = $false

# 1️⃣ Пытаемся через BITS (быстро и стабильно)
try {
    Write-Host "Trying BITS transfer..."
    Start-BitsTransfer -Source $Url -Destination $TempPath
    $Downloaded = $true
    Write-Host "Downloaded via BITS."
}
catch {
    Write-Warning "BITS failed, falling back to WebClient..."
}

# 2️⃣ Fallback на WebClient
if (-not $Downloaded) {
    $wc = New-Object System.Net.WebClient
    $wc.DownloadFile($Url, $TempPath)
    Write-Host "Downloaded via WebClient."
}

Write-Host "Starting installer..."
Start-Process -FilePath $TempPath
