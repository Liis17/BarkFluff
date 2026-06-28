<#
.SYNOPSIS
    Генерирует JS-файлы gRPC-Web клиента из .proto файлов BarkFluff и бандлит их
    в один self-contained файл wwwroot/js/proto/barkfluff.bundle.js.

    protoc и protoc-gen-grpc-web скачиваются автоматически, если не найдены в PATH.

.PREREQUISITES
    - Node.js + npm >= 18 (обязательно)
    - (опционально) protoc и protoc-gen-grpc-web в PATH — иначе скачиваются сами

.EXAMPLE
    cd Backend/BarkFluff.Web
    pwsh scripts/generate-proto.ps1
#>

$ErrorActionPreference = 'Stop'

$PROTOC_VERSION       = '3.20.3'
$GRPCWEB_VERSION      = '1.5.0'

$scriptRoot  = Split-Path -Parent $PSCommandPath
$projectRoot = Resolve-Path (Join-Path $scriptRoot '..')
$protoDir    = Resolve-Path (Join-Path $projectRoot '..\..\Shared\BarkFluff.Proto')
$tempDir     = Join-Path $scriptRoot '.proto-tmp'
$outDir      = Join-Path $projectRoot 'wwwroot\js\proto'
$toolsDir    = Join-Path $scriptRoot '.tools'

# --- 0. Убедиться что Node.js есть ---
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js не найден. Установите с https://nodejs.org"
}

# --- 0a. Авто-загрузка protoc ---
$protocExe = (Get-Command protoc -ErrorAction SilentlyContinue)?.Source
if (-not $protocExe) {
    Write-Host "protoc не найден — скачиваю protoc $PROTOC_VERSION..." -ForegroundColor Yellow
    $protocDir = Join-Path $toolsDir "protoc-$PROTOC_VERSION"
    if (-not (Test-Path $protocDir)) {
        New-Item -ItemType Directory -Path $protocDir -Force | Out-Null
        $arch = if ([System.Environment]::Is64BitOperatingSystem) { 'win64' } else { 'win32' }
        $url  = "https://github.com/protocolbuffers/protobuf/releases/download/v$PROTOC_VERSION/protoc-$PROTOC_VERSION-$arch.zip"
        $zip  = Join-Path $toolsDir "protoc.zip"
        Invoke-WebRequest -Uri $url -OutFile $zip
        Expand-Archive -Path $zip -DestinationPath $protocDir -Force
        Remove-Item $zip
    }
    $protocExe = Join-Path $protocDir 'bin\protoc.exe'
    Write-Host "  → $protocExe" -ForegroundColor Gray
} else {
    Write-Host "protoc найден: $protocExe" -ForegroundColor Gray
}

# --- 0b. Авто-загрузка protoc-gen-grpc-web ---
$grpcwebExe = (Get-Command protoc-gen-grpc-web -ErrorAction SilentlyContinue)?.Source
if (-not $grpcwebExe) {
    Write-Host "protoc-gen-grpc-web не найден — скачиваю v$GRPCWEB_VERSION..." -ForegroundColor Yellow
    $grpcwebDir = Join-Path $toolsDir "protoc-gen-grpc-web-$GRPCWEB_VERSION"
    New-Item -ItemType Directory -Path $grpcwebDir -Force | Out-Null
    $grpcwebExe = Join-Path $grpcwebDir 'protoc-gen-grpc-web.exe'
    if (-not (Test-Path $grpcwebExe)) {
        $url = "https://github.com/grpc/grpc-web/releases/download/$GRPCWEB_VERSION/protoc-gen-grpc-web-$GRPCWEB_VERSION-windows-x86_64.exe"
        Invoke-WebRequest -Uri $url -OutFile $grpcwebExe
    }
    Write-Host "  → $grpcwebExe" -ForegroundColor Gray
} else {
    Write-Host "protoc-gen-grpc-web найден: $grpcwebExe" -ForegroundColor Gray
}

# --- 1. Подготовка каталогов ---
if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
New-Item -ItemType Directory -Path $tempDir | Out-Null
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$protos = @(
    'shared.proto',
    'identity_api.proto',
    'users_api.proto',
    'messages_api.proto',
    'files_api.proto',
    'updates_api.proto',
    'onliner_api.proto',
    'fast_auth_api.proto',
    'calls_api.proto'
)

