$ErrorActionPreference = 'Stop'

$serial = '38031FDJG00768'
$testPackage = 'com.barkfluff.client.core.test'
$testClass = 'com.barkfluff.client.bft.BftClientInstrumentedTest'
$ownerPackages = @('com.barkfluff.nightly', 'com.barkfluff.dev', 'com.barkfluff.BarkCloud')
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$androidRoot = Join-Path $repositoryRoot 'Android'
$sdkRoot = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($sdkRoot)) {
    $sdkRoot = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
}
$adbPath = (Get-Command adb -ErrorAction Stop).Source
$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
$apkPath = Join-Path $androidRoot 'core\build\outputs\apk\androidTest\debug\core-debug-androidTest.apk'
$manifestPath = Join-Path $androidRoot 'core\build\intermediates\packaged_manifests\debugAndroidTest\processDebugAndroidTestManifest\AndroidManifest.xml'
$temporaryRoot = Join-Path $env:TEMP ('bft-pixel-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $temporaryRoot 'source'
$proxyOutput = Join-Path $temporaryRoot 'publish'
$keysFile = Join-Path $temporaryRoot 'client-keys.txt'
$connectionFile = Join-Path $temporaryRoot 'connection.txt'
$proxyStdout = Join-Path $temporaryRoot 'proxy.stdout.log'
$proxyStderr = Join-Path $temporaryRoot 'proxy.stderr.log'
$apkBuildLog = Join-Path $temporaryRoot 'apk-build.log'
$gradleLog = Join-Path $temporaryRoot 'gradle.log'
$proxyProcess = $null
$reverseAdded = $false
$installAttempted = $false
$port = 0
$result = 1

function Get-FreeLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port }
    finally { $listener.Stop() }
}

function Copy-SourceTree([string]$source, [string]$destination) {
    Get-ChildItem -LiteralPath $source -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($source.Length).TrimStart('\', '/')
        $segments = $relative -split '[\\/]'
        if ($segments -contains 'bin' -or $segments -contains 'obj' -or $segments -contains '.git') { return }
        $target = Join-Path $destination $relative
        $targetDirectory = Split-Path -Parent $target
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }
}

function Get-BarkFluffPackages {
    $lines = @(& $adbPath -s $serial shell pm list packages 2>&1 | ForEach-Object { "$_" })
    return @($lines | Where-Object { $_ -match '^package:com\.barkfluff\.' })
}

