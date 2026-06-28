[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$IdentityHost,

    [Parameter(Mandatory)]
    [string]$MessagesHost,

    [Parameter(Mandatory)]
    [string]$UsersHost,

    [Parameter(Mandatory)]
    [string]$OnlinerHost,

    [Parameter(Mandatory)]
    [string]$Username,

    [Parameter(Mandatory)]
    [securestring]$Password,

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
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  BarkFluff Read-Only Load Tests" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
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

Write-Host "[Auth] Initial authentication..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
Write-Host "  Token obtained" -ForegroundColor Green

Write-Host ""
Write-Host "[Discovery] Finding test data..." -ForegroundColor Yellow

$listJson = '{"pagination": {"offset": 0, "size": 1}}'
$chatsResult = Invoke-Grpcurl -JsonBody $listJson -GArgs ($auth.authArgs + @(
    "-proto", "messages_api.proto",
    "$MessagesHost",
    "barkfluff.messages.MessagesApi/ListChats"
))
$chatsData = $chatsResult | ConvertFrom-Json
$chatId = $null
if ($chatsData.chats -and $chatsData.chats.Count -gt 0) {
    $chatId = $chatsData.chats[0].id
    Write-Host "  Chat: $chatId" -ForegroundColor Green
}

$userId = $null
$getUserResult = Invoke-Grpcurl -JsonBody "{}" -GArgs ($auth.authArgs + @(
    "-proto", "users_api.proto",
    "$UsersHost",
    "barkfluff.users.UsersApi/GetDevices"
))
$getUserData = $getUserResult | ConvertFrom-Json
if ($getUserData.devices -and $getUserData.devices.Count -gt 0) {
    $userId = $getUserData.devices[0].userId
    Write-Host "  User ID: $userId" -ForegroundColor Green
}

$reports = @()
$testNum = 0
$totalTests = 6

# --- Test 1: ListChats ---
$testNum++
Write-Host ""
Write-Host "[$testNum/$totalTests] ListChats..." -ForegroundColor Yellow
Write-Host "  Re-authenticating..." -ForegroundColor Gray
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-listchats.json"
Write-GhzConfig -ConfigPath $configPath `
    -ProtoFile (Join-Path $ProtoPath "messages_api.proto") `
    -Call "barkfluff.messages.MessagesApi/ListChats" `
    -Host_ $MessagesHost `
    -Insecure:(-not $UseTls) `
    -Concurrency $Concurrency `
    -Total $TotalRequests `
    -Duration $Duration `
    -Rps 0 `
    -Data @{ pagination = @{ offset = 0; size = 20 } } `
    -Token $auth.token `
    -Headers $headers
$report = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "listchats"
if ($report) { $reports += "ListChats: $report" }

# --- Test 2: ListMessages ---
$testNum++
if ($chatId) {
    Write-Host ""
    Write-Host "[$testNum/$totalTests] ListMessages..." -ForegroundColor Yellow
    Write-Host "  Re-authenticating..." -ForegroundColor Gray
    $auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
    $configPath = Join-Path $OutputDir "ghz-listmessages.json"
    Write-GhzConfig -ConfigPath $configPath `
        -ProtoFile (Join-Path $ProtoPath "messages_api.proto") `
        -Call "barkfluff.messages.MessagesApi/ListMessages" `
        -Host_ $MessagesHost `
        -Insecure:(-not $UseTls) `
        -Concurrency $Concurrency `
        -Total $TotalRequests `
        -Duration $Duration `
        -Rps 0 `
        -Data @{ chat_id = $chatId; from_message_id = 0; offset_before = 20 } `
        -Token $auth.token `
        -Headers $headers
    $report = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "listmessages"
    if ($report) { $reports += "ListMessages: $report" }
} else {
    Write-Host "[$testNum/$totalTests] ListMessages - SKIPPED (no chat_id)" -ForegroundColor Gray
}

