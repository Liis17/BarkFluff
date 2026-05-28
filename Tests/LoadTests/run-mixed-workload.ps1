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

    [int]$Iterations = 100,

    [string]$ProtoPath = "$PSScriptRoot\..\..\Shared\BarkFluff.Proto",

    [string]$OutputDir = "$PSScriptRoot\reports",

    [switch]$UseTls
)

. "$PSScriptRoot\common.ps1"

$plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
)

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Mixed Workload Test" -ForegroundColor Green
Write-Host "  ListChats -> ListMessages -> GetChatInfo -> SendMessage -> ListMessages" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

if (-not (Get-Command grpcurl -ErrorAction SilentlyContinue)) {
    Write-Host "[!] grpcurl not found" -ForegroundColor Red
    exit 1
}

$headers = Get-XAuthHeaders
$baseArgs = Get-GrpcBaseArgs -ProtoPath $ProtoPath -Headers $headers -UseTls:$UseTls

Write-Host "[1/2] Authenticating + resolving chat..." -ForegroundColor Yellow
$auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
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

Write-Host ""
Write-Host "[2/2] Running $Iterations iterations..." -ForegroundColor Yellow
Write-Host ""

$stats = @{}
$stepNames = @("ListChats", "ListMessages", "GetChatInfo", "SendMessage", "ListMessages2")
foreach ($s in $stepNames) { $stats[$s] = @{ total = 0; errors = 0; latencies = @() } }

$totalSw = [System.Diagnostics.Stopwatch]::StartNew()

for ($i = 0; $i -lt $Iterations; $i++) {
    if ($i % 10 -eq 0 -and $i -gt 0) {
        $auth = New-AuthArgs -IdentityHost $IdentityHost -Username $Username -PlainPassword $plainPassword -BaseArgs $baseArgs
        $msgArgs = $auth.authArgs + @("-proto", "messages_api.proto")
    }

    $steps = @(
        @{ name = "ListChats"; json = '{"pagination":{"offset":0,"size":20}}'; method = "barkfluff.messages.MessagesApi/ListChats" },
        @{ name = "ListMessages"; json = "{`"chatId`":`"$ChatId`",`"fromMessageId`":0,`"offsetBefore`":20}"; method = "barkfluff.messages.MessagesApi/ListMessages" },
        @{ name = "GetChatInfo"; json = "{`"chatId`":`"$ChatId`"}"; method = "barkfluff.messages.MessagesApi/GetChatInfo" },
        @{ name = "SendMessage"; json = "{`"chatId`":`"$ChatId`",`"message`":{`"text`":`"mixed-$i`"}}"; method = "barkfluff.messages.MessagesApi/SendMessage" },
        @{ name = "ListMessages2"; json = "{`"chatId`":`"$ChatId`",`"fromMessageId`":0,`"offsetBefore`":5}"; method = "barkfluff.messages.MessagesApi/ListMessages" }
    )

    foreach ($step in $steps) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            Invoke-Grpcurl -JsonBody $step.json -GArgs ($msgArgs + @("$MessagesHost", $step.method)) | Out-Null
            $sw.Stop()
            $stats[$step.name].total++
            $stats[$step.name].latencies += $sw.ElapsedMilliseconds
        }
        catch {
            $sw.Stop()
            $stats[$step.name].total++
            $stats[$step.name].errors++
            $stats[$step.name].latencies += $sw.ElapsedMilliseconds
        }
    }

    if (($i + 1) % 10 -eq 0) {
        $pct = [Math]::Round((($i + 1) / $Iterations) * 100)
        Write-Host "  [$($i + 1)/$Iterations] $pct%" -ForegroundColor Gray
    }
}

$totalSw.Stop()

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Mixed workload completed!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Iterations:      $Iterations ($($Iterations * 5) operations)" -ForegroundColor White
Write-Host "  Total time:      $($totalSw.Elapsed.ToString('mm\:ss'))" -ForegroundColor White
Write-Host ""

foreach ($s in $stepNames) {
    $sStats = $stats[$s]
    $avg = if ($sStats.latencies.Count -gt 0) { [Math]::Round(($sStats.latencies | Measure-Object -Average).Average) } else { 0 }
    $sorted = $sStats.latencies | Sort-Object
    $p50 = if ($sorted.Count -gt 0) { $sorted[[Math]::Floor($sorted.Count * 0.5)] } else { 0 }
    $p95 = if ($sorted.Count -gt 0) { $sorted[[Math]::Floor($sorted.Count * 0.95)] } else { 0 }
    $p99 = if ($sorted.Count -gt 0) { $sorted[[Math]::Min([Math]::Floor($sorted.Count * 0.99), $sorted.Count - 1)] } else { 0 }
    $errColor = if ($sStats.errors -gt 0) { "Red" } else { "Green" }

    Write-Host "  $s" -ForegroundColor Cyan
    Write-Host "    Requests: $($sStats.total)  Errors: $($sStats.errors)" -ForegroundColor $errColor
    Write-Host "    Avg: ${avg}ms  p50: ${p50}ms  p95: ${p95}ms  p99: ${p99}ms" -ForegroundColor White
    Write-Host ""
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$reportPath = Join-Path $OutputDir "report-mixed-$timestamp.json"
$reportData = @{}
foreach ($s in $stepNames) {
    $sStats = $stats[$s]
    $avg = if ($sStats.latencies.Count -gt 0) { [Math]::Round(($sStats.latencies | Measure-Object -Average).Average) } else { 0 }
    $sorted = $sStats.latencies | Sort-Object
    $p50 = if ($sorted.Count -gt 0) { $sorted[[Math]::Floor($sorted.Count * 0.5)] } else { 0 }
    $p95 = if ($sorted.Count -gt 0) { $sorted[[Math]::Floor($sorted.Count * 0.95)] } else { 0 }
    $p99 = if ($sorted.Count -gt 0) { $sorted[[Math]::Min([Math]::Floor($sorted.Count * 0.99), $sorted.Count - 1)] } else { 0 }
    $reportData[$s] = @{ total = $sStats.total; errors = $sStats.errors; avg_ms = $avg; p50_ms = $p50; p95_ms = $p95; p99_ms = $p99 }
}
$reportData["_meta"] = @{ iterations = $Iterations; total_time_s = [Math]::Round($totalSw.Elapsed.TotalSeconds, 2); timestamp = $timestamp }
$reportData | ConvertTo-Json -Depth 3 | Out-File -FilePath $reportPath -Encoding UTF8
Write-Host "  JSON report: $reportPath" -ForegroundColor White
