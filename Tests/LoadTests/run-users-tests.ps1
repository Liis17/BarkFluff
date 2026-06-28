[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$IdentityHost,

    [Parameter(Mandatory)]
    [string]$UsersHost,

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
Write-Host "============================================" -ForegroundColor Magenta
Write-Host "  Users Service Load Tests" -ForegroundColor Magenta
Write-Host "============================================" -ForegroundColor Magenta
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

Write-Host "[Init] Authenticating..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
Write-Host "  Token obtained" -ForegroundColor Green

$authArgs = $auth.authArgs + @("-proto", "users_api.proto")

Write-Host ""
Write-Host "[Discovery] Getting user ID..." -ForegroundColor Yellow
$devResult = Invoke-Grpcurl -JsonBody "{}" -GArgs ($authArgs + @("$UsersHost", "barkfluff.users.UsersApi/GetDevices"))
$devData = $devResult | ConvertFrom-Json
$userId = $null
if ($devData.devices -and $devData.devices.Count -gt 0) {
    $userId = $devData.devices[0].userId
    Write-Host "  User ID: $userId" -ForegroundColor Green
}

$reports = @()
$protoFile = Join-Path $ProtoPath "users_api.proto"

# --- Test 1: GetUser ---
if ($userId) {
    Write-Host ""
    Write-Host "[Test 1/7] GetUser..." -ForegroundColor Yellow
    $auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
    $configPath = Join-Path $OutputDir "ghz-users-getuser.json"
    Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
        -Call "barkfluff.users.UsersApi/GetUser" -Host_ $UsersHost `
        -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
        -Data @{ userId = $userId } -Token $auth.token -Headers $headers
    $r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "users-getuser"
    if ($r) { $reports += "GetUser: $r" }
}

# --- Test 2: SearchUsers ---
Write-Host ""
Write-Host "[Test 2/7] SearchUsers..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-users-search.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.users.UsersApi/SearchUsers" -Host_ $UsersHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{ query = "a"; pagination = @{ offset = 0; size = 20 } } -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "users-search"
if ($r) { $reports += "SearchUsers: $r" }

# --- Test 3: CheckExistUsername ---
Write-Host ""
Write-Host "[Test 3/7] CheckExistUsername..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-users-checkusername.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.users.UsersApi/CheckExistUsername" -Host_ $UsersHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{ username = $Username } -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "users-checkusername"
if ($r) { $reports += "CheckExistUsername: $r" }

# --- Test 4: GetDevices ---
Write-Host ""
Write-Host "[Test 4/7] GetDevices..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-users-devices.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.users.UsersApi/GetDevices" -Host_ $UsersHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{} -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "users-devices"
if ($r) { $reports += "GetDevices: $r" }

# --- Test 5: GetPrivacySettings ---
Write-Host ""
Write-Host "[Test 5/7] GetPrivacySettings..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-users-privacy.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.users.UsersApi/GetPrivacySettings" -Host_ $UsersHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{} -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "users-privacy"
if ($r) { $reports += "GetPrivacySettings: $r" }

# --- Test 6: GetPersonalization ---
Write-Host ""
Write-Host "[Test 6/7] GetPersonalization..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-users-personalization.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.users.UsersApi/GetPersonalization" -Host_ $UsersHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{} -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "users-personalization"
if ($r) { $reports += "GetPersonalization: $r" }

# --- Test 7: GetChatFolders ---
Write-Host ""
Write-Host "[Test 7/7] GetChatFolders..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-users-chatfolders.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.users.UsersApi/GetChatFolders" -Host_ $UsersHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{} -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "users-chatfolders"
if ($r) { $reports += "GetChatFolders: $r" }

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Users tests completed!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
foreach ($r in $reports) {
    Write-Host "  $r" -ForegroundColor White
}
