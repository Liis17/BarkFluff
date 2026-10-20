$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('barkfluff-lognara-compose-' + [guid]::NewGuid().ToString('N'))
$repoRootForCompose = $repoRoot.Replace('\', '/')

$projects = @(
    @{ Name = 'main'; Files = @('Docker/nightly/barkfluff/docker-compose.yml', 'Docker/nightly/barkfluff/docker-compose.lognara.yml') },
    @{ Name = 'setup'; Files = @('Docker/nightly/barkfluff/docker-compose.setup.yml', 'Docker/nightly/barkfluff/docker-compose.setup.lognara.yml') },
    @{ Name = 'clientstorage'; Files = @('Docker/nightly/clientstorage/docker-compose.yml', 'Docker/nightly/clientstorage/docker-compose.lognara.yml') },
    @{ Name = 'website'; Files = @('Docker/nightly/website/docker-compose.yml', 'Docker/nightly/website/docker-compose.lognara.yml') },
    @{ Name = 'navigator'; Files = @('Docker/nightly/navigator/nginx/docker-compose.yml', 'Docker/nightly/navigator/nginx/docker-compose.lognara.yml') },
    @{ Name = 'nginx'; Files = @('Docker/nightly/nginx/docker-compose.yml', 'Docker/nightly/nginx/docker-compose.lognara.yml') },
    @{ Name = 'proxy'; Files = @('Docker/nightly/proxy/docker-compose.yml', 'Docker/nightly/proxy/docker-compose.lognara.yml') }
)
$baseProjects = @(
    @{ Name = 'main-base'; Files = @('Docker/nightly/barkfluff/docker-compose.yml') },
    @{ Name = 'setup-base'; Files = @('Docker/nightly/barkfluff/docker-compose.setup.yml') },
    @{ Name = 'clientstorage-base'; Files = @('Docker/nightly/clientstorage/docker-compose.yml') },
    @{ Name = 'website-base'; Files = @('Docker/nightly/website/docker-compose.yml') },
    @{ Name = 'navigator-base'; Files = @('Docker/nightly/navigator/nginx/docker-compose.yml') },
    @{ Name = 'proxy-base'; Files = @('Docker/nightly/proxy/docker-compose.yml') }
)

$mainApplications = [ordered]@{
    beacon = 'BEACON'
    settings = 'SETTINGS'
    files = 'FILES'
    identity = 'IDENTITY'
    messages = 'MESSAGES'
    notification = 'NOTIFICATION'
    users = 'USERS'
    'fast-auth' = 'FAST_AUTH'
    updates = 'UPDATES'
    onliner = 'ONLINER'
    federation = 'FEDERATION'
    'cloud-messaging' = 'CLOUD_MESSAGING'
    web = 'WEB'
    developers = 'DEVELOPERS'
    calls = 'CALLS'
    bots = 'BOTS'
    'admin-panel' = 'ADMIN_PANEL'
}

function Assert-Equal($actual, $expected, [string]$label) {
    if ([string]$actual -cne [string]$expected) {
        throw "$label expected '$expected', got '$actual'."
    }
}

function Assert-True([bool]$condition, [string]$label) {
    if (-not $condition) {
        throw $label
    }
}

function Get-RequiredComposeValues([string[]]$contents) {
    $values = [ordered]@{}
    foreach ($content in $contents) {
        $activeContent = [regex]::Replace($content, '(?m)^[ \t]*#.*(?:\r?\n|$)', '')
        foreach ($match in [regex]::Matches($activeContent, '\$\{(?<expression>[^}]+)\}')) {
            $expression = $match.Groups['expression'].Value
            $variable = [regex]::Match($expression, '^(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<operator>:-|:\?|-|\?)')
            if (-not $variable.Success) {
                if ($expression -match '^[A-Za-z_][A-Za-z0-9_]*$') {
                    $name = $expression
                    $operator = ''
                }
                else {
                    continue
                }
            }
            else {
                $name = $variable.Groups['name'].Value
                $operator = $variable.Groups['operator'].Value
            }

            if ($operator -in @('', ':?', '?')) {
                if ($values.Contains($name)) {
                    continue
                }

                if ($name -eq 'LOGNARA_CORE_URL') {
                    $values[$name] = 'https://core.invalid'
                }
                elseif ($name -eq 'LOGNARA_SERVER') {
                    $values[$name] = 'compose-test-node'
                }
                elseif ($name -match 'PORT$') {
                    $values[$name] = '5432'
                }
                else {
                    $values[$name] = "compose-test-$name"
                }
            }
        }
    }

    return $values
}

function Write-EnvironmentFile([string]$path, $values) {
    $lines = foreach ($key in $values.Keys) {
        "$key=$($values[$key])"
    }
    [IO.File]::WriteAllLines($path, [string[]]$lines, [Text.UTF8Encoding]::new($false))
}

function New-TestProject($definition) {
    $projectPath = Join-Path $tempRoot $definition.Name
    New-Item -ItemType Directory -Path $projectPath | Out-Null

    $composePaths = @()
    $contents = @()
    foreach ($relativePath in $definition.Files) {
        $sourcePath = Join-Path $repoRoot $relativePath
        $content = [IO.File]::ReadAllText($sourcePath)
        $content = [regex]::Replace(
            $content,
            '(?m)^(\s*context:\s*).+$',
            [System.Text.RegularExpressions.MatchEvaluator]{
                param($match)
                $match.Groups[1].Value + '"' + $repoRootForCompose + '"'
            })
        $targetPath = Join-Path $projectPath (Split-Path $relativePath -Leaf)
        [IO.File]::WriteAllText($targetPath, $content, [Text.UTF8Encoding]::new($false))
        $composePaths += $targetPath
        $contents += $content
    }

    $environmentValues = Get-RequiredComposeValues $contents
    $environmentFile = Join-Path $projectPath 'compose-test.env'
    Write-EnvironmentFile $environmentFile $environmentValues
    Write-EnvironmentFile (Join-Path $projectPath '.env') $environmentValues

    $secretDirectory = Join-Path $projectPath 'secrets'
    New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $secretDirectory 'setup_token'), 'compose-test-secret', [Text.UTF8Encoding]::new($false))

    return @{
        Path = $projectPath
        ComposePaths = $composePaths
        Environment = $environmentValues
    }
}