# --- Test 3: GetChatInfo ---
$testNum++
if ($chatId) {
    Write-Host ""
    Write-Host "[$testNum/$totalTests] GetChatInfo..." -ForegroundColor Yellow
    Write-Host "  Re-authenticating..." -ForegroundColor Gray
    $auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
    $configPath = Join-Path $OutputDir "ghz-getchatinfo.json"
    Write-GhzConfig -ConfigPath $configPath `
        -ProtoFile (Join-Path $ProtoPath "messages_api.proto") `
        -Call "barkfluff.messages.MessagesApi/GetChatInfo" `
        -Host_ $MessagesHost `
        -Insecure:(-not $UseTls) `
        -Concurrency $Concurrency `
        -Total $TotalRequests `
        -Duration $Duration `
        -Rps 0 `
        -Data @{ chat_id = $chatId } `
        -Token $auth.token `
        -Headers $headers
    $report = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "getchatinfo"
    if ($report) { $reports += "GetChatInfo: $report" }
} else {
    Write-Host "[$testNum/$totalTests] GetChatInfo - SKIPPED (no chat_id)" -ForegroundColor Gray
}

# --- Test 4: GetUser ---
$testNum++
if ($userId) {
    Write-Host ""
    Write-Host "[$testNum/$totalTests] GetUser..." -ForegroundColor Yellow
    Write-Host "  Re-authenticating..." -ForegroundColor Gray
    $auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
    $configPath = Join-Path $OutputDir "ghz-getuser.json"
    Write-GhzConfig -ConfigPath $configPath `
        -ProtoFile (Join-Path $ProtoPath "users_api.proto") `
        -Call "barkfluff.users.UsersApi/GetUser" `
        -Host_ $UsersHost `
        -Insecure:(-not $UseTls) `
        -Concurrency $Concurrency `
        -Total $TotalRequests `
        -Duration $Duration `
        -Rps 0 `
        -Data @{ user_id = $userId } `
        -Token $auth.token `
        -Headers $headers
    $report = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "getuser"
    if ($report) { $reports += "GetUser: $report" }
} else {
    Write-Host "[$testNum/$totalTests] GetUser - SKIPPED (no user_id)" -ForegroundColor Gray
}

# --- Test 5: SearchUsers ---
$testNum++
Write-Host ""
Write-Host "[$testNum/$totalTests] SearchUsers..." -ForegroundColor Yellow
Write-Host "  Re-authenticating..." -ForegroundColor Gray
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-searchusers.json"
Write-GhzConfig -ConfigPath $configPath `
    -ProtoFile (Join-Path $ProtoPath "users_api.proto") `
    -Call "barkfluff.users.UsersApi/SearchUsers" `
    -Host_ $UsersHost `
    -Insecure:(-not $UseTls) `
    -Concurrency $Concurrency `
    -Total $TotalRequests `
    -Duration $Duration `
    -Rps 0 `
    -Data @{ query = "a"; pagination = @{ offset = 0; size = 20 } } `
    -Token $auth.token `
    -Headers $headers
$report = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "searchusers"
if ($report) { $reports += "SearchUsers: $report" }

# --- Test 6: GetOnlineStatus ---
$testNum++
if ($userId) {
    Write-Host ""
    Write-Host "[$testNum/$totalTests] GetOnlineStatus..." -ForegroundColor Yellow
    Write-Host "  Re-authenticating..." -ForegroundColor Gray
    $auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
    $configPath = Join-Path $OutputDir "ghz-onlinestatus.json"
    Write-GhzConfig -ConfigPath $configPath `
        -ProtoFile (Join-Path $ProtoPath "onliner_api.proto") `
        -Call "barkfluff.onliner.OnlinerApi/GetOnlineStatus" `
        -Host_ $OnlinerHost `
        -Insecure:(-not $UseTls) `
        -Concurrency $Concurrency `
        -Total $TotalRequests `
        -Duration $Duration `
        -Rps 0 `
        -Data @{ user_ids = @($userId) } `
        -Token $auth.token `
        -Headers $headers
    $report = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "onlinestatus"
    if ($report) { $reports += "GetOnlineStatus: $report" }
} else {
    Write-Host "[$testNum/$totalTests] GetOnlineStatus - SKIPPED (no user_id)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  All read-only tests completed!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
foreach ($r in $reports) {
    Write-Host "  $r" -ForegroundColor White
}
