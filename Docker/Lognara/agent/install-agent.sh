#!/bin/sh
set -eu

release='agent-20261018-041923-6db0c80dd298'
target_arch="${1:?TARGETARCH is required}"

case "$target_arch" in
  amd64|arm64) archive="lognara-agent-linux-${target_arch}.tar.gz" ;;
  *) echo "unsupported TARGETARCH: $target_arch" >&2; exit 1 ;;
esac

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

curl --fail --location --silent --show-error \
  "https://github.com/Liis17/Lognara/releases/download/${release}/${archive}" \
  --output "${work_dir}/${archive}"

cd "$work_dir"
grep -F "  ${archive}" /tmp/SHA256SUMS | sha256sum --check --strict -
tar -xzf "$archive" -C /usr/local/bin lognara-agent
chmod 0755 /usr/local/bin/lognara-agent