function Invoke-ComposeConfig($project, $overrides = @{}, [string]$profile = '') {
    $values = [ordered]@{}
    foreach ($key in $project.Environment.Keys) {
        $values[$key] = $project.Environment[$key]
    }
    foreach ($key in $overrides.Keys) {
        $values[$key] = $overrides[$key]
    }

    $environmentFile = Join-Path $project.Path ('compose-test-' + [guid]::NewGuid().ToString('N') + '.env')
    Write-EnvironmentFile $environmentFile $values
    Write-EnvironmentFile (Join-Path $project.Path '.env') $values

    $arguments = @('compose', '--project-directory', $project.Path, '--env-file', $environmentFile)
    if ($profile) {
        $arguments += @('--profile', $profile)
    }
    foreach ($composePath in $project.ComposePaths) {
        $arguments += @('-f', $composePath)
    }
    $arguments += @('config', '--format', 'json')

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & docker @arguments 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($exitCode -ne 0) {
        throw "docker compose config failed for '$($project.Path)' with exit code $exitCode."
    }

    try {
        return ($output -join [Environment]::NewLine | ConvertFrom-Json)
    }
    catch {
        throw "docker compose config returned invalid JSON for '$($project.Path)'."
    }
}

function Has-Property($object, [string]$name) {
    return $null -ne $object -and $null -ne $object.PSObject.Properties[$name]
}

