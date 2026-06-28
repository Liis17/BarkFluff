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

    [int64]$PeerUserId,

    [int]$Concurrency = 50,

    [int]$TotalRequests = 5000,

    [string]$Duration,

    [int]$Rps = 0,

    [string]$ProtoPath = "$PSScriptRoot\..\..\Shared\BarkFluff.Proto",

    [string]$OutputDir = "$PSScriptRoot\reports",

    [string]$MessageText = "Load test message from ghz",

    [switch]$UseTls
)

. "$PSScriptRoot\common.ps1"

$plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
)

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  BarkFluff Load Test - SendMessage" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$missing = @()
if (-not (Get-Command grpcurl -ErrorAction SilentlyContinue)) { $missing += "grpcurl" }
if (-not (Get-Command ghz -ErrorAction SilentlyContinue)) { $missing += "ghz" }

if ($missing.Count -gt 0) {
    Write-Host "[!] Missing tools: $($missing -join ', ')" -ForegroundColor Red
    exit 1
}

$headers = Get-XAuthHeaders
$baseArgs = Get-GrpcBaseArgs -ProtoPath $ProtoPath -Headers $headers -UseTls:$UseTls

Write-Host "[1/4] Authenticating..." -ForegroundColor Yellow
$token = Get-AuthToken -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
Write-Host "  Token obtained" -ForegroundColor Green

$authArgs = $baseArgs + @("-H", "x-auth-token: $token")

Write-Host ""
Write-Host "[2/4] Resolving chat target..." -ForegroundColor Yellow

if (-not $ChatId -and $PeerUserId) {
    Write-Host "  Resolving chat_id for user_id=$PeerUserId..." -ForegroundColor Gray
    $chatJson = "{`"userId`": $PeerUserId}"
    $chatResult = Invoke-Grpcurl -JsonBody $chatJson -GArgs ($authArgs + @(
        "-proto", "messages_api.proto",
        "$MessagesHost",
        "barkfluff.messages.MessagesApi/GetPersonChatId"
    ))
    $chatData = $chatResult | ConvertFrom-Json
    $ChatId = $chatData.chatId
    Write-Host "  Chat ID: $ChatId" -ForegroundColor Green
}

if (-not $ChatId -and -not $PeerUserId) {
    Write-Host "  No ChatId specified. Listing chats..." -ForegroundColor Gray
    $listJson = '{"pagination": {"offset": 0, "size": 1}}'
    $chatsResult = Invoke-Grpcurl -JsonBody $listJson -GArgs ($authArgs + @(
        "-proto", "messages_api.proto",
        "$MessagesHost",
        "barkfluff.messages.MessagesApi/ListChats"
    ))
    $chatsData = $chatsResult | ConvertFrom-Json
    if ($chatsData.chats -and $chatsData.chats.Count -gt 0) {
        $ChatId = $chatsData.chats[0].id
        Write-Host "  Using first chat: $ChatId" -ForegroundColor Green
    }

    if (-not $ChatId) {
        Write-Host "[!] No chats found. Specify -ChatId or -PeerUserId" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "[3/4] Generating ghz config..." -ForegroundColor Yellow

$requestData = if ($ChatId) {
    @{ chat_id = $ChatId; message = @{ text = $MessageText } }
} else {
    @{ user_id = $PeerUserId; message = @{ text = $MessageText } }
}

$configPath = Join-Path $OutputDir "ghz-config.json"
Write-GhzConfig -ConfigPath $configPath `
    -ProtoFile (Join-Path $ProtoPath "messages_api.proto") `
    -Call "barkfluff.messages.MessagesApi/SendMessage" `
    -Host_ $MessagesHost `
    -Insecure:(-not $UseTls) `
    -Concurrency $Concurrency `
    -Total $TotalRequests `
    -Duration $Duration `
    -Rps $Rps `
    -Data $requestData `
    -Token $token `
    -Headers $headers

$target = if ($ChatId) { "chat_id=$ChatId" } else { "user_id=$PeerUserId" }
$loadDesc = if ($Duration) { "duration=$Duration" } else { "total=$TotalRequests requests" }

Write-Host ""
Write-Host "[4/4] Running load test..." -ForegroundColor Yellow
Write-Host "  Target: SendMessage($target)" -ForegroundColor Gray
Write-Host "  Load: $loadDesc, concurrency=$Concurrency$(if ($Rps -gt 0) { ", rps=$Rps" })" -ForegroundColor Gray
Write-Host ""

$report = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "sendmessage"

if ($report) {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "  Load test completed!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  HTML report: $report" -ForegroundColor White
    Write-Host ""
    Write-Host "  Opening report..." -ForegroundColor Gray
    Start-Process $report
}
else {
    Write-Host ""
    Write-Host "[!] Load test failed." -ForegroundColor Red
    exit 1
}
