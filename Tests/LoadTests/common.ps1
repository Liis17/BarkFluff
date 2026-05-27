$ErrorActionPreference = "Stop"

function Invoke-Grpcurl {
    param([string]$JsonBody, [string[]]$GArgs)
    $tmpJson = Join-Path $env:TEMP "grpcurl-body-$(Get-Random).json"
    $tmpBat = Join-Path $env:TEMP "grpcurl-cmd-$(Get-Random).bat"
    try {
        [System.IO.File]::WriteAllText($tmpJson, $JsonBody, (New-Object System.Text.UTF8Encoding $false))
        $flagArgs = @()
        $posArgs = @()
        for ($i = 0; $i -lt $GArgs.Count; $i++) {
            if ($GArgs[$i] -match '^-') {
                $flagArgs += $GArgs[$i]
                if ($i + 1 -lt $GArgs.Count -and $GArgs[$i + 1] -notmatch '^-') {
                    $i++
                    if ($flagArgs[-1] -match '^-H$') {
                        $flagArgs += "`"$($GArgs[$i])`""
                    } else {
                        $flagArgs += $GArgs[$i]
                    }
                }
            } else {
                $posArgs += $GArgs[$i]
            }
        }
        $flagStr = $flagArgs -join ' '
        $posStr = $posArgs -join ' '
        $batContent = "@echo off`ngrpcurl $flagStr -d @ $posStr < `"$tmpJson`"`n"
        [System.IO.File]::WriteAllText($tmpBat, $batContent, [System.Text.Encoding]::ASCII)
        $output = & cmd /c $tmpBat 2>&1 | Out-String
        if ($output -match "Too many arguments|Too few arguments") {
            throw "grpcurl failed:`n$output`nBAT: $batContent"
        }
        return $output
    }
    finally {
        if (Test-Path $tmpJson) { Remove-Item $tmpJson -Force -ErrorAction SilentlyContinue }
        if (Test-Path $tmpBat) { Remove-Item $tmpBat -Force -ErrorAction SilentlyContinue }
    }
}

function Get-XAuthHeaders {
    $deviceId = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([guid]::NewGuid().ToString()))
    $deviceName = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("LoadTest-Runner"))
    $ip = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("127.0.0.1"))
    $os = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("LoadTest"))
    $appName = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("BarkFluff.LoadTest"))
    $appVersion = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("1.0.0"))
    return @{
        deviceId    = $deviceId
        deviceName  = $deviceName
        ip          = $ip
        os          = $os
        appName     = $appName
        appVersion  = $appVersion
    }
}

function Get-GrpcBaseArgs {
    param(
        [string]$ProtoPath,
        [hashtable]$Headers,
        [switch]$UseTls
    )
    $args = @(
        "-import-path", $ProtoPath,
        "-H", "x-device-id: $($Headers.deviceId)",
        "-H", "x-device-name: $($Headers.deviceName)",
        "-H", "x-ip-address: $($Headers.ip)",
        "-H", "x-os-name: $($Headers.os)",
        "-H", "x-app-name: $($Headers.appName)",
        "-H", "x-app-version: $($Headers.appVersion)"
    )
    if (-not $UseTls) { $args = @("-plaintext") + $args }
    return $args
}

function Get-AuthToken {
    param(
        [string]$IdentityHost,
        [string]$Username,
        [string]$PlainPassword,
        [string[]]$BaseArgs
    )
    $authJson = @{ username = $Username; password = $PlainPassword } | ConvertTo-Json -Compress
    $authResult = Invoke-Grpcurl -JsonBody $authJson -GArgs ($BaseArgs + @(
        "-proto", "identity_api.proto",
        "$IdentityHost",
        "barkfluff.identity.IdentityApi/Auth"
    ))
    $authData = $authResult | ConvertFrom-Json
    $token = $authData.accessToken.value
    if (-not $token) {
        throw "Auth failed: no access_token in response"
    }
    return $token
}

function New-AuthArgs {
    param(
        [string]$IdentityHost,
        [string]$Username,
        [string]$PlainPassword,
        [string[]]$BaseArgs
    )
    $token = Get-AuthToken -IdentityHost $IdentityHost -Username $Username -PlainPassword $PlainPassword -BaseArgs $BaseArgs
    $args = $BaseArgs + @("-H", "x-auth-token: $token")
    return @{ token = $token; authArgs = $args }
}

function Write-GhzConfig {
    param(
        [string]$ConfigPath,
        [string]$ProtoFile,
        [string]$Call,
        [string]$Host_,
        [bool]$Insecure,
        [int]$Concurrency,
        [int]$Total,
        [string]$Duration,
        [int]$Rps,
        $Data,
        [string]$Token,
        [hashtable]$Headers
    )
    $ghzConfig = @{
        proto       = $ProtoFile
        call        = $Call
        host        = $Host_
        insecure    = $Insecure
        concurrency = $Concurrency
        total       = $Total
        data        = $Data
        metadata    = @{
            "x-auth-token"   = $Token
            "x-device-id"    = $Headers.deviceId
            "x-device-name"  = $Headers.deviceName
            "x-ip-address"   = $Headers.ip
            "x-os-name"      = $Headers.os
            "x-app-name"     = $Headers.appName
            "x-app-version"  = $Headers.appVersion
        }
    }
    if ($Duration) {
        $ghzConfig.Remove("total")
        $ghzConfig["duration"] = $Duration
    }
    if ($Rps -gt 0) {
        $ghzConfig["rps"] = $Rps
    }
    $json = $ghzConfig | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($ConfigPath, $json, (New-Object System.Text.UTF8Encoding $false))
    return $ConfigPath
}

function Run-GhzTest {
    param(
        [string]$ConfigPath,
        [string]$OutputDir,
        [string]$Label
    )
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $htmlReport = Join-Path $OutputDir "report-$Label-$timestamp.html"

    & ghz --config $ConfigPath --output $htmlReport --format html

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Report: $htmlReport" -ForegroundColor Green
        return $htmlReport
    } else {
        Write-Host "  [!] ghz failed for $Label" -ForegroundColor Red
        return $null
    }
}
