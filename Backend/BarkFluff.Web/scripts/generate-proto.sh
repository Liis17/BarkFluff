#!/usr/bin/env bash
# Генерирует JS-клиент gRPC-Web для BarkFluff.
# Требования: protoc, protoc-gen-grpc-web, node, npm. См. generate-proto.ps1 для деталей.

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROTO_DIR="$(cd "$PROJECT_ROOT/../../Shared/BarkFluff.Proto" && pwd)"
TEMP_DIR="$SCRIPT_DIR/.proto-tmp"
OUT_DIR="$PROJECT_ROOT/wwwroot/js/proto"

rm -rf "$TEMP_DIR"
mkdir -p "$TEMP_DIR" "$OUT_DIR"

PROTOS=(shared.proto identity_api.proto users_api.proto messages_api.proto files_api.proto updates_api.proto onliner_api.proto)

echo "[1/3] protoc → $TEMP_DIR"
( cd "$PROTO_DIR" && \
  protoc \
    --proto_path=. \
    --js_out="import_style=commonjs,binary:$TEMP_DIR" \
    --grpc-web_out="import_style=commonjs,mode=grpcwebtext:$TEMP_DIR" \
    "${PROTOS[@]}" )

echo "[2/3] index.js + npm install"
cat > "$TEMP_DIR/index.js" <<'EOF'
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
};

if (typeof window !== 'undefined') window.barkfluff = barkfluff;
module.exports = barkfluff;
EOF

if [ ! -d "$SCRIPT_DIR/node_modules/esbuild" ]; then
    cat > "$SCRIPT_DIR/package.json" <<'EOF'
{
  "name": "barkfluff-proto-bundle",
  "version": "1.0.0",
  "private": true,
  "dependencies": {
    "google-protobuf": "3.21.2",
    "grpc-web": "1.5.0",
    "esbuild": "0.24.0"
  }
}
EOF
    ( cd "$SCRIPT_DIR" && npm install --silent )
fi

echo "[3/3] esbuild → $OUT_DIR/barkfluff.bundle.js"
"$SCRIPT_DIR/node_modules/.bin/esbuild" "$TEMP_DIR/index.js" \
    --bundle \
    --format=iife \
    --global-name=BarkFluffBundle \
    --outfile="$OUT_DIR/barkfluff.bundle.js" \
    --target=es2020 \
    --log-level=warning

rm -rf "$TEMP_DIR"

echo ""
echo "Готово. Подключите: <script src=\"/js/proto/barkfluff.bundle.js\"></script>"