try {
    $env:ANDROID_SERIAL = $serial
    $env:ANDROID_HOME = $sdkRoot
    $env:ANDROID_SDK_ROOT = $sdkRoot

    Write-Output 'Packages before device run:'
    $beforePackages = Get-BarkFluffPackages
    $beforePackages | ForEach-Object { Write-Output $_ }
    foreach ($package in $ownerPackages) {
        if ($beforePackages -notcontains "package:$package") {
            throw "Expected owner package is missing before device run: $package"
        }
    }
    if ($beforePackages -contains "package:$testPackage") {
        throw 'The core instrumentation package is already installed; refusing to replace or remove it.'
    }

    $sdkLines = @(& $dotnetPath --list-sdks)
    $sdkVersions = foreach ($line in $sdkLines) {
        if ($line -match '^\s*(10\.0\.\d+)\s+\[') { $Matches[1] }
    }
    $sdkVersion = $sdkVersions | Sort-Object { [version]$_ } -Descending | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($sdkVersion)) { throw 'A stable .NET 10 SDK is required to build the Exit test host.' }

    New-Item -ItemType Directory -Path $sourceRoot, $proxyOutput -Force | Out-Null
    Copy-SourceTree (Join-Path $repositoryRoot 'Backend\BarkFluff.BftProxy') (Join-Path $sourceRoot 'Backend\BarkFluff.BftProxy')
    Copy-SourceTree (Join-Path $repositoryRoot 'Shared\BarkFluff.Bft') (Join-Path $sourceRoot 'Shared\BarkFluff.Bft')
    $globalJson = @{ sdk = @{ version = $sdkVersion; rollForward = 'disable'; allowPrerelease = $false } } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'global.json'), $globalJson, [Text.UTF8Encoding]::new($false))

    $proxyProject = Join-Path $sourceRoot 'Backend\BarkFluff.BftProxy\BarkFluff.BftProxy.csproj'
    Push-Location $sourceRoot
    try {
        & $dotnetPath build $proxyProject --configuration Release --output $proxyOutput --nologo --verbosity quiet
        $buildExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($buildExit -ne 0) { throw "Temporary .NET Exit build failed (exit $buildExit)." }

    $proxyDll = Join-Path $proxyOutput 'BarkFluff.BftProxy.dll'
    $keysFile = Join-Path $temporaryRoot 'client-keys.txt'
    [IO.File]::WriteAllText($keysFile, '', [Text.UTF8Encoding]::new($false))
    $genkeyLines = @(& $dotnetPath $proxyDll genkey 2>$null)
    if ($LASTEXITCODE -ne 0) { throw 'Exit genkey failed.' }
    $privateKeyLine = $genkeyLines | Where-Object { $_ -match '^BFT_STATIC_PRIVATE_KEY=' } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($privateKeyLine)) { throw 'Exit genkey returned no static private key.' }
    $privateKey = $privateKeyLine.Substring('BFT_STATIC_PRIVATE_KEY='.Length)

    $reverseList = @(& $adbPath -s $serial reverse --list 2>&1 | ForEach-Object { "$_" })
    for ($attempt = 0; $attempt -lt 32; $attempt++) {
        $candidatePort = Get-FreeLoopbackPort
        if (-not ($reverseList | Where-Object { $_ -match "\btcp:$candidatePort\b" })) {
            $port = $candidatePort
            break
        }
    }
    if ($port -eq 0) { throw 'Could not select an unused adb reverse port.' }
    $upstreamBeaconPort = Get-FreeLoopbackPort
    $upstreamIdentityPort = Get-FreeLoopbackPort

    $env:BFT_STATIC_PRIVATE_KEY = $privateKey
    $env:BFT_CLIENT_KEYS_FILE = $keysFile
    $linkLines = @(& $dotnetPath $proxyDll add-client --label allowed --name 'Android instrumentation' --host 127.0.0.1 --port $port 2>$null)
    if ($LASTEXITCODE -ne 0) { throw 'Exit add-client failed.' }
    $connection = ($linkLines -join '').Trim()
    if (-not $connection.StartsWith('bft://')) { throw 'Exit add-client returned no BFT connection.' }
    [IO.File]::WriteAllText($connectionFile, $connection, [Text.UTF8Encoding]::new($false))

    Push-Location $androidRoot
    try {
        & .\gradlew.bat :core:assembleDebugAndroidTest `
            "-PbftAndroidTestClass=$testClass" "-PbftConnectionFile=$connectionFile" --console=plain *> $apkBuildLog
        $apkBuildExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    $safeApkBuildLines = Get-Content -LiteralPath $apkBuildLog | Where-Object { $_ -notmatch 'bft://' }
    $safeApkBuildLines | Select-Object -Last 80 | ForEach-Object { Write-Output $_ }
    if ($apkBuildExit -ne 0) { throw "Core BFT instrumentation APK build failed (exit $apkBuildExit)." }
    if (-not (Test-Path -LiteralPath $apkPath)) { throw 'The :core androidTest APK was not produced.' }
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'The packaged :core androidTest manifest is missing.' }
    $manifest = [IO.File]::ReadAllText($manifestPath)
    if ($manifest -match 'usesCleartextTraffic') { throw 'The :core test APK manifest must not declare usesCleartextTraffic.' }

    $aaptPath = Get-ChildItem (Join-Path $sdkRoot 'build-tools') -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'aapt.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($aaptPath)) { throw 'Android build-tools aapt.exe is unavailable.' }
    $badging = (& $aaptPath dump badging $apkPath) -join "`n"
    $packageMatch = [regex]::Match($badging, "package: name='([^']+)'")
    if (-not $packageMatch.Success) { throw 'Could not read the androidTest APK applicationId.' }
    $apkPackage = $packageMatch.Groups[1].Value
    Write-Output "Test APK package: $apkPackage"
    if ($apkPackage -ne $testPackage -or $ownerPackages -contains $apkPackage) {
        throw 'Refusing to install an unexpected androidTest APK package.'
    }

    $env:BFT_MODE = 'Exit'
    $env:BFT_LISTEN = "127.0.0.1:$port"
    $env:BFT_PROXY_ID = 'android-device-test'
    $env:BFT_ORIGIN_SECRET = 'a' * 64
    $env:BFT_UPSTREAM_BEACON = "https://127.0.0.1:$upstreamBeaconPort"
    $env:BFT_UPSTREAM_IDENTITY = "https://127.0.0.1:$upstreamIdentityPort"
    $proxyProcess = Start-Process -FilePath $dotnetPath -ArgumentList ('"{0}"' -f $proxyDll) `
        -WorkingDirectory $temporaryRoot -RedirectStandardOutput $proxyStdout -RedirectStandardError $proxyStderr `
        -WindowStyle Hidden -PassThru

    $listenerReady = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        $proxyProcess.Refresh()
        if ($proxyProcess.HasExited) { throw 'Exit process stopped before opening its listener.' }
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $client.Connect([System.Net.IPAddress]::Loopback, $port)
            $listenerReady = $true
            break
        }
        catch { Start-Sleep -Milliseconds 50 }
        finally { $client.Dispose() }
    }
    if (-not $listenerReady) { throw 'Exit listener did not become ready.' }

    & $adbPath -s $serial reverse "tcp:$port" "tcp:$port" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'adb reverse failed.' }
    $reverseAdded = $true

    $env:BFT_TEST_CONNECTION = $null
    $installAttempted = $true
    Push-Location $androidRoot
    try {
        & .\gradlew.bat :core:connectedDebugAndroidTest `
            "-PbftAndroidTestClass=$testClass" "-PbftConnectionFile=$connectionFile" --console=plain *> $gradleLog
        $gradleExit = $LASTEXITCODE
    }
    finally { Pop-Location }

    $safeGradleLines = Get-Content -LiteralPath $gradleLog | Where-Object { $_ -notmatch 'bft://' }
    $safeGradleLines | Select-Object -Last 140 | ForEach-Object { Write-Output $_ }
    if ($gradleExit -ne 0) { throw "Core BFT instrumentation failed (exit $gradleExit)." }
    $result = 0
}
catch {
    Write-Error $_
    $result = 1
}
finally {
    if ($reverseAdded) {
        & $adbPath -s $serial reverse --remove "tcp:$port" | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Error 'Could not remove the adb reverse mapping created by this script.'; $result = 1 }
    }

    if ($proxyProcess -and -not $proxyProcess.HasExited) {
        Stop-Process -Id $proxyProcess.Id -Force
        Wait-Process -Id $proxyProcess.Id -Timeout 5 -ErrorAction SilentlyContinue
    }

    if ($installAttempted) {
        $installedLines = @(& $adbPath -s $serial shell pm list packages 2>&1 | ForEach-Object { "$_" })
        if ($installedLines -contains "package:$testPackage") {
            & $adbPath -s $serial shell pm uninstall $testPackage | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Error 'Could not remove the core instrumentation APK.'; $result = 1 }
        }
        $stillInstalled = @(& $adbPath -s $serial shell pm list packages 2>&1 | ForEach-Object { "$_" })
        if ($stillInstalled -contains "package:$testPackage") {
            Write-Error 'The core instrumentation APK is still installed.'
            $result = 1
        }
    }

    Remove-Item Env:BFT_STATIC_PRIVATE_KEY, Env:BFT_CLIENT_KEYS_FILE, Env:BFT_MODE, Env:BFT_LISTEN, `
        Env:BFT_PROXY_ID, Env:BFT_ORIGIN_SECRET, Env:BFT_UPSTREAM_BEACON, Env:BFT_UPSTREAM_IDENTITY, `
        Env:BFT_TEST_CONNECTION -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }

    Write-Output 'Packages after device run:'
    try {
        $afterPackages = Get-BarkFluffPackages
        $afterPackages | ForEach-Object { Write-Output $_ }
        foreach ($package in $ownerPackages) {
            if ($afterPackages -notcontains "package:$package") {
                Write-Error "Expected owner package is missing after device run: $package"
                $result = 1
            }
        }
    }
    catch {
        Write-Error 'Could not verify the owner packages after the device run.'
        $result = 1
    }
}

exit $result
