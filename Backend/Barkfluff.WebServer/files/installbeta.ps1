$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$BaseUrl     = "https://storage.barkfluff.com"
$Channel     = "beta"
$TempZip     = Join-Path $env:TEMP "BarkFluff_install.zip"
$InstallPath = Join-Path $env:APPDATA "BarkFluff"
$ExePath     = Join-Path $InstallPath "Barkfluff.exe"

Write-Host "Fetching download info..."

$Meta        = $null
$DownloadUrl = "$BaseUrl/get/barkfluffwindows/$Channel"

try {
    $Meta = Invoke-RestMethod -Uri "$BaseUrl/get/barkfluffwindows/$Channel/bitsurl" -Method Get
    if ($Meta.version)  { Write-Host "Version: $($Meta.version)" }
    if ($Meta.fileSize) { Write-Host "Size: $([math]::Round($Meta.fileSize / 1MB, 2)) MB" }
    if ($Meta.url)      { $DownloadUrl = $Meta.url }
}
catch {
    Write-Warning "Failed to fetch metadata, checksum will be skipped."
}

if (Test-Path $TempZip) {
    Remove-Item $TempZip -Force
}

$Downloaded = $false

# 1. BITS — URL from /bitsurl (server-side cache, supports Range)
try {
    Write-Host "Downloading via BITS..."
    Start-BitsTransfer -Source $DownloadUrl -Destination $TempZip
    $Downloaded = $true
    Write-Host "Downloaded via BITS."
}
catch {
    Write-Warning "BITS failed, falling back to WebClient..."
}

# 2. WebClient fallback
if (-not $Downloaded) {
    Write-Host "Downloading via WebClient..."
    $wc = New-Object System.Net.WebClient
    $wc.DownloadFile($DownloadUrl, $TempZip)
    Write-Host "Downloaded via WebClient."
}

# 3. Checksum verification
if ($Meta -ne $null -and $Meta.checksum) {
    Write-Host "Verifying checksum..."
    $Hash = (Get-FileHash -Path $TempZip -Algorithm SHA256).Hash
    if ($Hash -ne $Meta.checksum.ToUpper()) {
        Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
        throw "Checksum mismatch: expected $($Meta.checksum.ToUpper()), got $Hash"
    }
    Write-Host "Checksum OK."
}

# 4. Extract
Write-Host "Extracting to: $InstallPath"
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath | Out-Null
}
Expand-Archive -Path $TempZip -DestinationPath $InstallPath -Force
Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
Write-Host "Extraction complete."

# 5. Start Menu shortcut
$StartMenuPrograms = Join-Path ([Environment]::GetFolderPath('StartMenu')) "Programs"
$ShortcutPath = Join-Path $StartMenuPrograms "BarkFluff.lnk"

if (-not (Test-Path $ShortcutPath)) {
    Write-Host "Creating Start Menu shortcut..."
    $wshell = New-Object -ComObject WScript.Shell
    $shortcut = $wshell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $ExePath
    $shortcut.WorkingDirectory = $InstallPath
    $shortcut.Description = "BarkFluff Messenger"
    $shortcut.Save()
    Write-Host "Shortcut created: $ShortcutPath"
}
else {
    Write-Host "Start Menu shortcut already exists."
}

# 6. Register bf:// protocol
$ProtocolRegistered = Test-Path "Registry::HKEY_CLASSES_ROOT\bf"

if (-not $ProtocolRegistered) {
    Write-Host "Registering bf:// protocol (requires Administrator)..."

    $RegScript = @"
`$exePath = '$($ExePath -replace "'", "''")'
New-PSDrive -Name HKCR -PSProvider Registry -Root HKEY_CLASSES_ROOT -ErrorAction SilentlyContinue | Out-Null
New-Item -Path 'HKCR:\bf' -Force | Out-Null
Set-ItemProperty -Path 'HKCR:\bf' -Name '(Default)' -Value 'URL:BarkFluff Messenger Protocol'
Set-ItemProperty -Path 'HKCR:\bf' -Name 'URL Protocol' -Value ''
New-Item -Path 'HKCR:\bf\DefaultIcon' -Force | Out-Null
Set-ItemProperty -Path 'HKCR:\bf\DefaultIcon' -Name '(Default)' -Value ('"' + `$exePath + '",0')
New-Item -Path 'HKCR:\bf\shell\open\command' -Force | Out-Null
Set-ItemProperty -Path 'HKCR:\bf\shell\open\command' -Name '(Default)' -Value ('"' + `$exePath + '" "%1"')
"@

    $Bytes   = [System.Text.Encoding]::Unicode.GetBytes($RegScript)
    $Encoded = [Convert]::ToBase64String($Bytes)

    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -NonInteractive -EncodedCommand $Encoded" -Wait
    Write-Host "Protocol registration complete."
}
else {
    Write-Host "Protocol bf:// already registered."
}

# 7. Launch BarkFluff
if (Test-Path $ExePath) {
    Write-Host "Launching BarkFluff..."
    Start-Process -FilePath $ExePath -WorkingDirectory $InstallPath
}
else {
    Write-Warning "Barkfluff.exe not found at: $ExePath"
}
