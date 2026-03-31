$ErrorActionPreference = "Stop"

$DownloadUrl = "https://storage.barkfluff.com/get/barkfluffwindows/"
$TempZip     = Join-Path $env:TEMP "BarkFluff_install.zip"
$InstallPath = Join-Path $env:APPDATA "BarkFluff"
$ExePath     = Join-Path $InstallPath "Barkfluff.exe"

Write-Host "Downloading BarkFluff..."

if (Test-Path $TempZip) {
    Remove-Item $TempZip -Force
}

$Downloaded = $false

# 1. BITS
try {
    Write-Host "Trying BITS transfer..."
    Start-BitsTransfer -Source $DownloadUrl -Destination $TempZip
    $Downloaded = $true
    Write-Host "Downloaded via BITS."
}
catch {
    Write-Warning "BITS failed, falling back to WebClient..."
}

# 2. WebClient fallback
if (-not $Downloaded) {
    $wc = New-Object System.Net.WebClient
    $wc.DownloadFile($DownloadUrl, $TempZip)
    Write-Host "Downloaded via WebClient."
}

# 3. Extract
Write-Host "Extracting to: $InstallPath"
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath | Out-Null
}
Expand-Archive -Path $TempZip -DestinationPath $InstallPath -Force
Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
Write-Host "Extraction complete."

# 4. Start Menu shortcut
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

# 5. Register bf:// protocol (requires Administrator - separate elevated process)
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
    Write-Host "Protocol bf:// is already registered."
}

# 6. Launch BarkFluff
if (Test-Path $ExePath) {
    Write-Host "Launching BarkFluff..."
    Start-Process -FilePath $ExePath -WorkingDirectory $InstallPath
}
else {
    Write-Warning "Barkfluff.exe not found at: $ExePath"
}
