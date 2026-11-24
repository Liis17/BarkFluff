param(
    [ValidateSet('x86_64-pc-windows-msvc', 'i686-pc-windows-msvc', 'aarch64-pc-windows-msvc')]
    [string] $Target = 'x86_64-pc-windows-msvc',
    [switch] $Test
)

$ErrorActionPreference = 'Stop'
if (-not $env:PROTOC -and -not (Get-Command protoc -ErrorAction SilentlyContinue)) {
    $protoProject = Join-Path $PSScriptRoot '../../Shared/BarkFluff.Proto/BarkFluff.Proto.csproj'
    $version = ([xml](Get-Content -LiteralPath $protoProject -Raw)).Project.ItemGroup.PackageReference |
        Where-Object Include -eq 'Grpc.Tools' | ForEach-Object Version
    $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $protoc = Join-Path $packages "grpc.tools/$version/tools/windows_x64/protoc.exe"
    if (-not (Test-Path -LiteralPath $protoc)) {
        dotnet restore $protoProject
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $env:PROTOC = $protoc
}

Push-Location $PSScriptRoot
try {
    $toolchain = [regex]::Match(
        (Get-Content -LiteralPath 'rust-toolchain.toml' -Raw), '(?m)^channel\s*=\s*"([^"]+)"'
    ).Groups[1].Value
    if (-not $toolchain) { throw 'Missing channel in rust-toolchain.toml' }
    rustup toolchain install $toolchain --profile minimal --no-self-update
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    rustup target add --toolchain $toolchain $Target
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    cargo "+$toolchain" build --locked --release --target $Target
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($Test) {
        cargo "+$toolchain" test --locked --release --target $Target
    }
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
