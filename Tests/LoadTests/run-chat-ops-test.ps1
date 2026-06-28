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

    [int]$TotalRequests = 3000,

    [string]$Duration,

    [string]$ProtoPath = "$PSScriptRoot\..\..\Shared\BarkFluff.Proto",

    [string]$OutputDir = "$PSScriptRoot\reports",

    [switch]$UseTls
)

. "$PSScriptRoot\common.ps1"

$plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
)

Write-Host ""
Write-Host "============================================" -ForegroundColor Blue
Write-Host "  Chat Operations Test" -ForegroundColor Blue
Write-Host "  Mixed read operations on a single chat" -ForegroundColor Blue
Write-Host "============================================" -ForegroundColor Blue
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

Write-Host "[Init] Authenticating + resolving chat..." -ForegroundColor Yellow
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

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$reports = @()
$protoFile = Join-Path $ProtoPath "messages_api.proto"

# --- Test 1: GetChatInfo ---
Write-Host ""
Write-Host "[Test 1/4] GetChatInfo..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-chatops-getchatinfo.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.messages.MessagesApi/GetChatInfo" -Host_ $MessagesHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{ chatId = $ChatId } -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "chatops-getchatinfo"
if ($r) { $reports += "GetChatInfo: $r" }

# --- Test 2: ListChatMembers ---
Write-Host ""
Write-Host "[Test 2/4] ListChatMembers..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-chatops-members.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.messages.MessagesApi/ListChatMembers" -Host_ $MessagesHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{ chatId = $ChatId; pagination = @{ offset = 0; size = 50 } } -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "chatops-members"
if ($r) { $reports += "ListChatMembers: $r" }

# --- Test 3: ListPinnedMessages ---
Write-Host ""
Write-Host "[Test 3/4] ListPinnedMessages..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-chatops-pinned.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.messages.MessagesApi/ListPinnedMessages" -Host_ $MessagesHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{ chatId = $ChatId; pagination = @{ offset = 0; size = 20 } } -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "chatops-pinned"
if ($r) { $reports += "ListPinnedMessages: $r" }

# --- Test 4: ListChatAttachments ---
Write-Host ""
Write-Host "[Test 4/4] ListChatAttachments..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-chatops-attachments.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.messages.MessagesApi/ListChatAttachments" -Host_ $MessagesHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{ chatId = $ChatId; pagination = @{ offset = 0; size = 20 } } -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "chatops-attachments"
if ($r) { $reports += "ListChatAttachments: $r" }

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Chat ops tests completed!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
foreach ($r in $reports) {
    Write-Host "  $r" -ForegroundColor White
}
