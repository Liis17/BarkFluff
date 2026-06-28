[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$IdentityHost,

    [Parameter(Mandatory)]
    [string]$Username,

    [Parameter(Mandatory)]
    [securestring]$Password,

    [int]$Concurrency = 50,

    [int]$TotalRequests = 5000,

    [string]$Duration,

    [int]$Rps = 0,

    [string]$ProtoPath = "$PSScriptRoot\..\..\Shared\BarkFluff.Proto",

    [string]$OutputDir = "$PSScriptRoot\reports",

    [switch]$UseTls
)

. "$PSScriptRoot\common.ps1"

$plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
)

Write-Host ""
Write-Host "============================================" -ForegroundColor Red
Write-Host "  Auth Stress Test" -ForegroundColor Red
Write-Host "  Identity.Auth login flood" -ForegroundColor Red
Write-Host "============================================" -ForegroundColor Red
Write-Host ""

if (-not (Get-Command ghz -ErrorAction SilentlyContinue)) {
    Write-Host "[!] ghz not found" -ForegroundColor Red
    exit 1
}

$headers = Get-XAuthHeaders

$ghzConfig = @{
    proto       = (Join-Path $ProtoPath "identity_api.proto")
    call        = "barkfluff.identity.IdentityApi/Auth"
    host        = $IdentityHost
    insecure    = (-not $UseTls)
    concurrency = $Concurrency
    data        = @{ username = $Username; password = $plainPassword }
    metadata    = @{
        "x-device-id"    = $headers.deviceId
        "x-device-name"  = $headers.deviceName
        "x-ip-address"   = $headers.ip
        "x-os-name"      = $headers.os
        "x-app-name"     = $headers.appName
        "x-app-version"  = $headers.appVersion
    }
}

if ($Duration) {
    $ghzConfig["duration"] = $Duration
} else {
    $ghzConfig["total"] = $TotalRequests
}
if ($Rps -gt 0) {
    $ghzConfig["rps"] = $Rps
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$configPath = Join-Path $OutputDir "ghz-auth-stress.json"
$json = $ghzConfig | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($configPath, $json, (New-Object System.Text.UTF8Encoding $false))

$loadDesc = if ($Duration) { "duration=$Duration" } else { "total=$TotalRequests" }
Write-Host "  Target: IdentityApi/Auth" -ForegroundColor Gray
Write-Host "  Load: $loadDesc, concurrency=$Concurrency$(if ($Rps -gt 0) { ", rps=$Rps" })" -ForegroundColor Gray
Write-Host ""

$report = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "auth-stress"

if ($report) {
    Write-Host ""
    Write-Host "  Report: $report" -ForegroundColor Green
    Start-Process $report
}
