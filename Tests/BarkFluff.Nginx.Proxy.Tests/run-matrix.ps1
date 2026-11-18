[CmdletBinding()]
param(
    [ValidateSet('Windows', 'Docker')]
    [string] $Mode = 'Windows'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$temporaryName = 'bft-nginx-proxy-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) $temporaryName
$prefix = Join-Path $temporaryRoot 'runtime'
$configDirectory = Join-Path $prefix 'conf.d'
$certificateDirectory = Join-Path $prefix 'certs'
$logsDirectory = Join-Path $prefix 'logs'
$mimeDirectory = Join-Path $prefix 'conf'
$nginxProcess = $null
$composeStarted = $false
$projectName = $temporaryName
$proxyContainer = "$projectName-proxy"
$upstreamContainer = "$projectName-upstream"
$exitClientContainer = "$projectName-client-exit"
$directClientContainer = "$projectName-client-direct"
$composeFile = Join-Path $PSScriptRoot 'docker-compose.yml'
$composeArgs = @('compose', '--project-name', $projectName, '--file', $composeFile)
$nginxExe = $null
$nginxPrefixArg = $null
$fullConfigFile = Join-Path $prefix 'nginx.conf'
$httpListenPort = 0
$httpsListenPort = 0
$proxyPort = 0
$upstreamPort = 0
$testSecret = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$wrongSecret = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
$exitIp = '198.51.100.10'
$otherSourceIp = '198.51.100.11'
$forwardedForInput = '203.0.113.99'
$matrixPasses = 0

function Assert-Equal($expected, $actual, [string] $message) {
    if ($expected -cne $actual) {
        throw "$message; expected '$expected', got '$actual'"
    }
}

function Invoke-Docker([string[]] $DockerArgs) {
    $output = & docker @DockerArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($DockerArgs -join ' ') failed with exit code $LASTEXITCODE"
    }
    return $output
}

function Invoke-Compose([string[]] $DockerArgs) {
    Invoke-Docker -DockerArgs ($script:composeArgs + $DockerArgs)
}

