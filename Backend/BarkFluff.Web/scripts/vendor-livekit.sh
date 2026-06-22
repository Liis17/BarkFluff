#!/usr/bin/env bash
# Бандлит livekit-client (ESM) в один self-contained browser-глобал window.LivekitClient.
# Результат: wwwroot/js/vendor/livekit-client.bundle.js — подключается обычным <script>.
# LiveKit JS SDK меняется редко, поэтому это отдельный скрипт (не часть generate-proto).
# Кроссплатформенно: см. vendor-livekit.ps1 для Windows.
#
# Требования: Node.js + npm. esbuild и livekit-client ставятся локально в scripts/node_modules.

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT_DIR="$PROJECT_ROOT/wwwroot/js/vendor"
ENTRY="$SCRIPT_DIR/.livekit-entry.js"

if ! command -v node >/dev/null 2>&1; then
    echo "Node.js не найден. Установите с https://nodejs.org" >&2
    exit 1
fi

mkdir -p "$OUT_DIR"

# 1. Зависимости (esbuild + livekit-client) в scripts/node_modules
if [ ! -d "$SCRIPT_DIR/node_modules/livekit-client" ] || [ ! -d "$SCRIPT_DIR/node_modules/esbuild" ]; then
    echo "Устанавливаю зависимости (esbuild, livekit-client)..."
    ( cd "$SCRIPT_DIR" && npm install --silent )
fi

# 2. Точка входа: ре-экспорт всего публичного API livekit-client в глобал
echo "export * from 'livekit-client';" > "$ENTRY"

# 3. esbuild → IIFE-глобал window.LivekitClient
echo "esbuild → $OUT_DIR/livekit-client.bundle.js"
"$SCRIPT_DIR/node_modules/.bin/esbuild" "$ENTRY" \
    --bundle \
    --format=iife \
    --global-name=LivekitClient \
    --outfile="$OUT_DIR/livekit-client.bundle.js" \
    --target=es2020 \
    --minify \
    --log-level=warning

rm -f "$ENTRY"

echo ""
echo "Готово. Подключите в HTML: <script src=\"/js/vendor/livekit-client.bundle.js\"></script>"
echo "  // доступно: window.LivekitClient.{Room, RoomEvent, Track, ConnectionState, ...}"
