$ErrorActionPreference = "Stop"

$Url = "https://barkFluff.com/download/installer"
$TempPath = Join-Path $env:TEMP "Barkfluff.Updater.CLI.exe"

Write-Host "Downloading BarkFluff installer..."
Write-Host "Source: $Url"
Write-Host "Target: $TempPath"

if (Test-Path $TempPath) {
    Remove-Item $TempPath -Force
}

Start-BitsTransfer `
    -Source $Url `
    -Destination $TempPath `
    -DisplayName "BarkFluff Installer" `
    -Description "Downloading BarkFluff updater"

Write-Host "Download completed."

Write-Host "Starting installer..."
Start-Process -FilePath $TempPath