function Get-Property($object, [string]$name) {
    $property = $object.PSObject.Properties[$name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Get-Service($config, [string]$name) {
    if (-not (Has-Property $config.services $name)) {
        throw "Missing Compose service '$name'."
    }
    return Get-Property $config.services $name
}

function Assert-Flags($config, [string]$serviceName, [string]$prefix, [string]$lognara, [string]$seq, [string]$console) {
    $environment = (Get-Service $config $serviceName).environment
    Assert-Equal (Get-Property $environment 'LognaraEnable') $lognara "$serviceName ($prefix) LognaraEnable"
    Assert-Equal (Get-Property $environment 'SeqEnable') $seq "$serviceName ($prefix) SeqEnable"
    Assert-Equal (Get-Property $environment 'ConsoleOutput') $console "$serviceName ($prefix) ConsoleOutput"
}

function Assert-FlagMappings([string[]]$files, $services, [string]$lognaraDefault, [string]$seqDefault, [string]$consoleDefault, [string]$label) {
    $content = ($files | ForEach-Object { [IO.File]::ReadAllText((Join-Path $repoRoot $_)) }) -join [Environment]::NewLine
    foreach ($service in $services.GetEnumerator()) {
        $expected = @(
            'LognaraEnable: "$' + '{' + $service.Value + '_LOGNARA_ENABLE:-' + $lognaraDefault + '}"'
            'SeqEnable: "$' + '{' + $service.Value + '_SEQ_ENABLE:-' + $seqDefault + '}"'
            'ConsoleOutput: "$' + '{' + $service.Value + '_CONSOLE_OUTPUT:-' + $consoleDefault + '}"'
        )
        foreach ($mapping in $expected) {
            Assert-True $content.Contains($mapping) "$label is missing service-specific mapping '$mapping'."
        }
    }
}

function Assert-SequenceEqual($actual, $expected, [string]$label) {
    $actualItems = @($actual)
    $expectedItems = @($expected)
    Assert-Equal $actualItems.Count $expectedItems.Count "$label count"
    for ($index = 0; $index -lt $expectedItems.Count; $index++) {
        Assert-Equal $actualItems[$index] $expectedItems[$index] "$label item $index"
    }
}

function Get-MountAt($service, [string]$target) {
    return @($service.volumes | Where-Object { $_.target -ceq $target }) | Select-Object -First 1
}

New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $testProjects = @{}
    foreach ($definition in $projects) {
        $testProjects[$definition.Name] = New-TestProject $definition
    }
    foreach ($definition in $baseProjects) {
        $testProjects[$definition.Name] = New-TestProject $definition
    }

    Assert-FlagMappings @('Docker/nightly/barkfluff/docker-compose.yml') $mainApplications 'false' 'true' 'true' 'main base flags'
    Assert-FlagMappings @('Docker/nightly/barkfluff/docker-compose.lognara.yml') $mainApplications 'true' 'false' 'true' 'main overlay flags'
    Assert-FlagMappings @('Docker/nightly/barkfluff/docker-compose.setup.yml') ([ordered]@{ setup = 'SETUP'; settings = 'SETUP_SETTINGS' }) 'false' 'true' 'true' 'setup base flags'
    Assert-FlagMappings @('Docker/nightly/barkfluff/docker-compose.setup.lognara.yml') ([ordered]@{ setup = 'SETUP'; settings = 'SETUP_SETTINGS' }) 'true' 'false' 'true' 'setup overlay flags'
    Assert-FlagMappings @('Docker/nightly/clientstorage/docker-compose.yml') ([ordered]@{ 'clientstorage-nightly' = 'CLIENTSTORAGE' }) 'false' 'true' 'true' 'clientstorage base flags'
    Assert-FlagMappings @('Docker/nightly/clientstorage/docker-compose.lognara.yml') ([ordered]@{ 'clientstorage-nightly' = 'CLIENTSTORAGE' }) 'true' 'false' 'true' 'clientstorage overlay flags'
    Assert-FlagMappings @('Docker/nightly/website/docker-compose.yml') ([ordered]@{ 'webserver-nightly' = 'WEBSERVER' }) 'false' 'true' 'true' 'website base flags'
    Assert-FlagMappings @('Docker/nightly/website/docker-compose.lognara.yml') ([ordered]@{ 'webserver-nightly' = 'WEBSERVER' }) 'true' 'false' 'true' 'website overlay flags'
    Assert-FlagMappings @('Docker/nightly/navigator/nginx/docker-compose.yml') ([ordered]@{ 'navigator-dev' = 'NAVIGATOR' }) 'false' 'true' 'true' 'navigator base flags'
    Assert-FlagMappings @('Docker/nightly/navigator/nginx/docker-compose.lognara.yml') ([ordered]@{ 'navigator-dev' = 'NAVIGATOR' }) 'true' 'false' 'true' 'navigator overlay flags'
    Assert-FlagMappings @('Docker/nightly/proxy/docker-compose.yml') ([ordered]@{ web = 'WEB_PROXY' }) 'false' 'true' 'true' 'proxy base flags'
    Assert-FlagMappings @('Docker/nightly/proxy/docker-compose.lognara.yml') ([ordered]@{ web = 'WEB_PROXY' }) 'true' 'false' 'true' 'proxy overlay flags'

    $mainBase = Invoke-ComposeConfig $testProjects.'main-base'
    foreach ($service in $mainApplications.GetEnumerator()) {
        Assert-Flags $mainBase $service.Key $service.Value 'false' 'true' 'true'
    }
    Write-Output '[PASS] main base defaults: false/true/true for all 17 .NET services'

    $setupBase = Invoke-ComposeConfig $testProjects.'setup-base'
    Assert-Flags $setupBase 'setup' 'SETUP' 'false' 'true' 'true'
    Assert-Flags $setupBase 'settings' 'SETUP_SETTINGS' 'false' 'true' 'true'
    $clientBase = Invoke-ComposeConfig $testProjects.'clientstorage-base'
    Assert-Flags $clientBase 'clientstorage-nightly' 'CLIENTSTORAGE' 'false' 'true' 'true'
    $websiteBase = Invoke-ComposeConfig $testProjects.'website-base'
    Assert-Flags $websiteBase 'webserver-nightly' 'WEBSERVER' 'false' 'true' 'true'
    $navigatorBase = Invoke-ComposeConfig $testProjects.'navigator-base'
    Assert-Flags $navigatorBase 'navigator-dev' 'NAVIGATOR' 'false' 'true' 'true'
    $proxyBase = Invoke-ComposeConfig $testProjects.'proxy-base'
    Assert-Flags $proxyBase 'web' 'WEB_PROXY' 'false' 'true' 'true'
    Write-Output '[PASS] setup and separate-bundle base defaults'

    $main = Invoke-ComposeConfig $testProjects.main
    foreach ($service in $mainApplications.GetEnumerator()) {
        Assert-Flags $main $service.Key $service.Value 'true' 'false' 'true'
        $mount = Get-MountAt (Get-Service $main $service.Key) '/app/logs'
        Assert-True ($null -ne $mount -and $mount.type -ceq 'volume') "$($service.Key) must persist its application log buffer on a named volume."
        $spool = Get-MountAt (Get-Service $main $service.Key) '/var/lib/lognara-agent/spool'
        Assert-True ($null -ne $spool -and $spool.type -ceq 'volume') "$($service.Key) must persist its agent spool on a named volume."
        Assert-True ($mount.source -cne $spool.source) "$($service.Key) application buffer and agent spool must use separate volumes."
    }
    Assert-True (-not $testProjects.main.Environment.Contains('MINIO_LOGNARA_RELAY_TOKEN')) 'The inactive MinIO profile must not require a token.'
    Assert-True (Has-Property $main.services 'lognara-relay') 'The main overlay must define exactly one relay.'
    $relay = Get-Service $main 'lognara-relay'
    Assert-Equal $relay.user '1000:1000' 'relay user'
    Assert-Equal (Get-Property $relay.environment 'LOGNARA_LISTEN_ADDR') '0.0.0.0:7401' 'relay listener'
    Assert-True (-not (Has-Property $relay 'ports')) 'The relay must not publish its ingest port to the host.'
    Assert-Equal (Get-MountAt $relay '/var/lib/lognara-relay/spool').source 'lognara-relay-spool' 'relay spool volume'
    $mainServices = @($main.services.PSObject.Properties | ForEach-Object { $_.Value })
    Assert-True (@($mainServices | Where-Object { $_.ports | Where-Object { $_.target -in @(7400, 7401) } }).Count -eq 0) 'No Lognara ingest port may be published by the main overlay.'
    Write-Output '[PASS] main overlay defaults, isolated volumes, and private relay'

    for ($bits = 0; $bits -lt 8; $bits++) {
        $lognara = if ($bits -band 1) { 'true' } else { 'false' }
        $seq = if ($bits -band 2) { 'true' } else { 'false' }
        $console = if ($bits -band 4) { 'true' } else { 'false' }
        $overrides = @{
            BEACON_LOGNARA_ENABLE = $lognara
            BEACON_SEQ_ENABLE = $seq
            BEACON_CONSOLE_OUTPUT = $console
        }
        $variant = Invoke-ComposeConfig $testProjects.'main-base' $overrides
        Assert-Flags $variant 'beacon' 'BEACON' $lognara $seq $console
    }
    Write-Output '[PASS] all eight independent flag combinations render in the base deployment'

    for ($bits = 0; $bits -lt 8; $bits++) {
        $lognara = if ($bits -band 1) { 'true' } else { 'false' }
        $seq = if ($bits -band 2) { 'true' } else { 'false' }
        $console = if ($bits -band 4) { 'true' } else { 'false' }
        $overrides = @{
            BEACON_LOGNARA_ENABLE = $lognara
            BEACON_SEQ_ENABLE = $seq
            BEACON_CONSOLE_OUTPUT = $console
        }
        $variant = Invoke-ComposeConfig $testProjects.main $overrides
        Assert-Flags $variant 'beacon' 'BEACON' $lognara $seq $console
    }
    Write-Output '[PASS] all eight independent flag combinations render'

    $missingToken = Invoke-ComposeConfig $testProjects.main @{ BEACON_LOGNARA_ENABLE = 'false' }
    Assert-Equal (Get-Property (Get-Service $missingToken 'beacon').environment 'LOGNARA_RELAY_TOKEN') '' 'disabled service token default'

    $uniqueTokenOverrides = @{}
    foreach ($service in $mainApplications.GetEnumerator()) {
        $uniqueTokenOverrides["$($service.Value)_LOGNARA_RELAY_TOKEN"] = "token-$($service.Value.ToLowerInvariant())"
    }
    $uniqueTokens = Invoke-ComposeConfig $testProjects.main $uniqueTokenOverrides
    foreach ($service in $mainApplications.GetEnumerator()) {
        Assert-Equal (Get-Property (Get-Service $uniqueTokens $service.Key).environment 'LOGNARA_RELAY_TOKEN') $uniqueTokenOverrides["$($service.Value)_LOGNARA_RELAY_TOKEN"] "$($service.Key) token scope"
    }
    Write-Output '[PASS] disabled services accept an empty token and every main service has independent token mapping'

    $mainInfrastructure = [ordered]@{
        postgres = 'POSTGRES'
        'postgres-bootstrap' = 'POSTGRES_BOOTSTRAP'
        rabbitmq = 'RABBITMQ'
        redis = 'REDIS'
        livekit = 'LIVEKIT'
        seq = 'SEQ'
    }
    $infrastructureOverrides = @{}
    foreach ($service in $mainInfrastructure.GetEnumerator()) {
        $infrastructureOverrides["$($service.Value)_LOGNARA_RELAY_TOKEN"] = "token-$($service.Value.ToLowerInvariant())"
        $infrastructureOverrides["$($service.Value)_LOGNARA_SERVICE_INSTANCE"] = "instance-$($service.Value.ToLowerInvariant())"
    }
    $infrastructure = Invoke-ComposeConfig $testProjects.main $infrastructureOverrides
    foreach ($service in $mainInfrastructure.GetEnumerator()) {
        $environment = (Get-Service $infrastructure $service.Key).environment
        Assert-Equal (Get-Property $environment 'LOGNARA_RELAY_TOKEN') $infrastructureOverrides["$($service.Value)_LOGNARA_RELAY_TOKEN"] "$($service.Key) token scope"
        Assert-Equal (Get-Property $environment 'LOGNARA_SERVICE_INSTANCE') $infrastructureOverrides["$($service.Value)_LOGNARA_SERVICE_INSTANCE"] "$($service.Key) instance scope"
    }
    Write-Output '[PASS] infrastructure sources use independent token and instance mappings'

    $setup = Invoke-ComposeConfig $testProjects.setup
    Assert-Flags $setup 'setup' 'SETUP' 'true' 'false' 'true'
    Assert-Flags $setup 'settings' 'SETUP_SETTINGS' 'true' 'false' 'true'
    Assert-True (-not (Has-Property $setup.services 'lognara-relay')) 'The setup overlay must reuse the external relay.'
    Assert-Equal (Get-Property (Get-Service $setup 'postgres-bootstrap').environment 'LOGNARA_SERVICE') 'postgres-bootstrap-setup' 'setup bootstrap source identity'
    Assert-Equal (Get-Property (Get-Service $setup 'postgres-bootstrap').environment 'LOGNARA_RELAY_TOKEN') $testProjects.setup.Environment['SETUP_POSTGRES_BOOTSTRAP_LOGNARA_RELAY_TOKEN'] 'setup bootstrap token scope'
    Assert-Equal (Get-MountAt (Get-Service $setup 'settings') '/app/logs').source 'lognara-buffer-settings-setup' 'setup settings buffer volume'
    $setupInfrastructureOverrides = @{
        SETUP_POSTGRES_LOGNARA_RELAY_TOKEN = 'token-setup-postgres'
        SETUP_POSTGRES_BOOTSTRAP_LOGNARA_RELAY_TOKEN = 'token-setup-bootstrap'
    }
    $setupInfrastructure = Invoke-ComposeConfig $testProjects.setup $setupInfrastructureOverrides
    Assert-Equal (Get-Property (Get-Service $setupInfrastructure 'postgres').environment 'LOGNARA_RELAY_TOKEN') 'token-setup-postgres' 'setup postgres token scope'
    Assert-Equal (Get-Property (Get-Service $setupInfrastructure 'postgres-bootstrap').environment 'LOGNARA_RELAY_TOKEN') 'token-setup-bootstrap' 'setup bootstrap token scope'
    Write-Output '[PASS] setup overlay shares the external relay and isolates setup/settings storage'

    $client = Invoke-ComposeConfig $testProjects.clientstorage
    Assert-Flags $client 'clientstorage-nightly' 'CLIENTSTORAGE' 'true' 'false' 'true'
    $website = Invoke-ComposeConfig $testProjects.website
    Assert-Flags $website 'webserver-nightly' 'WEBSERVER' 'true' 'false' 'true'
    $navigator = Invoke-ComposeConfig $testProjects.navigator
    Assert-Flags $navigator 'navigator-dev' 'NAVIGATOR' 'true' 'false' 'true'
    Assert-True (Has-Property (Get-Service $navigator 'navigator-dev').networks 'barkfluff-network') 'Navigator must reach the shared relay network.'
    $proxy = Invoke-ComposeConfig $testProjects.proxy
    Assert-Flags $proxy 'web' 'WEB_PROXY' 'true' 'false' 'true'
    Assert-True (Has-Property (Get-Service $proxy 'web').networks 'barkfluff-network') 'Proxy Web must reach the shared relay network.'
    Assert-True (Has-Property (Get-Service $proxy 'nginx').networks 'barkfluff-network') 'Proxy nginx must reach the shared relay network.'
    $nginx = Invoke-ComposeConfig $testProjects.nginx
    Assert-True (Has-Property $nginx.services 'nginx') 'Standalone nginx overlay must render.'
    Assert-Equal ((Get-Service $nginx 'nginx').command -join ' ') 'nginx -g daemon off;' 'nginx image default command'
    Assert-Equal ((Get-Service $navigator 'nginx').command -join ' ') 'nginx -g daemon off;' 'navigator nginx image default command'
    Assert-Equal ((Get-Service $proxy 'nginx').command -join ' ') 'nginx -g daemon off;' 'proxy nginx image default command'
    Write-Output '[PASS] clientstorage, website, navigator, proxy, and nginx overlays render with shared relay networking'

    Assert-SequenceEqual (Get-Service $mainBase 'postgres').command (Get-Service $main 'postgres').command 'postgres command preservation'
    Assert-SequenceEqual (Get-Service $mainBase 'redis').command (Get-Service $main 'redis').command 'redis command preservation'
    Assert-SequenceEqual (Get-Service $mainBase 'livekit').command (Get-Service $main 'livekit').command 'livekit command preservation'
    Assert-SequenceEqual (Get-Service $mainBase 'postgres-bootstrap').command (Get-Service $main 'postgres-bootstrap').command 'postgres-bootstrap command preservation'
    Assert-SequenceEqual (Get-Service $setupBase 'postgres-bootstrap').command (Get-Service $setup 'postgres-bootstrap').command 'setup postgres-bootstrap command preservation'
    Assert-SequenceEqual (Get-Service $main 'postgres-bootstrap').entrypoint @('/usr/local/bin/lognara-agent', 'run', '--', '/bin/sh', '-ec') 'bootstrap wrapped entrypoint'
    Assert-SequenceEqual (Get-Service $main 'postgres').entrypoint @('/usr/local/bin/lognara-agent', 'run', '--', '/usr/local/bin/docker-entrypoint.sh') 'postgres wrapped entrypoint'
    Assert-SequenceEqual (Get-Service $main 'rabbitmq').command @('rabbitmq-server') 'rabbitmq image default command'
    Assert-SequenceEqual (Get-Service $main 'seq').command @() 'seq empty default command'
    Assert-SequenceEqual (Get-Service $main 'livekit').entrypoint @('/usr/local/bin/lognara-agent', 'run', '--', '/livekit-server') 'livekit wrapped entrypoint'
    Assert-SequenceEqual (Get-Service $main 'seq').entrypoint @('/usr/local/bin/lognara-agent', 'run', '--', '/bin/seqentry') 'seq wrapped entrypoint'
    Write-Output '[PASS] infrastructure entrypoints and existing command arguments are preserved'

    $minio = Invoke-ComposeConfig $testProjects.main @{} 'lognara-minio'
    $minioService = Get-Service $minio 'minio'
    Assert-SequenceEqual $minioService.entrypoint @('/usr/local/bin/lognara-agent', 'run', '--', '/usr/bin/docker-entrypoint.sh') 'optional MinIO wrapped entrypoint'
    Assert-Equal ($minioService.command -join ' ') 'server /data --console-address :9001' 'optional MinIO sample command'
    Assert-Equal (Get-MountAt $minioService '/var/lib/lognara-agent/spool').source 'lognara-agent-spool-minio' 'optional MinIO spool volume'
    Assert-True (Has-Property (Get-Service $mainBase 'admin-panel').environment 'LognaraEnable') 'Base admin panel flags must be independently configurable.'
    Write-Output '[PASS] optional MinIO wrapper renders only when its profile is requested'

    $dockerfiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Backend') -Filter Dockerfile.slim -Recurse
    Assert-Equal $dockerfiles.Count 21 'slim Dockerfile count'
    foreach ($dockerfile in $dockerfiles) {
        $text = [IO.File]::ReadAllText($dockerfile.FullName)
        Assert-True ($text.Contains('Docker/Lognara/runtime/application-buffer/ /app/logs/')) "$($dockerfile.FullName) must seed /app/logs."
        Assert-True ($text.Contains('Docker/Lognara/runtime/agent-spool/ /var/lib/lognara-agent/spool/')) "$($dockerfile.FullName) must seed the agent spool."
        Assert-True ($text.Contains('COPY --chown=1654:1654')) "$($dockerfile.FullName) must assign persistent paths to UID 1654."
        Assert-True ($text.Contains('--chmod=0755')) "$($dockerfile.FullName) must make the agent executable."
    }
    Write-Output '[PASS] all 21 slim Dockerfiles seed writable buffers and executable agent binaries'

    $workflows = Get-ChildItem -LiteralPath (Join-Path $repoRoot '.github/workflows') -Filter 'build-backend-*.yml'
    Assert-Equal $workflows.Count 21 'backend workflow count'
    foreach ($workflow in $workflows) {
        $text = [IO.File]::ReadAllText($workflow.FullName)
        Assert-True ($text.Contains('Docker/Lognara/**')) "$($workflow.Name) must rebuild when shared agent packaging changes."
    }
    Write-Output '[PASS] all 21 backend workflows watch the shared agent packaging path'
}
finally {
    $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTempRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTempRoot)) {
        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
    }
}
