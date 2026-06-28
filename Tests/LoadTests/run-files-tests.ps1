[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$IdentityHost,

    [Parameter(Mandatory)]
    [string]$FilesHost,

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
Write-Host "============================================" -ForegroundColor DarkYellow
Write-Host "  Files Service Load Tests" -ForegroundColor DarkYellow
Write-Host "============================================" -ForegroundColor DarkYellow
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

$reports = @()
$protoFile = Join-Path $ProtoPath "files_api.proto"

# --- Test 1: GetUserStorageInfo ---
Write-Host ""
Write-Host "[Test 1/3] GetUserStorageInfo..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-files-storage.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.files.FilesApi/GetUserStorageInfo" -Host_ $FilesHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{} -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "files-storage"
if ($r) { $reports += "GetUserStorageInfo: $r" }

# --- Test 2: ListStickerPacks ---
Write-Host ""
Write-Host "[Test 2/3] ListStickerPacks..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-files-stickers.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.files.FilesApi/ListStickerPacks" -Host_ $FilesHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{ pagination = @{ offset = 0; size = 20 } } -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "files-stickers"
if ($r) { $reports += "ListStickerPacks: $r" }

# --- Test 3: CheckFileHash ---
Write-Host ""
Write-Host "[Test 3/3] CheckFileHash..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
$configPath = Join-Path $OutputDir "ghz-files-hash.json"
Write-GhzConfig -ConfigPath $configPath -ProtoFile $protoFile `
    -Call "barkfluff.files.FilesApi/CheckFileHash" -Host_ $FilesHost `
    -Insecure:(-not $UseTls) -Concurrency $Concurrency -Total $TotalRequests -Duration $Duration -Rps 0 `
    -Data @{ fileHash = "0000000000000000000000000000000000000000000000000000000000000000" } -Token $auth.token -Headers $headers
$r = Run-GhzTest -ConfigPath $configPath -OutputDir $OutputDir -Label "files-hash"
if ($r) { $reports += "CheckFileHash: $r" }

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Files tests completed!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
foreach ($r in $reports) {
    Write-Host "  $r" -ForegroundColor White
}
