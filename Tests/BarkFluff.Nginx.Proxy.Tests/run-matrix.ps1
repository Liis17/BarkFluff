$ErrorActionPreference = 'Stop'

$composeFile = Join-Path $PSScriptRoot 'docker-compose.yml'
$projectName = 'bft-proxy-matrix-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) $projectName
$configDirectory = Join-Path $temporaryDirectory 'conf.d'
$certificateDirectory = Join-Path $temporaryDirectory 'certs'
$nginxConfigFile = Join-Path $temporaryDirectory 'nginx.conf'
$env:BFT_PROXY_TEST_PROXY_NAME = "$projectName-proxy"
$env:BFT_PROXY_TEST_UPSTREAM_NAME = "$projectName-upstream"
$env:BFT_PROXY_TEST_NGINX_CONFIG_FILE = $nginxConfigFile
$env:BFT_PROXY_TEST_NGINX_CONFIG_DIR = $configDirectory
$env:BFT_PROXY_TEST_CERT_DIR = $certificateDirectory
$env:BFT_PROXY_TEST_UPSTREAM_CONFIG = Join-Path $PSScriptRoot 'nginx/upstream.conf'
$composeArgs = @('compose', '--project-name', $projectName, '--file', $composeFile)
$testSecret = 'bft-matrix-test-secret-only'
$exitIp = '198.51.100.10'
$otherSourceIp = '198.51.100.11'
$forwardedForInput = '203.0.113.99'
$httpClient = [System.Net.Http.HttpClient]::new()
$matrixPasses = 0

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]] $DockerArgs)

    $output = & docker @DockerArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $details = $output -join "`n"
        throw "docker $($DockerArgs -join ' ') failed with exit code $LASTEXITCODE`n$details"
    }
    return $output
}

function Assert-Equal($expected, $actual, [string] $message) {
    if ($expected -cne $actual) {
        throw "$message; expected '$expected', got '$actual'"
    }
}

