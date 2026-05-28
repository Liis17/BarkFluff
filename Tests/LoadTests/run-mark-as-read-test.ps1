[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$IdentityHost,

    [Parameter(Mandatory)]
    [string]$MessagesHost,

    [Parameter(Mandatory)]
    [string]$Username,

    [Parameter(Mandatory)]
    [securestring]$Password,

    [string]$ChatId,

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
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "  MarkAsRead Flood Test" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
Write-Host ""

$missing = @()
if (-not (Get-Command grpcurl -ErrorAction SilentlyContinue)) { $missing += "grpcurl" }
if (-not (Get-Command ghz -ErrorAction SilentlyContinue)) { $missing += "ghz" }
if ($missing.Count -gt 0) {
    Write-Host "[!] Missing: $($missing -join ', ')" -ForegroundColor Red
    exit 1
}

$headers = Get-XAuthHeaders
$baseArgs = Get-GrpcBaseArgs -ProtoPath $ProtoPath -Headers $headers -UseTls:$UseTls

Write-Host "[1/2] Authenticating + resolving chat..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
Write-Host "  Token obtained" -ForegroundColor Green

$msgArgs = $auth.authArgs + @("-proto", "messages_api.proto")

if (-not $ChatId) {
    $listJson = '{"pagination": {"offset": 0, "size": 1}}'
    $chatsResult = Invoke-Grpcurl -JsonBody $listJson -GArgs ($msgArgs + @(
        "$MessagesHost", "barkfluff.messages.MessagesApi/ListChats"
    ))
    $chatsData = $chatsResult | ConvertFrom-Json
    if ($chatsData.chats -and $chatsData.chats.Count -gt 0) {
        $ChatId = $chatsData.chats[0].id
    }
    if (-not $ChatId) {
        Write-Host "[!] No chats found" -ForegroundColor Red
        exit 1
    }
}
Write-Host "  Chat: $ChatId" -ForegroundColor Green

$msgListResult = Invoke-Grpcurl -JsonBody "{`"chatId`":`"$ChatId`",`"fromMessageId`":0,`"offsetBefore`":10}" -GArgs ($msgArgs + @(
    "$MessagesHost", "barkfluff.messages.MessagesApi/ListMessages"
))
$msgListData = $msgListResult | ConvertFrom-Json
$messageIds = @()
if ($msgListData.messages) {
    foreach ($m in $msgListData.messages) {
        $messageIds += $m.id
    }
}
if ($messageIds.Count -eq 0) {
    $messageIds = @(1, 2, 3, 4, 5)
}
Write-Host "  Message IDs: $($messageIds -join ', ')" -ForegroundColor Gray

Write-Host ""
Write-Host "[2/2] Running MarkAsRead flood..." -ForegroundColor Yellow

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$configPath = Join-Path $OutputDir "ghz-markasread.json"
Write-GhzConfig -ConfigPath $configPath `
    -ProtoFile (Join-Path $ProtoPath "messages_api.proto") `
    -Call "barkfluff.messages.MessagesApi/MarkAsRead" `
    -Host_ $MessagesHost `
    -Insecure:(-not $UseTls) `
    -Concurrency $Concurrency `
    -Total $TotalRequests `
    -Duration $Duration `
    -Rps $Rps `
    -Data @{ messageIds = $messageIds } `
    -Token $auth.token `
    -Headers $headers

$loadDesc = if ($Duration) { "duration=$Duration" } else { "total=$TotalRequests" }
Write-Host "  Target: MarkAsRead($($messageIds -join ','))" -ForegroundColor Gray
Write-Host "  Load: $loadDesc, concurrency=$Concurrency$(if ($Rps -gt 0) { ", rps=$Rps" })" -ForegroundColor Gray
Write-Host ""

$report = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "markasread"

if ($report) {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "  MarkAsRead flood completed!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "  Report: $report" -ForegroundColor White
    Start-Process $report
}
