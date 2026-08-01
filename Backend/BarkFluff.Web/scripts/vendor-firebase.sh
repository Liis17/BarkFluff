#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$SCRIPT_DIR"
npm install --silent
mkdir -p "$PROJECT_ROOT/wwwroot/js/vendor"
"$SCRIPT_DIR/node_modules/.bin/esbuild" "$SCRIPT_DIR/firebase-compat-entry.js" \
  --bundle \
  --format=iife \
  --outfile="$PROJECT_ROOT/wwwroot/js/vendor/firebase-messaging-compat.bundle.js" \
  --target=es2020 \
  --log-level=warning
