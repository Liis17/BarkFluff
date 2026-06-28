[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$IdentityHost,

    [Parameter(Mandatory)]
    [string]$Username,

    [Parameter(Mandatory)]
    [securestring]$Password,

    [int]$Concurrency = 50,

    [int]$TotalRequests = 3000,

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
Write-Host "============================================" -ForegroundColor DarkCyan
Write-Host "  Identity Service Load Tests" -ForegroundColor DarkCyan
Write-Host "  (non-auth methods)" -ForegroundColor DarkCyan
Write-Host "============================================" -ForegroundColor DarkCyan
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

$idArgs = $auth.authArgs + @("-proto", "identity_api.proto")

Write-Host ""
Write-Host "[Discovery] Getting refresh token..." -ForegroundColor Yellow
$authBody = @{ username = $Username; password = $plainPassword } | ConvertTo-Json -Compress
$authResult = Invoke-Grpcurl -JsonBody $authBody -GArgs ($baseArgs + @(
    "-proto", "identity_api.proto", "$IdentityHost", "barkfluff.identity.IdentityApi/Auth"
))
$authData = $authResult | ConvertFrom-Json
$refreshToken = $authData.refreshToken.value
if ($refreshToken) {
    Write-Host "  Refresh token obtained" -ForegroundColor Green
}

$reports = @()
$protoFile = Join-Path $ProtoPath "identity_api.proto"

# --- Test 1: CreateToken (refresh flow) ---
if ($refreshToken) {
    Write-Host ""
    Write-Host "[Test 1/2] CreateToken (refresh token flow)..." -ForegroundColor Yellow
    $auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
    $configPath = Join-Path $OutputDir "ghz-identity-refreshtoken.json"
    Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
        -Call "barkfluff.identity.IdentityApi/CreateToken" -Host_ $IdentityHost `
        -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps $Rps `
        -Data @{ refreshToken = $refreshToken } -Token $auth.token -Headers $headers
    $r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "identity-refreshtoken"
    if ($r) { $reports += "CreateToken: $r" }
} else {
    Write-Host "[Test 1/2] CreateToken - SKIPPED (no refresh token)" -ForegroundColor Gray
}

# --- Test 2: GetActiveSessions ---
Write-Host ""
Write-Host "[Test 2/2] GetActiveSessions..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-identity-sessions.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.identity.IdentityApi/GetActiveSessions" -Host_ $IdentityHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps $Rps `
    -Data @{} -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "identity-sessions"
if ($r) { $reports += "GetActiveSessions: $r" }

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Identity tests completed!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
foreach ($r in $reports) {
    Write-Host "  $r" -ForegroundColor White
}
