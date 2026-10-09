#!/usr/bin/env bash
set -euo pipefail

: "${CLIENT_APP:?CLIENT_APP is required}"
: "${CA_FILE:?CA_FILE is required}"
: "${GITHUB_OUTPUT:?GITHUB_OUTPUT is required}"

if [[ "${GITHUB_REF_TYPE:-}" != branch ]]; then
  echo 'Версия клиента вычисляется только для веток' >&2
  exit 1
fi
case "${GITHUB_REF_NAME:-}" in
  nightly|dev) source_channel=nightly ;;
  master) source_channel=dev ;;
  *) echo 'Ветка не привязана к каналу обновлений' >&2; exit 1 ;;
esac

response=$(curl --fail --silent --show-error --connect-timeout 10 --max-time 30 \
  --cacert "$CA_FILE" \
  "https://storage.barkfluff.com/get/${CLIENT_APP}/${source_channel}/version")

version=$(printf '%s' "$response" | python3 -c '
import json, os, re, sys
try:
    data = json.load(sys.stdin)
    version = data.get("version") if isinstance(data, dict) else None
    if not isinstance(version, str) or not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version):
        raise ValueError("ожидается версия MAJOR.MINOR.PATCH")
    if os.environ["GITHUB_REF_NAME"] == "nightly":
        major, minor, patch = map(int, version.split("."))
        version = f"{major}.{minor}.{patch + 1}"
    print(version)
except (ValueError, TypeError) as error:
    sys.exit(f"Некорректная версия ClientStorage: {error}")
')

printf 'Источник: %s/%s, версия сборки: %s\n' "$CLIENT_APP" "$source_channel" "$version"
printf 'version=%s\n' "$version" >> "$GITHUB_OUTPUT"
