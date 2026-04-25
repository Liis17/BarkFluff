$ErrorActionPreference = "Stop"

$BaseUrl     = "https://storage.barkfluff.com"
$TempZip     = Join-Path $env:TEMP "BarkFluff_install.zip"
$InstallPath = Join-Path $env:APPDATA "BarkFluff"
$ExePath     = Join-Path $InstallPath "Barkfluff.exe"

# --- Выбор канала / Channel selection ---
Write-Host ""
Write-Host "Выберите канал обновления / Choose update channel:"
Write-Host "  1 - Release  (стабильная версия / stable version)"
Write-Host "  2 - Beta     (бета-версия / beta version)"
Write-Host ""

do {
    $choice = Read-Host "Ваш выбор / Your choice [1/2]"
} while ($choice -notin @('1', '2'))

$Channel = if ($choice -eq '2') { 'beta' } else { 'release' }

Write-Host ""
Write-Host "Канал выбран / Selected channel: $Channel"

# --- Получение информации о загрузке / Fetch download info ---
Write-Host "Получение информации о загрузке... / Fetching download info..."

$BitsInfo   = $null
$Downloaded = $false

try {
    $BitsInfo = Invoke-RestMethod -Uri "$BaseUrl/get/barkfluffwindows/$Channel/bitsurl" -Method Get
    if ($BitsInfo.version) {
        Write-Host "Версия / Version: $($BitsInfo.version)"
    }
    if ($BitsInfo.fileSize) {
        Write-Host "Размер / Size: $([math]::Round($BitsInfo.fileSize / 1MB, 2)) MB"
    }
}
catch {
    Write-Warning "Не удалось получить BITS-информацию, будет использован прямой URL. / Failed to get BITS info, will use direct URL."
}

if (Test-Path $TempZip) {
    Remove-Item $TempZip -Force
}

# 1. BITS
if ($BitsInfo -ne $null) {
    try {
        Write-Host "Загрузка через BITS... / Downloading via BITS..."
        Start-BitsTransfer -Source $BitsInfo.url -Destination $TempZip
        $Downloaded = $true
        Write-Host "Загружено через BITS / Downloaded via BITS."
    }
    catch {
        Write-Warning "BITS не удался, переключаемся на WebClient... / BITS failed, falling back to WebClient..."
    }
}

# 2. WebClient fallback
if (-not $Downloaded) {
    Write-Host "Загрузка через WebClient... / Downloading via WebClient..."
    $wc = New-Object System.Net.WebClient
    $wc.DownloadFile("$BaseUrl/get/barkfluffwindows/$Channel", $TempZip)
    Write-Host "Загружено через WebClient / Downloaded via WebClient."
}

# 3. Проверка контрольной суммы / Checksum verification
if ($BitsInfo -ne $null -and $BitsInfo.checksum) {
    Write-Host "Проверка контрольной суммы... / Verifying checksum..."
    $Hash = (Get-FileHash -Path $TempZip -Algorithm SHA256).Hash
    if ($Hash -ne $BitsInfo.checksum.ToUpper()) {
        Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
        throw "Ошибка контрольной суммы / Checksum mismatch: expected $($BitsInfo.checksum.ToUpper()), got $Hash"
    }
    Write-Host "Контрольная сумма верна / Checksum OK."
}

# 4. Распаковка / Extract
Write-Host "Распаковка в / Extracting to: $InstallPath"
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath | Out-Null
}
Expand-Archive -Path $TempZip -DestinationPath $InstallPath -Force
Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
Write-Host "Распаковка завершена / Extraction complete."

# 5. Ярлык в меню Пуск / Start Menu shortcut
$StartMenuPrograms = Join-Path ([Environment]::GetFolderPath('StartMenu')) "Programs"
$ShortcutPath = Join-Path $StartMenuPrograms "BarkFluff.lnk"

if (-not (Test-Path $ShortcutPath)) {
    Write-Host "Создание ярлыка в меню Пуск... / Creating Start Menu shortcut..."
    $wshell = New-Object -ComObject WScript.Shell
    $shortcut = $wshell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $ExePath
    $shortcut.WorkingDirectory = $InstallPath
    $shortcut.Description = "BarkFluff Messenger"
    $shortcut.Save()
    Write-Host "Ярлык создан / Shortcut created: $ShortcutPath"
}
else {
    Write-Host "Ярлык в меню Пуск уже существует / Start Menu shortcut already exists."
}

# 6. Регистрация протокола bf:// / Register bf:// protocol
$ProtocolRegistered = Test-Path "Registry::HKEY_CLASSES_ROOT\bf"

if (-not $ProtocolRegistered) {
    Write-Host "Регистрация протокола bf:// (требуются права администратора)... / Registering bf:// protocol (requires Administrator)..."

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
    Write-Host "Протокол зарегистрирован / Protocol registration complete."
}
else {
    Write-Host "Протокол bf:// уже зарегистрирован / Protocol bf:// is already registered."
}

# 7. Запуск BarkFluff / Launch BarkFluff
if (Test-Path $ExePath) {
    Write-Host "Запуск BarkFluff... / Launching BarkFluff..."
    Start-Process -FilePath $ExePath -WorkingDirectory $InstallPath
}
else {
    Write-Warning "Barkfluff.exe не найден по пути / Barkfluff.exe not found at: $ExePath"
}
