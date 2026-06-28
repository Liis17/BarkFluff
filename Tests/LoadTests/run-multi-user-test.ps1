param(
    [Parameter(Mandatory)]
    [string]$IdentityHost,

    [Parameter(Mandatory)]
    [string]$MessagesHost,

    [Parameter(Mandatory)]
    [string]$Username,

    [Parameter(Mandatory)]
    [securestring]$Password,

    [int32]$NumUsers = 10,

    [int]$ConcurrencyPerUser = 5,

    [int]$TotalRequestsPerUser = 500,

    [string]$ProtoPath = "$PSScriptRoot\..\..\Shared\BarkFluff.Proto",

    [string]$OutputDir = "$PSScriptRoot\reports",

    [string]$MessageText = "Multi-user load test message",

    [switch]$SkipAuth
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================" -ForegroundColor Magenta
Write-Host "  BarkFluff Multi-User Load Test" -ForegroundColor Magenta
Write-Host "============================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "  Users: $NumUsers" -ForegroundColor Gray
Write-Host "  Concurrency per user: $ConcurrencyPerUser" -ForegroundColor Gray
Write-Host "  Requests per user: $TotalRequestsPerUser" -ForegroundColor Gray
Write-Host "  Total requests: $($NumUsers * $TotalRequestsPerUser)" -ForegroundColor Gray
Write-Host ""

$plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
)

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$deviceId = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([guid]::NewGuid().ToString()))
$deviceName = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("LoadTest-Multi"))
$ip = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("127.0.0.1"))
$os = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("LoadTest"))
$appName = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("BarkFluff.LoadTest"))
$appVersion = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("1.0.0"))

$commonHeaders = @(
    "-H", "x-device-id: $deviceId",
    "-H", "x-device-name: $deviceName",
    "-H", "x-ip: $ip",
    "-H", "x-os: $os",
    "-H", "x-app-name: $appName",
    "-H", "x-app-version: $appVersion"
)

Write-Host "[1/3] Authenticating user..." -ForegroundColor Yellow

$authBody = @{ username = $Username; password = $plainPassword } | ConvertTo-Json -Compress
$authResult = & grpcurl -plaintext -import-path $ProtoPath -proto "identity_api.proto" @commonHeaders -d $authBody "$IdentityHost" "barkfluff.identity.IdentityApi/Auth" 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "[!] Auth failed" -ForegroundColor Red
    Write-Host $authResult
    exit 1
}

$token = ($authResult | ConvertFrom-Json).access_token.value
Write-Host "  Token obtained" -ForegroundColor Green

Write-Host ""
Write-Host "[2/3] Discovering chats..." -ForegroundColor Yellow

$chatsResult = & grpcurl -plaintext -import-path $ProtoPath -proto "messages_api.proto" `
    @commonHeaders `
    -H "x-auth-token: $token" `
    -d '{"pagination": {"offset": 0, "size": 50}}' `
    "$MessagesHost" "barkfluff.messages.MessagesApi/ListChats" 2>&1

$chatIds = @()
if ($LASTEXITCODE -eq 0) {
    $chatsJson = $chatsResult | ConvertFrom-Json
    foreach ($chat in $chatsJson.chats) {
        $chatIds += $chat.id
    }
    Write-Host "  Found $($chatIds.Count) chats" -ForegroundColor Green
}

if ($chatIds.Count -eq 0) {
    Write-Host "[!] No chats available for testing" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "[3/3] Launching concurrent ghz workers..." -ForegroundColor Yellow

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$jobs = @()
$actualUsers = [Math]::Min($NumUsers, $chatIds.Count)

for ($i = 0; $i -lt $actualUsers; $i++) {
    $chatId = $chatIds[$i % $chatIds.Count]

    $ghzConfig = @{
        proto       = (Join-Path $ProtoPath "messages_api.proto")
        call        = "barkfluff.messages.MessagesApi/SendMessage"
        host        = $MessagesHost
        insecure    = $true
        concurrency = $ConcurrencyPerUser
        total       = $TotalRequestsPerUser
        data        = @{
            chat_id = $chatId
            message = @{ text = "$MessageText (user-$i)" }
        }
        metadata    = @{
            "x-auth-token"  = $token
            "x-device-id"   = $deviceId
            "x-device-name" = $deviceName
            "x-ip"          = $ip
            "x-os"          = $os
            "x-app-name"    = $appName
            "x-app-version" = $appVersion
        }
    }

    $configFile = Join-Path $OutputDir "ghz-multi-user-$i.json"
    $ghzConfig | ConvertTo-Json -Depth 5 | Set-Content -Path $configFile -Encoding UTF8

    $userReport = Join-Path $OutputDir "report-multi-user-$i-$timestamp.json"

    $scriptBlock = {
        param($cfg, $report)
        & ghz -c $cfg -o "json=$report"
    }

    Write-Host "  Starting worker $i -> chat $chatId" -ForegroundColor Gray

    $jobs += Start-Job -ScriptBlock $scriptBlock -ArgumentList $configFile, $userReport
}

Write-Host ""
Write-Host "  Waiting for $($jobs.Count) workers to complete..." -ForegroundColor Yellow

$results = $jobs | Wait-Job | Receive-Job

$failedCount = 0
$allLatencies = @()
$totalReqs = 0

foreach ($job in $jobs) {
    $idx = [array]::IndexOf($jobs, $job)
    if ($job.State -ne "Completed") {
        Write-Host "  [!] Worker $idx failed: $($job.ChildJobs[0].JobStateInfo.Reason)" -ForegroundColor Red
        $failedCount++
        continue
    }

    $userReport = Join-Path $OutputDir "report-multi-user-$idx-$timestamp.json"
    if (Test-Path $userReport) {
        $report = Get-Content $userReport -Raw | ConvertFrom-Json
        $totalReqs += $report.count
        Write-Host "  Worker $idx done: $($report.count) requests, avg=$($report.averageMs)ms, p99=$($report.fastestMs)ms" -ForegroundColor Green
    }
}

$jobs | Remove-Job -Force

$combinedReport = Join-Path $OutputDir "report-multi-combined-$timestamp.json"
@{
    total_requests = $totalReqs
    users          = $actualUsers
    failed_workers = $failedCount
    timestamp      = $timestamp
} | ConvertTo-Json | Set-Content -Path $combinedReport -Encoding UTF8

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Multi-user test completed!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Total requests sent: $totalReqs" -ForegroundColor White
Write-Host "  Failed workers: $failedCount" -ForegroundColor $(if ($failedCount -gt 0) { "Red" } else { "Green" })
Write-Host "  Combined report: $combinedReport" -ForegroundColor White
