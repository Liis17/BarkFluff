[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$IdentityHost,

    [Parameter(Mandatory)]
    [string]$UpdatesHost,

    [Parameter(Mandatory)]
    [string]$Username,

    [Parameter(Mandatory)]
    [securestring]$Password,

    [int]$Connections = 100,

    [int]$DurationSeconds = 60,

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
Write-Host "  BarkFluff Stream Stress Test" -ForegroundColor Magenta
Write-Host "  SubscribeNewMessages (server streaming)" -ForegroundColor Magenta
Write-Host "============================================" -ForegroundColor Magenta
Write-Host ""

if (-not (Get-Command grpcurl -ErrorAction SilentlyContinue)) {
    Write-Host "[!] grpcurl not found" -ForegroundColor Red
    exit 1
}

$headers = Get-XAuthHeaders
$baseArgs = Get-GrpcBaseArgs -ProtoPath $ProtoPath -Headers $headers -UseTls:$UseTls

Write-Host "[Auth] Authenticating..." -ForegroundColor Yellow
$token = Get-AuthToken -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
Write-Host "  Token obtained" -ForegroundColor Green

$authArgs = $baseArgs + @(
    "-proto", "updates_api.proto",
    "-H", "x-auth-token: $token"
)

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host ""
Write-Host "Starting $Connections concurrent streaming connections..." -ForegroundColor Yellow
Write-Host "Duration: ${DurationSeconds}s" -ForegroundColor Gray
Write-Host ""

$flagArgs = @()
$posArgs = @()
for ($i = 0; $i -lt $authArgs.Count; $i++) {
    if ($authArgs[$i] -match '^-') {
        $flagArgs += $authArgs[$i]
        if ($i + 1 -lt $authArgs.Count -and $authArgs[$i + 1] -notmatch '^-') {
            $i++
            if ($flagArgs[-1] -match '^-H$') {
                $flagArgs += "`"$($authArgs[$i])`""
            } else {
                $flagArgs += $authArgs[$i]
            }
        }
    } else {
        $posArgs += $authArgs[$i]
    }
}
$flagStr = $flagArgs -join ' '
$posStr = $posArgs -join ' '

$tmpJson = Join-Path $env:TEMP "grpcurl-stream-body-$(Get-Random).json"
[System.IO.File]::WriteAllText($tmpJson, "{}", (New-Object System.Text.UTF8Encoding $false))

$batContent = "@echo off`ngrpcurl $flagStr -d @ $posStr < `"$tmpJson`"`n"
$tmpBat = Join-Path $env:TEMP "grpcurl-stream-cmd-$(Get-Random).bat"
[System.IO.File]::WriteAllText($tmpBat, $batContent, [System.Text.Encoding]::ASCII)

$scriptBlock = {
    param($bat)
    $proc = Start-Process -FilePath "cmd.exe" -ArgumentList "/c", $bat `
        -NoNewWindow -PassThru `
        -RedirectStandardOutput "NUL" `
        -RedirectStandardError "NUL"
    return $proc
}

$jobs = @()
$successCount = 0
$failCount = 0
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "Opening connections..." -ForegroundColor Yellow
for ($i = 0; $i -lt $Connections; $i++) {
    try {
        $proc = & $scriptBlock $tmpBat
        if ($proc -and -not $proc.HasExited) {
            $jobs += $proc
            $successCount++
        } else {
            $failCount++
        }
    }
    catch {
        $failCount++
    }

    if (($i + 1) % 25 -eq 0) {
        Write-Host "  Opened $($i + 1)/$Connections (alive: $successCount, failed: $failCount)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "  Connected: $successCount, Failed: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Yellow" } else { "Green" })
Write-Host ""
Write-Host "Holding connections for ${DurationSeconds}s..." -ForegroundColor Yellow

for ($s = $DurationSeconds; $s -gt 0; $s -= 5) {
    $aliveCount = ($jobs | Where-Object { -not $_.HasExited }).Count
    $deadCount = ($jobs | Where-Object { $_.HasExited }).Count
    Write-Host "  [$($DurationSeconds - $s)s] Alive: $aliveCount, Dropped: $deadCount" -ForegroundColor Gray
    Start-Sleep -Seconds ([Math]::Min($s, 5))
}

$stopwatch.Stop()

$aliveAfter = ($jobs | Where-Object { -not $_.HasExited }).Count
$droppedDuring = ($jobs | Where-Object { $_.HasExited }).Count

Write-Host ""
Write-Host "Stopping all connections..." -ForegroundColor Yellow
$jobs | Where-Object { -not $_.HasExited } | ForEach-Object { $_.Kill() }
$jobs | ForEach-Object { $_.Dispose() }

if (Test-Path $tmpJson) { Remove-Item $tmpJson -Force -ErrorAction SilentlyContinue }
if (Test-Path $tmpBat) { Remove-Item $tmpBat -Force -ErrorAction SilentlyContinue }

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$reportPath = Join-Path $OutputDir "report-stream-$timestamp.json"
$report = @{
    test            = "SubscribeNewMessages"
    target_hosts    = $Connections
    connected       = $successCount
    failed_connect  = $failCount
    alive_after     = $aliveAfter
    dropped_during  = $droppedDuring
    duration_s      = $DurationSeconds
    elapsed_ms      = $stopwatch.ElapsedMilliseconds
    drop_rate_pct   = if ($successCount -gt 0) { [Math]::Round(($droppedDuring / $successCount) * 100, 2) } else { 0 }
}
$report | ConvertTo-Json | Out-File -FilePath $reportPath -Encoding UTF8

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Stream stress test completed!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Target connections:  $Connections" -ForegroundColor White
Write-Host "  Successfully opened: $successCount" -ForegroundColor White
Write-Host "  Still alive:         $aliveAfter" -ForegroundColor $(if ($aliveAfter -eq $successCount) { "Green" } else { "Yellow" })
Write-Host "  Dropped:             $droppedDuring" -ForegroundColor $(if ($droppedDuring -eq 0) { "Green" } else { "Red" })
Write-Host "  Drop rate:           $($report.drop_rate_pct)%" -ForegroundColor White
Write-Host "  Report:              $reportPath" -ForegroundColor White