function ConvertTo-NginxPath([string] $Path) {
    return [System.IO.Path]::GetFullPath($Path).Replace('\', '/')
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function ConvertTo-Pem([string] $Label, [byte[]] $Data) {
    $base64 = [Convert]::ToBase64String($Data)
    $lines = [System.Collections.Generic.List[string]]::new()
    for ($offset = 0; $offset -lt $base64.Length; $offset += 64) {
        $length = [Math]::Min(64, $base64.Length - $offset)
        $lines.Add($base64.Substring($offset, $length))
    }
    return "-----BEGIN $Label-----`n$($lines -join "`n")`n-----END $Label-----`n"
}

function New-TestCertificates {
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=barkfluff.com',
        $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned(
        [DateTimeOffset]::UtcNow.AddDays(-1),
        [DateTimeOffset]::UtcNow.AddDays(1))
    $certificatePem = ConvertTo-Pem 'CERTIFICATE' $certificate.RawData
    $privateKeyPem = ConvertTo-Pem 'PRIVATE KEY' $rsa.ExportPkcs8PrivateKey()
    foreach ($name in @('barkfluff.com', 'files2.barkfluff.com')) {
        [System.IO.File]::WriteAllText((Join-Path $certificateDirectory "$name-crt.pem"), $certificatePem)
        [System.IO.File]::WriteAllText((Join-Path $certificateDirectory "$name-key.pem"), $privateKeyPem)
    }
    $certificate.Dispose()
    $rsa.Dispose()
}

function Copy-ProductionConfig {
    $source = Join-Path $repoRoot 'Docker/nightly/nginx/sites'
    $configNames = @(
        '00-bft-proxy.conf',
        '00-default.conf',
        '00-rate-limits.conf',
        '00-real-ip.conf',
        '01-ssl-params.conf',
        '10-subdomains.conf',
        'bft-proxy-exit-ips.conf.example',
        'bft-proxy-headers.inc',
        'bft-proxy-secrets.conf.example'
    )
    foreach ($name in $configNames) {
        $sourceFile = Join-Path $source $name
        $destinationFile = Join-Path $configDirectory $name
        if ($Mode -eq 'Windows') {
            # Adapt only test paths, listeners, logs, and Docker-only upstream hosts in the temporary staging copy.
            $configuration = [System.IO.File]::ReadAllText($sourceFile)
            $testConfigPrefix = (ConvertTo-NginxPath $configDirectory).TrimEnd('/') + '/'
            $testCertificatePrefix = (ConvertTo-NginxPath $certificateDirectory).TrimEnd('/') + '/'
            $httpListenAddress = "127.0.0.1:$($script:httpListenPort)"
            $httpsListenAddress = "127.0.0.1:$($script:httpsListenPort)"
            $testUpstream = "127.0.0.1:$($script:upstreamPort)"
            $configuration = $configuration.Replace('/etc/nginx/conf.d/', $testConfigPrefix)
            $configuration = $configuration.Replace('/etc/nginx/certs/', $testCertificatePrefix)
            $configuration = [regex]::Replace($configuration, '(?m)^([ \t]*listen[ \t]+)80(?=[ \t;])', ('${1}' + $httpListenAddress))
            $configuration = [regex]::Replace($configuration, '(?m)^([ \t]*listen[ \t]+)443(?=[ \t;])', ('${1}' + $httpsListenAddress))
            $configuration = $configuration.Replace('server gitnotifier:28111;', "server $testUpstream;")
            $configuration = $configuration.Replace('server clientstorage:7050;', "server $testUpstream;")
            $configuration = $configuration.Replace('http://webserver:64641', "http://$testUpstream")
            $configuration = $configuration.Replace('http://fluffboard:8080', "http://$testUpstream")
            $configuration = $configuration.Replace('http://files:7006', "http://$testUpstream")
            $configuration = $configuration.Replace('/dev/stdout', (ConvertTo-NginxPath (Join-Path $logsDirectory 'default-access.log')))
            $configuration = $configuration.Replace('/dev/stderr', (ConvertTo-NginxPath (Join-Path $logsDirectory 'default-error.log')))
            [System.IO.File]::WriteAllText($destinationFile, $configuration)
        }
        else {
            Copy-Item -LiteralPath $sourceFile -Destination $destinationFile
        }
    }
    foreach ($privateDirectory in @('bft-proxy-exit-ips', 'bft-proxy-secrets')) {
        $destination = Join-Path $configDirectory $privateDirectory
        New-Item -Path $destination -ItemType Directory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $source "$privateDirectory/00-empty.conf") -Destination $destination
    }

    $testRealIp = if ($Mode -eq 'Docker') {
        "set_real_ip_from 198.18.230.30;`r`n"
    }
    else {
        "set_real_ip_from 127.0.0.1;`r`n"
    }
    [System.IO.File]::WriteAllText((Join-Path $configDirectory '99-test-real-ip.conf'), $testRealIp)
}

function New-MatrixConfig([int] $ProxyPort, [int] $UpstreamPort) {
    if ($Mode -eq 'Docker') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'nginx/matrix.conf') -Destination (Join-Path $configDirectory '99-bft-proxy-matrix.conf')
        return
    }

    $listenAddress = if ($Mode -eq 'Docker') { '0.0.0.0' } else { '127.0.0.1' }
    $upstreamTarget = "http://127.0.0.1:$UpstreamPort"
    $stubServer = @"

server {
    listen 127.0.0.1`:$UpstreamPort;
    server_name _;

    location / {
        default_type application/json;
        return 200 '{"realIp":"`$http_x_real_ip","forwardedFor":"`$http_x_forwarded_for","bftClientIp":"`$http_x_bft_client_ip","proxyId":"`$http_x_bft_proxy_id","proxySecret":"`$http_x_bft_proxy_secret","remoteAddr":"`$http_x_test_remote_addr"}';
    }
}
"@
    $config = @"
limit_req_zone `$bft_rate_limit_key zone=bft_proxy_matrix:1m rate=1r/s;

server {
    listen $listenAddress`:$ProxyPort;
    server_name bft-proxy-matrix.test;
    limit_req_status 429;

    location /rate/ {
        limit_req zone=bft_proxy_matrix;
        proxy_pass $upstreamTarget;
        include conf.d/bft-proxy-headers.inc;
        proxy_set_header Host `$host;
        proxy_set_header X-Real-IP `$bft_client_ip;
        proxy_set_header X-Forwarded-For `$bft_forwarded_for_add;
        proxy_set_header X-Test-Remote-Addr `$remote_addr;
    }

    location /identity/ {
        proxy_pass $upstreamTarget;
        include conf.d/bft-proxy-headers.inc;
        proxy_set_header Host `$host;
        proxy_set_header X-Real-IP `$bft_client_ip;
        proxy_set_header X-Forwarded-For `$bft_forwarded_for;
        proxy_set_header X-Test-Remote-Addr `$remote_addr;
    }

    location / {
        proxy_pass $upstreamTarget;
        include conf.d/bft-proxy-headers.inc;
        proxy_set_header Host `$host;
        proxy_set_header X-Real-IP `$bft_client_ip;
        proxy_set_header X-Forwarded-For `$bft_forwarded_for_add;
        proxy_set_header X-Test-Remote-Addr `$remote_addr;
    }
}

$stubServer
"@
    [System.IO.File]::WriteAllText((Join-Path $configDirectory '99-bft-proxy-matrix.conf'), $config)
}