# --- 2. protoc → commonjs JS + gRPC-Web клиенты ---
Write-Host "[1/3] protoc → $tempDir" -ForegroundColor Cyan
Push-Location $protoDir
try {
    $pluginArg = "--plugin=protoc-gen-grpc-web=$grpcwebExe"
    & $protocExe `
        $pluginArg `
        --proto_path=. `
        --js_out="import_style=commonjs,binary:$tempDir" `
        --grpc-web_out="import_style=commonjs,mode=grpcwebtext:$tempDir" `
        $protos
    if ($LASTEXITCODE -ne 0) { throw "protoc failed ($LASTEXITCODE)" }
} finally {
    Pop-Location
}

# --- 3. Собираем точку входа (index.js) для esbuild ---
Write-Host "[2/3] Пишу index.js для бандла..." -ForegroundColor Cyan
$indexPath = Join-Path $tempDir 'index.js'
$indexContent = @"
// Автогенерируется generate-proto.ps1 — все сгенерённые модули собираются в window.barkfluff
const jspb = require('google-protobuf');
const grpcWeb = require('grpc-web');

const sharedPb    = require('./shared_pb.js');
const identityPb  = require('./identity_api_pb.js');
const identitySvc = require('./identity_api_grpc_web_pb.js');
const usersPb     = require('./users_api_pb.js');
const usersSvc    = require('./users_api_grpc_web_pb.js');
const messagesPb  = require('./messages_api_pb.js');
const messagesSvc = require('./messages_api_grpc_web_pb.js');
const filesPb     = require('./files_api_pb.js');
const filesSvc    = require('./files_api_grpc_web_pb.js');
const updatesPb   = require('./updates_api_pb.js');
const updatesSvc  = require('./updates_api_grpc_web_pb.js');
const onlinerPb   = require('./onliner_api_pb.js');
const onlinerSvc  = require('./onliner_api_grpc_web_pb.js');
const fastAuthPb  = require('./fast_auth_api_pb.js');
const fastAuthSvc = require('./fast_auth_api_grpc_web_pb.js');
const callsPb     = require('./calls_api_pb.js');
const callsSvc    = require('./calls_api_grpc_web_pb.js');

// В protoc-gen-js (commonjs) сообщения экспортируются в global proto.<package>.<Message>
// Мы прокидываем глобальный proto namespace наружу как window.barkfluff.proto
// Клиенты же экспортируются из *_grpc_web_pb.js — их берём напрямую
const barkfluff = {
    grpcWeb: grpcWeb,
    proto: (typeof proto !== 'undefined') ? proto : (globalThis.proto || {}),
    IdentityApiClient:          identitySvc.IdentityApiClient          || identitySvc.IdentityApiPromiseClient,
    IdentityApiPromiseClient:   identitySvc.IdentityApiPromiseClient,
    UsersApiClient:             usersSvc.UsersApiClient                || usersSvc.UsersApiPromiseClient,
    UsersApiPromiseClient:      usersSvc.UsersApiPromiseClient,
    MessagesApiClient:          messagesSvc.MessagesApiClient          || messagesSvc.MessagesApiPromiseClient,
    MessagesApiPromiseClient:   messagesSvc.MessagesApiPromiseClient,
    FilesApiClient:             filesSvc.FilesApiClient                || filesSvc.FilesApiPromiseClient,
    FilesApiPromiseClient:      filesSvc.FilesApiPromiseClient,
    UpdatesApiClient:           updatesSvc.UpdatesApiClient            || updatesSvc.UpdatesApiPromiseClient,
    UpdatesApiPromiseClient:    updatesSvc.UpdatesApiPromiseClient,
    OnlinerApiClient:           onlinerSvc.OnlinerApiClient            || onlinerSvc.OnlinerApiPromiseClient,
    OnlinerApiPromiseClient:    onlinerSvc.OnlinerApiPromiseClient,
    FastAuthApiClient:          fastAuthSvc.FastAuthApiClient          || fastAuthSvc.FastAuthApiPromiseClient,
    FastAuthApiPromiseClient:   fastAuthSvc.FastAuthApiPromiseClient,
    CallsApiClient:             callsSvc.CallsApiClient                || callsSvc.CallsApiPromiseClient,
    CallsApiPromiseClient:      callsSvc.CallsApiPromiseClient,
};

if (typeof window !== 'undefined') window.barkfluff = barkfluff;
module.exports = barkfluff;
"@
Set-Content -Path $indexPath -Value $indexContent -Encoding UTF8

# --- 4. Устанавливаем esbuild + google-protobuf + grpc-web локально в scripts/node_modules ---
Push-Location $scriptRoot
try {
    if (-not (Test-Path (Join-Path $scriptRoot 'node_modules\esbuild'))) {
        Write-Host "Устанавливаю esbuild, google-protobuf, grpc-web в scripts/node_modules..." -ForegroundColor Cyan
        $pkgJson = @"
{
  "name": "barkfluff-proto-bundle",
  "version": "1.0.0",
  "private": true,
  "dependencies": {
    "google-protobuf": "3.21.2",
    "grpc-web": "1.5.0",
    "esbuild": "0.24.0",
    "livekit-client": "2.19.2"
  }
}
"@
        Set-Content -Path (Join-Path $scriptRoot 'package.json') -Value $pkgJson
        & npm install --silent
        if ($LASTEXITCODE -ne 0) { throw "npm install failed ($LASTEXITCODE)" }
    }
} finally {
    Pop-Location
}

# --- 5. esbuild → одиночный bundle ---
Write-Host "[3/3] esbuild → $outDir\barkfluff.bundle.js" -ForegroundColor Cyan
$esbuild = Join-Path $scriptRoot 'node_modules\.bin\esbuild.cmd'
if (-not (Test-Path $esbuild)) { $esbuild = Join-Path $scriptRoot 'node_modules\.bin\esbuild' }

& $esbuild $indexPath `
    --bundle `
    --format=iife `
    --global-name=BarkFluffBundle `
    --outfile="$outDir\barkfluff.bundle.js" `
    --target=es2020 `
    --log-level=warning

if ($LASTEXITCODE -ne 0) { throw "esbuild failed ($LASTEXITCODE)" }

# --- 6. Очистка ---
Remove-Item -Recurse -Force $tempDir

Write-Host "`nГотово. Подключите в HTML:" -ForegroundColor Green
Write-Host "  <script src=`"/js/proto/barkfluff.bundle.js`"></script>" -ForegroundColor Gray
Write-Host "  // доступно: window.barkfluff.{IdentityApiClient, proto, grpcWeb, ...}" -ForegroundColor Gray