function ConvertTo-Pem([string] $label, [byte[]] $data) {
    $base64 = [Convert]::ToBase64String($data)
    $lines = [System.Collections.Generic.List[string]]::new()
    for ($offset = 0; $offset -lt $base64.Length; $offset += 64) {
        $length = [Math]::Min(64, $base64.Length - $offset)
        $lines.Add($base64.Substring($offset, $length))
    }
    return "-----BEGIN $label-----`n$($lines -join "`n")`n-----END $label-----`n"
}

function Send-ProxyRequest(
    [string] $path,
    [string] $source,
    [string] $secretMode,
    [AllowNull()][string] $clientIp,
    [string] $proxyId = 'lv-1',
    [switch] $duplicateClientIp) {
    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Get,
        "$script:baseUri$path")
    [void]$request.Headers.TryAddWithoutValidation('X-Forwarded-For', $script:forwardedForInput)
    [void]$request.Headers.TryAddWithoutValidation('X-BFT-Proxy-Id', $proxyId)

    if ($source -eq 'exit') {
        [void]$request.Headers.TryAddWithoutValidation('CF-Connecting-IP', $script:exitIp)
    }
    elseif ($source -eq 'not-exit') {
        [void]$request.Headers.TryAddWithoutValidation('CF-Connecting-IP', $script:otherSourceIp)
    }

    if ($secretMode -eq 'valid') {
        [void]$request.Headers.TryAddWithoutValidation('X-BFT-Proxy-Secret', $script:testSecret)
    }
    elseif ($secretMode -eq 'wrong') {
        [void]$request.Headers.TryAddWithoutValidation('X-BFT-Proxy-Secret', 'wrong-test-secret')
    }

    if ($null -ne $clientIp) {
        [void]$request.Headers.TryAddWithoutValidation('X-BFT-Client-IP', $clientIp)
        if ($duplicateClientIp) {
            [void]$request.Headers.TryAddWithoutValidation('X-BFT-Client-IP', '198.51.100.22')
        }
    }

    $response = $script:httpClient.Send($request)
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $data = if ([int]$response.StatusCode -eq 200) { $body | ConvertFrom-Json } else { $null }
    $result = [pscustomobject]@{
        StatusCode = [int]$response.StatusCode
        Data = $data
    }
    $response.Dispose()
    $request.Dispose()
    return $result
}

try {
    New-Item -Path $configDirectory -ItemType Directory -Force | Out-Null
    New-Item -Path $certificateDirectory -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $repoRoot 'Docker/nightly/nginx/nginx.conf') $nginxConfigFile
    Copy-Item (Join-Path $repoRoot 'Docker/nightly/nginx/sites/*') $configDirectory -Recurse -Force
    Copy-Item (Join-Path $PSScriptRoot 'nginx/matrix.conf') (Join-Path $configDirectory '99-bft-proxy-matrix.conf')
    foreach ($privateMap in @('exit-ips', 'secrets')) {
        $mapDirectory = Join-Path $configDirectory "bft-proxy-$privateMap"
        New-Item -Path $mapDirectory -ItemType Directory -Force | Out-Null
        Copy-Item (Join-Path $PSScriptRoot "config/$privateMap/matrix.conf") $mapDirectory
    }

    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $certificateRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=barkfluff.com',
        $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $certificateRequest.CreateSelfSigned(
        [DateTimeOffset]::UtcNow.AddDays(-1),
        [DateTimeOffset]::UtcNow.AddDays(1))
    $certificatePem = ConvertTo-Pem 'CERTIFICATE' $certificate.RawData
    $privateKeyPem = ConvertTo-Pem 'PRIVATE KEY' $rsa.ExportPkcs8PrivateKey()
    foreach ($certificateName in @('barkfluff.com', 'files2.barkfluff.com')) {
        [System.IO.File]::WriteAllText((Join-Path $certificateDirectory "$certificateName-crt.pem"), $certificatePem)
        [System.IO.File]::WriteAllText((Join-Path $certificateDirectory "$certificateName-key.pem"), $privateKeyPem)
    }
    $certificate.Dispose()
    $rsa.Dispose()

    Invoke-Docker -DockerArgs ($composeArgs + @('up', '-d')) | Out-Null
    $portOutput = Invoke-Docker -DockerArgs ($composeArgs + @('port', 'proxy', '8080'))
    $portLine = ($portOutput | Select-Object -Last 1).ToString().Trim()
    if ($portLine -match ':(\d+)$') {
        $hostPort = $Matches[1]
    }
    else {
        throw 'docker compose did not return the proxy host port'
    }
    $script:baseUri = "http://127.0.0.1:$hostPort"

    Invoke-Docker -DockerArgs @('exec', $env:BFT_PROXY_TEST_PROXY_NAME, 'nginx', '-t') | ForEach-Object { Write-Output $_ }

    $ipCases = @(
        [pscustomobject]@{ Name = 'IPv4'; Value = '198.51.100.21'; Valid = $true },
        [pscustomobject]@{ Name = 'IPv6'; Value = '2001:db8::21'; Valid = $true },
        [pscustomobject]@{ Name = 'list'; Value = '198.51.100.21, 198.51.100.22'; Valid = $false },
        [pscustomobject]@{ Name = 'malformed'; Value = '999.999.999.999'; Valid = $false },
        [pscustomobject]@{ Name = 'port'; Value = '198.51.100.21:443'; Valid = $false },
        [pscustomobject]@{ Name = 'brackets'; Value = '[2001:db8::21]'; Valid = $false },
        [pscustomobject]@{ Name = 'zone-id'; Value = 'fe80::21%eth0'; Valid = $false },
        [pscustomobject]@{ Name = 'spaces'; Value = '198.51.100.21 22'; Valid = $false },
        [pscustomobject]@{ Name = 'missing'; Value = $null; Valid = $false }
    )

    foreach ($source in @('exit', 'not-exit')) {
        foreach ($secretMode in @('valid', 'wrong', 'missing')) {
            foreach ($ipCase in $ipCases) {
                $result = Send-ProxyRequest '/' $source $secretMode $ipCase.Value
                Assert-Equal 200 $result.StatusCode "matrix HTTP status ($source/$secretMode/$($ipCase.Name))"
                $trusted = $source -eq 'exit' -and $secretMode -eq 'valid' -and $ipCase.Valid

                if ($trusted) {
                    Assert-Equal $ipCase.Value $result.Data.realIp 'trusted X-Real-IP'
                    Assert-Equal $ipCase.Value $result.Data.forwardedFor 'trusted X-Forwarded-For'
                    Assert-Equal 'lv-1' $result.Data.proxyId 'trusted ProxyId'
                }
                else {
                    Assert-Equal $result.Data.remoteAddr $result.Data.realIp 'untrusted X-Real-IP fallback'
                    Assert-Equal "$forwardedForInput, $($result.Data.remoteAddr)" $result.Data.forwardedFor 'untrusted X-Forwarded-For fallback'
                    Assert-Equal '' $result.Data.proxyId 'untrusted ProxyId removal'
                }

                Assert-Equal '' $result.Data.bftClientIp 'X-BFT-Client-IP removal before upstream'
                Assert-Equal '' $result.Data.proxySecret 'X-BFT-Proxy-Secret removal before upstream'
                $matrixPasses++
                Write-Output "$source | $secretMode | $($ipCase.Name) | PASS"
            }
        }
    }

    $direct = Send-ProxyRequest '/' 'direct' 'valid' '198.51.100.31'
    Assert-Equal 200 $direct.StatusCode 'direct request status'
    Assert-Equal $direct.Data.remoteAddr $direct.Data.realIp 'direct source not in Exit allowlist'
    Assert-Equal '' $direct.Data.proxyId 'direct ProxyId removal'
    Assert-Equal '' $direct.Data.proxySecret 'direct secret removal'

    $trustedIdentity = Send-ProxyRequest '/identity/' 'exit' 'valid' '2001:db8::31'
    Assert-Equal '2001:db8::31' $trustedIdentity.Data.forwardedFor 'trusted X-Forwarded-For in remote-address locations'
    $untrustedIdentity = Send-ProxyRequest '/identity/' 'not-exit' 'valid' '198.51.100.31'
    Assert-Equal $untrustedIdentity.Data.remoteAddr $untrustedIdentity.Data.forwardedFor 'untrusted X-Forwarded-For in remote-address locations'

    $trustedInvalidProxyId = Send-ProxyRequest '/' 'exit' 'valid' '198.51.100.32' 'bad id'
    Assert-Equal '198.51.100.32' $trustedInvalidProxyId.Data.realIp 'trusted client IP despite invalid ProxyId'
    Assert-Equal '' $trustedInvalidProxyId.Data.proxyId 'invalid ProxyId removal'
    $duplicateClientIp = Send-ProxyRequest '/' 'exit' 'valid' '198.51.100.33' 'lv-1' -duplicateClientIp
    Assert-Equal $duplicateClientIp.Data.remoteAddr $duplicateClientIp.Data.realIp 'duplicate client IP header rejection'

    Remove-Item -LiteralPath (Join-Path $configDirectory 'bft-proxy-exit-ips/matrix.conf')
    Remove-Item -LiteralPath (Join-Path $configDirectory 'bft-proxy-secrets/matrix.conf')
    Invoke-Docker -DockerArgs @('exec', $env:BFT_PROXY_TEST_PROXY_NAME, 'nginx', '-t') | ForEach-Object { Write-Output $_ }
    Invoke-Docker -DockerArgs @('exec', $env:BFT_PROXY_TEST_PROXY_NAME, 'nginx', '-s', 'reload') | Out-Null

    $disabled = Send-ProxyRequest '/' 'exit' 'valid' '198.51.100.61'
    $reloadAttempts = 0
    while ($disabled.Data.proxyId -ne '' -and $reloadAttempts -lt 20) {
        Start-Sleep -Milliseconds 100
        $disabled = Send-ProxyRequest '/' 'exit' 'valid' '198.51.100.61'
        $reloadAttempts++
    }
    Assert-Equal $exitIp $disabled.Data.realIp 'disabled default X-Real-IP behavior'
    Assert-Equal "$forwardedForInput, $exitIp" $disabled.Data.forwardedFor 'disabled default X-Forwarded-For behavior'
    Assert-Equal '' $disabled.Data.proxyId 'disabled default ProxyId removal'
    Assert-Equal '' $disabled.Data.bftClientIp 'disabled default X-BFT-Client-IP removal'
    Assert-Equal '' $disabled.Data.proxySecret 'disabled default secret removal'

    $defaultRateFirst = Send-ProxyRequest '/rate/' 'exit' 'valid' '198.51.100.62'
    $defaultRateSecond = Send-ProxyRequest '/rate/' 'exit' 'valid' '198.51.100.63'
    Assert-Equal 200 $defaultRateFirst.StatusCode 'disabled default first request uses source key'
    Assert-Equal 429 $defaultRateSecond.StatusCode 'disabled default clients share the existing source key'

    Copy-Item (Join-Path $PSScriptRoot 'config/exit-ips/matrix.conf') (Join-Path $configDirectory 'bft-proxy-exit-ips')
    Copy-Item (Join-Path $PSScriptRoot 'config/secrets/matrix.conf') (Join-Path $configDirectory 'bft-proxy-secrets')
    Invoke-Docker -DockerArgs @('exec', $env:BFT_PROXY_TEST_PROXY_NAME, 'nginx', '-t') | ForEach-Object { Write-Output $_ }
    Invoke-Docker -DockerArgs @('exec', $env:BFT_PROXY_TEST_PROXY_NAME, 'nginx', '-s', 'reload') | Out-Null
    $enabled = Send-ProxyRequest '/' 'exit' 'valid' '198.51.100.64'
    $reloadAttempts = 0
    while ($enabled.Data.proxyId -ne 'lv-1' -and $reloadAttempts -lt 20) {
        Start-Sleep -Milliseconds 100
        $enabled = Send-ProxyRequest '/' 'exit' 'valid' '198.51.100.64'
        $reloadAttempts++
    }
    Assert-Equal '198.51.100.64' $enabled.Data.realIp 'enabled proxy config after reload'
    Assert-Equal 'lv-1' $enabled.Data.proxyId 'enabled ProxyId after reload'

    $rateFirstClient = Send-ProxyRequest '/rate/' 'exit' 'valid' '198.51.100.41'
    $rateSameClient = Send-ProxyRequest '/rate/' 'exit' 'valid' '198.51.100.41'
    $rateSecondClient = Send-ProxyRequest '/rate/' 'exit' 'valid' '198.51.100.42'
    Assert-Equal 200 $rateFirstClient.StatusCode 'first trusted client request'
    Assert-Equal 429 $rateSameClient.StatusCode 'same trusted client is rate limited'
    Assert-Equal 200 $rateSecondClient.StatusCode 'two clients through one Exit have separate rate keys'

    $rateUntrusted = Send-ProxyRequest '/rate/' 'not-exit' 'valid' '198.51.100.51'
    $rateUntrustedSameSource = Send-ProxyRequest '/rate/' 'not-exit' 'valid' '198.51.100.52'
    Assert-Equal 200 $rateUntrusted.StatusCode 'first untrusted source request'
    Assert-Equal 429 $rateUntrustedSameSource.StatusCode 'untrusted source retains its remote-address rate key'

    $logLines = Invoke-Docker -DockerArgs @('logs', $env:BFT_PROXY_TEST_PROXY_NAME)
    $logs = $logLines -join "`n"
    if ($logs.Contains($testSecret, [StringComparison]::Ordinal)) {
        throw 'the test secret appeared in Nginx container logs'
    }
    $accessLogLines = Invoke-Docker -DockerArgs @('exec', $env:BFT_PROXY_TEST_PROXY_NAME, 'cat', '/var/log/nginx/access.log')
    $accessLog = $accessLogLines -join "`n"
    if ($accessLog.Contains($testSecret, [StringComparison]::Ordinal)) {
        throw 'the test secret appeared in Nginx access logs'
    }

    Write-Output "PASS: $matrixPasses matrix requests, direct-source rejection, both X-Forwarded-For styles, disabled defaults, trusted per-client rate keys, untrusted fallback, upstream and log secret checks"
}
finally {
    $httpClient.Dispose()
    try {
        Invoke-Docker -DockerArgs ($composeArgs + @('down', '--remove-orphans')) | Out-Null
    }
    catch {
        & docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Warning "Could not clean up isolated project $projectName; no volumes were removed."
        }
    }

    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTemporaryDirectory = [System.IO.Path]::GetFullPath($temporaryDirectory)
    if ($resolvedTemporaryDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