function New-WindowsNginxConfigs {
    Copy-Item -LiteralPath (Join-Path $nginxInstallDirectory 'conf/mime.types') -Destination $mimeDirectory
    $fullConfig = @'
worker_processes 1;
pid logs/nginx.pid;
error_log logs/error.log warn;

events {
    worker_connections 1024;
}

http {
    include conf/mime.types;
    default_type application/octet-stream;
    sendfile on;
    keepalive_timeout 65;
    client_max_body_size 100m;
    client_body_buffer_size 10m;
    log_format main '$remote_addr - $remote_user [$time_local] "$request" $status "$http_x_forwarded_for"';
    access_log logs/access.log main;
    include conf.d/*.conf;
}
'@
    [System.IO.File]::WriteAllText($fullConfigFile, $fullConfig)
}

function Invoke-Nginx([string[]] $Arguments) {
    & $script:nginxExe -p $script:nginxPrefixArg -c (ConvertTo-NginxPath $script:fullConfigFile) @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "nginx $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Invoke-NginxCheck {
    if ($Mode -eq 'Windows') {
        $output = & $nginxExe -p $nginxPrefixArg -c (ConvertTo-NginxPath $fullConfigFile) -t 2>&1
        $script:productionLayoutNginxExitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Output $_ }
        if ($LASTEXITCODE -ne 0) {
            $details = $output -join "`n"
            if ($details -match 'host not found in upstream "(gitnotifier|clientstorage):') {
                $script:productionLayoutNginxState = 'NOT_RUN'
                Write-Output 'NOT_RUN: full production-layout nginx -t needs OS-level resolution for Docker service names gitnotifier/clientstorage; Windows nginx cannot use Docker DNS here.'
            }
            else {
                throw "Production-layout nginx -t failed with exit code $LASTEXITCODE"
            }
        }
        else {
            $script:productionLayoutNginxState = 'PASS'
        }
    }
    else {
        Invoke-Compose -DockerArgs @('up', '-d') | Out-Null
        $script:composeStarted = $true
        Invoke-Compose -DockerArgs @('exec', 'proxy', 'nginx', '-t') | ForEach-Object { Write-Output $_ }
    }
}

function Start-MatrixNginx {
    if ($Mode -eq 'Docker') { return }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $nginxExe
    $startInfo.WorkingDirectory = $nginxInstallDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add('-p')
    $startInfo.ArgumentList.Add($nginxPrefixArg)
    $startInfo.ArgumentList.Add('-c')
    $startInfo.ArgumentList.Add((ConvertTo-NginxPath $fullConfigFile))
    $nginxProcess = [System.Diagnostics.Process]::Start($startInfo)
    $script:nginxProcess = $nginxProcess

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($nginxProcess.HasExited) { throw "Test nginx exited with code $($nginxProcess.ExitCode)" }
        $client = [System.Net.Sockets.TcpClient]::new()
        $connected = $false
        try {
            $client.Connect([System.Net.IPAddress]::Loopback, $script:proxyPort)
            $connected = $true
        }
        catch {
        }
        finally {
            $client.Dispose()
        }
        if ($connected) {
            $pidFile = Join-Path $prefix 'logs/nginx.pid'
            $pidValue = 0
            $pidFileExists = Test-Path -LiteralPath $pidFile
            $pidParsed = $false
            if ($pidFileExists) { $pidParsed = [int]::TryParse((Get-Content -Raw -LiteralPath $pidFile).Trim(), [ref]$pidValue) }
            if (-not $pidFileExists -or -not $pidParsed -or $pidValue -ne $nginxProcess.Id) {
                throw 'The test nginx PID file does not identify the process started by this test.'
            }
            return
        }
        Start-Sleep -Milliseconds 100
    }
    throw 'Test nginx did not start listening within 10 seconds.'
}

function Get-EffectiveSource([string] $Source) {
    switch ($Source) {
        'exit' { return $script:exitIp }
        'not-exit' { return $script:otherSourceIp }
        'direct' { return $script:exitIp }
    }
}

function Get-ExpectedRemoteAddress([string] $Source) {
    if ($Mode -eq 'Docker') {
        if ($Source -eq 'direct') { return '198.18.230.31' }
        return Get-EffectiveSource $Source
    }
    if ($Source -eq 'direct') { return '127.0.0.2' }
    return Get-EffectiveSource $Source
}

function Get-SecretValue([string] $SecretMode) {
    switch ($SecretMode) {
        'valid-lower' { return $script:testSecret }
        'valid-upper' { return $script:testSecret.ToUpperInvariant() }
        'wrong' { return $script:wrongSecret }
        default { return $null }
    }
}

function Parse-HttpResponse([string] $RawResponse) {
    $separator = [regex]::Match($RawResponse, "`r?`n`r?`n")
    if (-not $separator.Success) { throw 'Nginx returned an invalid HTTP response.' }
    $headers = $RawResponse.Substring(0, $separator.Index)
    $body = $RawResponse.Substring($separator.Index + $separator.Length).Trim()
    $statusMatch = [regex]::Match($headers, '^HTTP/\d(?:\.\d)?\s+(\d{3})')
    if (-not $statusMatch.Success) { throw 'Nginx returned an invalid HTTP status line.' }
    $status = [int]$statusMatch.Groups[1].Value
    $data = if ($status -eq 200) { $body | ConvertFrom-Json } else { $null }
    return [pscustomobject]@{ StatusCode = $status; Data = $data }
}

function Send-ProxyRequest(
    [string] $Path,
    [string] $Source,
    [string] $SecretMode,
    [AllowNull()][string] $ClientIp) {
    $headers = [System.Collections.Generic.List[string]]::new()
    $headers.Add("GET $Path HTTP/1.1")
    $headers.Add('Host: bft-proxy-matrix.test')
    $headers.Add('Connection: close')
    $headers.Add("CF-Connecting-IP: $(Get-EffectiveSource $Source)")
    $headers.Add("X-Forwarded-For: $script:forwardedForInput")
    $headers.Add('X-BFT-Proxy-Id: lv-1')
    $secret = Get-SecretValue $SecretMode
    if ($null -ne $secret) { $headers.Add("X-BFT-Proxy-Secret: $secret") }
    if ($null -ne $ClientIp) { $headers.Add("X-BFT-Client-IP: $ClientIp") }
    $rawRequest = ($headers -join "`r`n") + "`r`n`r`n"

    if ($Mode -eq 'Docker') {
        $container = if ($Source -eq 'direct') { $directClientContainer } else { $exitClientContainer }
        $requestBase64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($rawRequest))
        $remoteCommand = "printf '%s' '$requestBase64' | base64 -d | nc -w 5 proxy 8080"
        $responseLines = & docker exec $container sh -c $remoteCommand 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Docker test client failed with exit code $LASTEXITCODE" }
        $rawResponse = $responseLines -join "`n"
    }
    else {
        $sourceAddress = if ($Source -eq 'direct') { '127.0.0.2' } else { '127.0.0.1' }
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $client.ReceiveTimeout = 5000
            $client.SendTimeout = 5000
            $client.Client.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Parse($sourceAddress), 0))
            $client.Connect([System.Net.IPAddress]::Loopback, $script:proxyPort)
            $stream = $client.GetStream()
            $requestBytes = [Text.Encoding]::ASCII.GetBytes($rawRequest)
            $stream.Write($requestBytes, 0, $requestBytes.Length)
            $stream.Flush()
            $reader = [System.IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
            try { $rawResponse = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        finally {
            $client.Dispose()
        }
    }
    return Parse-HttpResponse $rawResponse
}

function Send-SubdomainRequest(
    [string] $ServerName,
    [string] $Path,
    [string] $Source,
    [string] $SecretMode,
    [AllowNull()][string] $ClientIp) {
    $headers = [System.Collections.Generic.List[string]]::new()
    $headers.Add("GET $Path HTTP/1.1")
    $headers.Add("Host: $ServerName")
    $headers.Add('Connection: close')
    $headers.Add("CF-Connecting-IP: $(Get-EffectiveSource $Source)")
    $headers.Add("X-Forwarded-For: $script:forwardedForInput")
    $headers.Add('X-BFT-Proxy-Id: lv-1')
    $secret = Get-SecretValue $SecretMode
    if ($null -ne $secret) { $headers.Add("X-BFT-Proxy-Secret: $secret") }
    if ($null -ne $ClientIp) { $headers.Add("X-BFT-Client-IP: $ClientIp") }
    $rawRequest = ($headers -join "`r`n") + "`r`n`r`n"
    $sourceAddress = if ($Source -eq 'direct') { '127.0.0.2' } else { '127.0.0.1' }
    $client = [System.Net.Sockets.TcpClient]::new()
    $sslStream = $null
    try {
        $client.ReceiveTimeout = 5000
        $client.SendTimeout = 5000
        $client.Client.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Parse($sourceAddress), 0))
        $client.Connect([System.Net.IPAddress]::Loopback, $script:httpsListenPort)
        $validateCertificate = [System.Net.Security.RemoteCertificateValidationCallback]{
            param($sender, $certificate, $chain, $sslPolicyErrors)
            return $true
        }
        $sslStream = [System.Net.Security.SslStream]::new($client.GetStream(), $false, $validateCertificate)
        $sslStream.AuthenticateAsClient($ServerName)
        $requestBytes = [Text.Encoding]::ASCII.GetBytes($rawRequest)
        $sslStream.Write($requestBytes, 0, $requestBytes.Length)
        $sslStream.Flush()
        $reader = [System.IO.StreamReader]::new($sslStream, [Text.Encoding]::ASCII, $false, 1024, $true)
        try { $rawResponse = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally {
        if ($null -ne $sslStream) { $sslStream.Dispose() }
        $client.Dispose()
    }
    return Parse-HttpResponse $rawResponse
}

function Assert-UpstreamFallback($Result, [string] $Source, [string] $Path) {
    $remoteAddress = Get-ExpectedRemoteAddress $Source
    Assert-Equal 200 $Result.StatusCode 'untrusted request status'
    Assert-Equal $remoteAddress $Result.Data.realIp 'untrusted X-Real-IP fallback'
    if ($Path -eq '/identity/') {
        Assert-Equal $remoteAddress $Result.Data.forwardedFor 'identity X-Forwarded-For fallback'
    }
    else {
        Assert-Equal "$script:forwardedForInput, $remoteAddress" $Result.Data.forwardedFor 'X-Forwarded-For fallback'
    }
    Assert-Equal '' $Result.Data.proxyId 'untrusted ProxyId removal'
    Assert-Equal '' $Result.Data.bftClientIp 'X-BFT-Client-IP removal before upstream'
    Assert-Equal '' $Result.Data.proxySecret 'X-BFT-Proxy-Secret removal before upstream'
}

function Assert-TrustedUpstream($Result, [string] $ClientIp) {
    Assert-Equal 200 $Result.StatusCode 'trusted request status'
    Assert-Equal $ClientIp $Result.Data.realIp 'trusted X-Real-IP'
    Assert-Equal $ClientIp $Result.Data.forwardedFor 'trusted X-Forwarded-For'
    Assert-Equal 'lv-1' $Result.Data.proxyId 'trusted ProxyId'
    Assert-Equal '' $Result.Data.bftClientIp 'X-BFT-Client-IP removal before upstream'
    Assert-Equal '' $Result.Data.proxySecret 'X-BFT-Proxy-Secret removal before upstream'
}

function Add-TestPrivateMaps {
    $exitMap = Join-Path $configDirectory 'bft-proxy-exit-ips/matrix.conf'
    $secretMap = Join-Path $configDirectory 'bft-proxy-secrets/matrix.conf'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'config/exit-ips/matrix.conf') -Destination $exitMap
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'config/secrets/matrix.conf') -Destination $secretMap
}

function Assert-NginxLogsDoNotContainSecret {
    if ($Mode -eq 'Docker') {
        $accessLog = Invoke-Docker -DockerArgs @('exec', $proxyContainer, 'cat', '/var/log/nginx/access.log')
        $logText = $accessLog -join "`n"
    }
    else {
        $accessLogPath = Join-Path $logsDirectory 'access.log'
        $logText = if (Test-Path -LiteralPath $accessLogPath) { Get-Content -Raw -LiteralPath $accessLogPath } else { '' }
    }
    if ($logText.Contains($testSecret, [StringComparison]::Ordinal)) {
        throw 'The synthetic BFT test secret appeared in the Nginx access log.'
    }
}

try {
    foreach ($directory in @(
        $configDirectory,
        $certificateDirectory,
        $logsDirectory,
        $mimeDirectory,
        (Join-Path $prefix 'temp/client_body_temp'),
        (Join-Path $prefix 'temp/proxy_temp'),
        (Join-Path $prefix 'temp/fastcgi_temp'),
        (Join-Path $prefix 'temp/uwsgi_temp'),
        (Join-Path $prefix 'temp/scgi_temp')
    )) {
        New-Item -Path $directory -ItemType Directory -Force | Out-Null
    }

    $proxyPort = if ($Mode -eq 'Docker') { 8080 } else { Get-FreeTcpPort }
    $upstreamPort = if ($Mode -eq 'Docker') { 8081 } else { Get-FreeTcpPort }
    while ($upstreamPort -eq $proxyPort) { $upstreamPort = Get-FreeTcpPort }
    if ($Mode -eq 'Windows') {
        $usedPorts = @($proxyPort, $upstreamPort)
        do { $httpListenPort = Get-FreeTcpPort } while ($httpListenPort -in $usedPorts)
        $usedPorts += $httpListenPort
        do { $httpsListenPort = Get-FreeTcpPort } while ($httpsListenPort -in $usedPorts)
    }
    $script:proxyPort = $proxyPort
    $script:httpsListenPort = $httpsListenPort

    Copy-ProductionConfig
    New-TestCertificates

    if ($Mode -eq 'Windows') {
        $downloadPage = Invoke-WebRequest -Uri 'https://nginx.org/en/download.html'
        $versionMatch = [regex]::Match($downloadPage.Content, 'nginx/Windows-(?<version>\d+\.\d+\.\d+)')
        if (-not $versionMatch.Success) { throw 'Could not find the Windows nginx ZIP version on nginx.org.' }
        $version = $versionMatch.Groups['version'].Value
        $archive = Join-Path $temporaryRoot "nginx-$version.zip"
        Invoke-WebRequest -Uri "https://nginx.org/download/nginx-$version.zip" -OutFile $archive
        $nginxInstallRoot = Join-Path $temporaryRoot 'nginx-dist'
        Expand-Archive -LiteralPath $archive -DestinationPath $nginxInstallRoot -Force
        $nginxExe = (Get-ChildItem -LiteralPath $nginxInstallRoot -Recurse -Filter nginx.exe | Select-Object -First 1).FullName
        if (-not $nginxExe) { throw 'The official nginx ZIP did not contain nginx.exe.' }
        $nginxInstallDirectory = Split-Path -Parent $nginxExe
        $nginxPrefixArg = (ConvertTo-NginxPath $prefix).TrimEnd('/') + '/'
        New-WindowsNginxConfigs
    }
    else {
        $testNginxConfig = Join-Path $repoRoot 'Docker/nightly/nginx/nginx.conf'
        Copy-Item -LiteralPath $testNginxConfig -Destination $fullConfigFile
        $nginxPrefixArg = '/etc/nginx/'
    }

    New-MatrixConfig $proxyPort $upstreamPort

    if ($Mode -eq 'Docker') {
        $env:BFT_PROXY_TEST_PROXY_NAME = $proxyContainer
        $env:BFT_PROXY_TEST_UPSTREAM_NAME = $upstreamContainer
        $env:BFT_PROXY_TEST_EXIT_CLIENT_NAME = $exitClientContainer
        $env:BFT_PROXY_TEST_DIRECT_CLIENT_NAME = $directClientContainer
        $env:BFT_PROXY_TEST_NGINX_CONFIG_FILE = $fullConfigFile
        $env:BFT_PROXY_TEST_NGINX_CONFIG_DIR = $configDirectory
        $env:BFT_PROXY_TEST_CERT_DIR = $certificateDirectory
        $env:BFT_PROXY_TEST_UPSTREAM_CONFIG = Join-Path $PSScriptRoot 'nginx/upstream.conf'
    }

    Invoke-NginxCheck
    Start-MatrixNginx

    $disabled = Send-ProxyRequest '/' 'exit' 'valid-lower' '198.51.100.61'
    Assert-UpstreamFallback $disabled 'exit' '/'
    $disabledIdentity = Send-ProxyRequest '/identity/' 'exit' 'valid-lower' '198.51.100.62'
    Assert-UpstreamFallback $disabledIdentity 'exit' '/identity/'
    $disabledRateFirst = Send-ProxyRequest '/rate/' 'exit' 'valid-lower' '198.51.100.63'
    $disabledRateSecond = Send-ProxyRequest '/rate/' 'exit' 'valid-lower' '198.51.100.64'
    Assert-Equal 200 $disabledRateFirst.StatusCode 'disabled default first rate request'
    Assert-Equal 429 $disabledRateSecond.StatusCode 'disabled default clients share the existing source key'

    Add-TestPrivateMaps
    if ($Mode -eq 'Docker') {
        Invoke-Compose -DockerArgs @('exec', 'proxy', 'nginx', '-t') | ForEach-Object { Write-Output $_ }
        Invoke-Compose -DockerArgs @('exec', 'proxy', 'nginx', '-s', 'reload') | Out-Null
    }
    else {
        Invoke-Nginx -Arguments @('-t')
        Invoke-Nginx -Arguments @('-s', 'reload')
    }

    $reloadDeadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $enabledProbe = Send-ProxyRequest '/' 'exit' 'valid-lower' '198.51.100.64'
        if ($enabledProbe.Data.proxyId -eq 'lv-1') { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $reloadDeadline)
    Assert-TrustedUpstream $enabledProbe '198.51.100.64'

    if ($Mode -eq 'Windows') {
        $directFilesMedia = Send-SubdomainRequest 'files2.barkfluff.com' '/web/' 'direct' 'valid-lower' '198.51.100.65'
        Assert-UpstreamFallback $directFilesMedia 'direct' '/web/'
        $trustedFilesMedia = Send-SubdomainRequest 'files2.barkfluff.com' '/web/' 'exit' 'valid-lower' '198.51.100.66'
        Assert-TrustedUpstream $trustedFilesMedia '198.51.100.66'
        $directBarkfluff = Send-SubdomainRequest 'barkfluff.com' '/' 'direct' 'valid-lower' '198.51.100.67'
        Assert-UpstreamFallback $directBarkfluff 'direct' '/'
        Write-Output 'PASS: files2 and barkfluff production vhosts ignore a spoofed CF-Connecting-IP from direct origin clients; files2 forwards a trusted Exit client.'
    }

    $ipCases = @(
        [pscustomobject]@{ Name = 'IPv4'; Value = '198.51.100.21'; Valid = $true },
        [pscustomobject]@{ Name = 'IPv6'; Value = '2001:db8::21'; Valid = $true },
        [pscustomobject]@{ Name = 'list'; Value = '198.51.100.21, 198.51.100.22'; Valid = $false },
        [pscustomobject]@{ Name = 'malformed'; Value = '999.999.999.999'; Valid = $false },
        [pscustomobject]@{ Name = 'port'; Value = '198.51.100.21:443'; Valid = $false },
        [pscustomobject]@{ Name = 'brackets'; Value = '[2001:db8::21]'; Valid = $false },
        [pscustomobject]@{ Name = 'zone-id'; Value = 'fe80::21%eth0'; Valid = $false },
        [pscustomobject]@{ Name = 'spaces'; Value = '198.51.100.21 22'; Valid = $false },
        [pscustomobject]@{ Name = 'missing'; Value = $null; Valid = $false },
        [pscustomobject]@{ Name = '255.255.255.255'; Value = '255.255.255.255'; Valid = $false }
    )
    $sources = @('exit', 'not-exit', 'direct')
    $secretModes = @('valid-lower', 'wrong', 'missing', 'valid-upper')
    foreach ($source in $sources) {
        foreach ($secretMode in $secretModes) {
            foreach ($ipCase in $ipCases) {
                $result = Send-ProxyRequest '/' $source $secretMode $ipCase.Value
                $trusted = $source -eq 'exit' -and $secretMode -in @('valid-lower', 'valid-upper') -and $ipCase.Valid
                if ($trusted) { Assert-TrustedUpstream $result $ipCase.Value }
                else { Assert-UpstreamFallback $result $source '/' }
                $matrixPasses++
            }
        }
    }

    $trustedIdentity = Send-ProxyRequest '/identity/' 'exit' 'valid-lower' '2001:db8::31'
    Assert-Equal '2001:db8::31' $trustedIdentity.Data.forwardedFor 'trusted X-Forwarded-For in remote-address locations'
    $untrustedIdentity = Send-ProxyRequest '/identity/' 'not-exit' 'valid-lower' '198.51.100.31'
    Assert-UpstreamFallback $untrustedIdentity 'not-exit' '/identity/'

    $rateFirst = Send-ProxyRequest '/rate/' 'exit' 'valid-lower' '198.51.100.41'
    $rateSame = Send-ProxyRequest '/rate/' 'exit' 'valid-lower' '198.51.100.41'
    $rateSecond = Send-ProxyRequest '/rate/' 'exit' 'valid-lower' '198.51.100.42'
    Assert-Equal 200 $rateFirst.StatusCode 'first trusted client request'
    Assert-Equal 429 $rateSame.StatusCode 'same trusted client is rate limited'
    Assert-Equal 200 $rateSecond.StatusCode 'two clients through one Exit have separate rate keys'

    $rateUntrusted = Send-ProxyRequest '/rate/' 'not-exit' 'valid-lower' '198.51.100.51'
    $rateUntrustedSameSource = Send-ProxyRequest '/rate/' 'not-exit' 'valid-lower' '198.51.100.52'
    Assert-Equal 200 $rateUntrusted.StatusCode 'first untrusted source request'
    Assert-Equal 429 $rateUntrustedSameSource.StatusCode 'untrusted source retains its remote-address rate key'

    Assert-NginxLogsDoNotContainSecret
    Write-Output "PASS: $matrixPasses source/secret/client-IP combinations; loopback trust emulates the Cloudflare-side peer only in test config; private test maps are synthetic."
}
finally {
    if ($Mode -eq 'Windows' -and $null -ne $nginxExe) {
        $expectedExecutable = [System.IO.Path]::GetFullPath($nginxExe)
        $expectedConfig = ConvertTo-NginxPath $fullConfigFile
        $nginxEntries = Get-CimInstance Win32_Process | Where-Object {
            [string]::Equals($_.ExecutablePath, $expectedExecutable, [StringComparison]::OrdinalIgnoreCase) -and
            $null -ne $_.CommandLine -and
            $_.CommandLine.Contains($expectedConfig, [StringComparison]::OrdinalIgnoreCase)
        }
        foreach ($nginxEntry in $nginxEntries) {
            $nginxPid = $nginxEntry.ProcessId
            Stop-Process -Id $nginxPid -Force -ErrorAction Stop
            $stopDeadline = [DateTime]::UtcNow.AddSeconds(5)
            do {
                $nginxEntry = Get-CimInstance Win32_Process -Filter "ProcessId = $nginxPid"
                if ($null -eq $nginxEntry -or
                    -not [string]::Equals($nginxEntry.ExecutablePath, $expectedExecutable, [StringComparison]::OrdinalIgnoreCase)) {
                    break
                }
                Start-Sleep -Milliseconds 100
            } while ([DateTime]::UtcNow -lt $stopDeadline)
            if ($null -ne $nginxEntry -and
                [string]::Equals($nginxEntry.ExecutablePath, $expectedExecutable, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Owned matrix nginx PID $nginxPid did not stop."
            }
        }
        if ($null -ne $nginxProcess) { try { $nginxProcess.Dispose() } catch { } }
    }

    if ($composeStarted) {
        try { Invoke-Compose -DockerArgs @('down', '--remove-orphans') | Out-Null } catch { }
    }

    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
    $systemTemporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith($systemTemporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
